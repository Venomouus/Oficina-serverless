using System.Security.Cryptography;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;
using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public sealed class ConfiguracaoAwsTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private const string Password = "private-test;Password=nao-injetar";
    private static Dictionary<string, string?> AwsEnvironment() => new()
    {
        ["AWS_LAMBDA_FUNCTION_NAME"] = "oficina-staging-autenticacao",
        ["DB_SECRET_ARN"] = "db-secret",
        ["JWT_SECRET_ARN"] = "jwt-secret",
        ["DB_HOST"] = "oficina.example.us-east-1.rds.amazonaws.com",
        ["DB_NAME"] = "oficina_staging",
        ["DB_USERNAME"] = "oficina_staging_auth",
        ["DB_SSL_ROOT_CERTIFICATE"] = Path.Combine(AppContext.BaseDirectory, "certificates", "rds-global-bundle.pem"),
        ["JWT_ISSUER"] = "https://auth.example.com",
        ["JWT_AUDIENCE"] = "oficina-api"
    };

    private Leitor Secrets() => new(new()
    {
        ["db-secret"] = JsonSerializer.Serialize(new { username = "oficina_staging_auth", password = Password, host = "evil.example.com" }),
        ["jwt-secret"] = JsonSerializer.Serialize(new { keyId = "aws-test", privateKeyPem = _rsa.ExportPkcs8PrivateKeyPem() })
    });

    [Fact]
    public async Task AwsDeveUsarRoleEsperadaTlsECertificadoSemAceitarHostDoSegredo()
    {
        var env = AwsEnvironment();
        var secrets = Secrets();
        var result = await ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets);
        var connection = new NpgsqlConnectionStringBuilder(result.ConnectionString);
        Assert.Equal(env["DB_HOST"], connection.Host);
        Assert.Equal("oficina_staging_auth", connection.Username);
        Assert.Equal("oficina_staging", connection.Database);
        Assert.Equal(Password, connection.Password);
        Assert.Equal(SslMode.VerifyFull, connection.SslMode);
        Assert.Equal(env["DB_SSL_ROOT_CERTIFICATE"], connection.RootCertificate);
        Assert.Equal(5, connection.MaxPoolSize);
        Assert.False(connection.IncludeErrorDetail);
        Assert.Equal("aws-test", result.Options.KeyId);
        Assert.Equal(2, secrets.Calls);
        Assert.DoesNotContain(Password, result.ToString());
        using var runtime = AutenticacaoRuntime.Criar(result);
        var jwks = await runtime.Http.ExecutarAsync("GET", "/.well-known/jwks.json", null, false, "test");
        Assert.Equal(200, jwks.StatusCode);
        Assert.Contains("aws-test", jwks.Body);
        Assert.DoesNotContain("PRIVATE KEY", jwks.Body);
    }

    [Theory]
    [InlineData("DB_CONNECTION_STRING")]
    [InlineData("JWT_PRIVATE_KEY_PEM")]
    [InlineData("JWT_PRIVATE_KEY_FILE")]
    [InlineData("JWT_KEY_ID")]
    public async Task AwsDeveRejeitarFontesLocaisSemConsultarSegredos(string variable)
    {
        var env = AwsEnvironment();
        env[variable] = "valor-proibido";
        var secrets = Secrets();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets));
        Assert.Equal(0, secrets.Calls);
    }

    [Theory]
    [InlineData("DB_SECRET_ARN")]
    [InlineData("JWT_SECRET_ARN")]
    [InlineData("DB_HOST")]
    [InlineData("DB_NAME")]
    [InlineData("DB_USERNAME")]
    [InlineData("DB_SSL_ROOT_CERTIFICATE")]
    public async Task ConfiguracaoAwsIncompletaDeveFalhar(string variable)
    {
        var env = AwsEnvironment();
        env.Remove(variable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, Secrets()));
    }

    [Fact]
    public async Task IssuerHttpDeveSerRejeitadoNaAwsMesmoEmLoopback()
    {
        var env = AwsEnvironment();
        env["JWT_ISSUER"] = "http://127.0.0.1:5081";
        var secrets = Secrets();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets));
        Assert.Equal(0, secrets.Calls);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"username\":\"oficina_admin\",\"password\":\"private-test\"}")]
    [InlineData("{\"username\":\"oficina_staging_auth\",\"password\":\"\"}")]
    public async Task SegredoInvalidoOuMestreDeveFalharSemVazarConteudo(string json)
    {
        var env = AwsEnvironment();
        var secrets = Secrets();
        secrets.Values["db-secret"] = json;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-test", error.ToString());
        Assert.DoesNotContain(json, error.Message);
    }

    [Fact]
    public async Task FalhaSdkDeveSerSanitizadaECancelamentoPreservado()
    {
        var env = AwsEnvironment();
        var secrets = Secrets();
        secrets.Failure = new AmazonSecretsManagerException("private-test") { ErrorCode = "AccessDeniedException" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets));
        Assert.DoesNotContain("private-test", error.ToString());
        Assert.Null(error.InnerException);
        secrets.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets));
    }

    [Fact]
    public async Task ModoLocalDevePreservarConfiguracaoSemConsultarAws()
    {
        var env = new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Host=localhost;Database=oficina;Username=postgres;Password=local",
            ["JWT_PRIVATE_KEY_PEM"] = _rsa.ExportPkcs8PrivateKeyPem(),
            ["JWT_KEY_ID"] = "local",
            ["JWT_ISSUER"] = "http://127.0.0.1:5081",
            ["JWT_AUDIENCE"] = "oficina-api"
        };
        var secrets = Secrets();
        var result = await ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, secrets);
        using var runtime = AutenticacaoRuntime.Criar(result);
        Assert.Equal(0, secrets.Calls);
        Assert.Equal("local", result.Options.KeyId);
        Assert.Equal(200, (await runtime.Http.ExecutarAsync("GET", "/.well-known/jwks.json", null, false, "test")).StatusCode);
    }

    [Fact]
    public async Task CacheDeveTentarNovamenteAposFalhaECompartilharApenasSucesso()
    {
        var env = AwsEnvironment();
        var configuration = await ConfiguracaoAutenticacao.CarregarAsync(env.GetValueOrDefault, Secrets());
        using var runtime = AutenticacaoRuntime.Criar(configuration);
        var calls = 0;
        var cache = new RuntimeCache(async ct =>
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("temporario");
            return runtime;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.ObterAsync(CancellationToken.None));
        var values = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.ObterAsync(CancellationToken.None)));
        Assert.Equal(2, calls);
        Assert.All(values, value => Assert.Same(runtime.Http, value));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ObterAsync(canceled.Token));
    }

    [Fact]
    public async Task AdaptadorAwsDevePedirSomenteVersaoAtualEPropagarToken()
    {
        using var client = new FakeSecretsClient();
        using var cancellation = new CancellationTokenSource();
        Assert.Equal("{}", await new LeitorSegredosAws(client).LerAsync("arn-test", cancellation.Token));
        Assert.Equal("arn-test", client.Request!.SecretId);
        Assert.Equal("AWSCURRENT", client.Request.VersionStage);
        Assert.Equal(cancellation.Token, client.Token);
        client.Value = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeitorSegredosAws(client).LerAsync("arn-test", cancellation.Token));
    }

    public void Dispose() => _rsa.Dispose();

    private sealed class Leitor(Dictionary<string, string> values) : ILeitorSegredos
    {
        public Dictionary<string, string> Values { get; } = values;
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task<string> LerAsync(string arn, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            return Task.FromResult(Values[arn]);
        }
    }

    private sealed class FakeSecretsClient() : AmazonSecretsManagerClient(
        new BasicAWSCredentials("fake-test", "fake-test"), RegionEndpoint.USEast1)
    {
        public GetSecretValueRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public string? Value { get; set; } = "{}";
        public override Task<GetSecretValueResponse> GetSecretValueAsync(GetSecretValueRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Token = cancellationToken;
            return Task.FromResult(new GetSecretValueResponse { SecretString = Value });
        }
    }
}
