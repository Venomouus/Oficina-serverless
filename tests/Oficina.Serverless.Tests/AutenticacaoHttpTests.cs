using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public class AutenticacaoHttpTests : IDisposable
{
    private readonly RsaEmissorToken _emissor;
    public AutenticacaoHttpTests()
    {
        using var rsa = RSA.Create(2048);
        _emissor = new RsaEmissorToken(rsa.ExportPkcs8PrivateKeyPem(), RsaEmissorTokenTests.Options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"cpf\":123}")]
    [InlineData("{\"cpf\":\"00000000000\"}")]
    public async Task CorpoInvalidoDeveRetornar400SemConsultar(string? body)
    {
        var consulta = new Consulta(null);
        var response = await Http(consulta).ExecutarAsync("POST", "/auth/cpf", body, false, "request-test");
        Assert.Equal(400, response.StatusCode);
        Assert.Equal(0, consulta.Chamadas);
        Assert.Equal("no-store", response.Headers["Cache-Control"]);
    }

    [Fact]
    public async Task Base64InvalidoECorpoGrandeDevemSerRejeitados()
    {
        var consulta = new Consulta(null);
        Assert.Equal(400, (await Http(consulta).ExecutarAsync("POST", "/auth/cpf", "%%%", true, null)).StatusCode);
        Assert.Equal(400, (await Http(consulta).ExecutarAsync("POST", "/auth/cpf", new string('a', 5000), false, null)).StatusCode);
        Assert.Equal(0, consulta.Chamadas);
    }

    [Fact]
    public async Task InexistenteEInativoDevemTerMesmaResposta()
    {
        var inexistente = await Http(new Consulta(null)).ExecutarAsync("POST", "/auth/cpf", "{\"cpf\":\"52998224725\"}", false, "test");
        var inativo = await Http(new Consulta(new(Guid.NewGuid(), false))).ExecutarAsync("POST", "/auth/cpf", "{\"cpf\":\"52998224725\"}", false, "test");
        Assert.Equal(401, inexistente.StatusCode);
        Assert.Equal(inexistente, inativo with { Headers = inexistente.Headers });
    }

    [Fact]
    public async Task BaseIndisponivelNaoDeveExporDetalhes()
    {
        var response = await Http(new Consulta(null, falha: true)).ExecutarAsync("POST", "/auth/cpf", "{\"cpf\":\"52998224725\"}", false, "test");
        Assert.Equal(503, response.StatusCode);
        Assert.DoesNotContain("segredo", response.Body);
        Assert.DoesNotContain("52998224725", response.Body);
    }

    [Fact]
    public async Task DiscoveryEJwksNaoDevemConsultarBanco()
    {
        var consulta = new Consulta(null, falha: true);
        foreach (var path in new[] { "/.well-known/openid-configuration", "/.well-known/jwks.json" })
        {
            var response = await Http(consulta).ExecutarAsync("GET", path, null, false, "test");
            Assert.Equal(200, response.StatusCode);
        }
        Assert.Equal(0, consulta.Chamadas);
        Assert.Equal(404, (await Http(consulta).ExecutarAsync("GET", "/desconhecida", null, false, "test")).StatusCode);
        Assert.Equal(405, (await Http(consulta).ExecutarAsync("GET", "/auth/cpf", null, false, "test")).StatusCode);
    }

    [Fact]
    public async Task EventoLambdaV2Base64DeveRetornarTokenCamelCaseECorrelacao()
    {
        var consulta = new Consulta(new(Guid.NewGuid(), true));
        var serializer = new DefaultLambdaJsonSerializer();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"cpf\":\"529.982.247-25\"}"));
        var json = JsonSerializer.Serialize(new { version = "2.0", rawPath = "/auth/cpf", body = encoded,
            isBase64Encoded = true, requestContext = new { requestId = "gateway-test-123", http = new { method = "POST" } } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var request = serializer.Deserialize<APIGatewayHttpApiV2ProxyRequest>(stream);
        var response = await new Function(() => Http(consulta)).FunctionHandler(request, new Contexto());
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("gateway-test-123", response.Headers["X-Correlation-ID"]);
        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(900, body.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("accessToken").GetString()!.Split('.').Length);
        Assert.Equal("52998224725", consulta.Cpf);
    }

    [Fact]
    public async Task ConfiguracaoLambdaAusenteDeveFalharSemExporSegredo()
    {
        var function = new Function(() => throw new InvalidOperationException("segredo"));
        var response = await function.FunctionHandler(new APIGatewayHttpApiV2ProxyRequest(), new Contexto());
        Assert.Equal(503, response.StatusCode);
        Assert.DoesNotContain("segredo", response.Body);
    }

    private AutenticacaoHttp Http(IClienteConsulta consulta) => new(new AutenticarCliente(consulta, _emissor), _emissor, RsaEmissorTokenTests.Options);
    public void Dispose() => _emissor.Dispose();

    private sealed class Consulta(ClienteAutenticacao? cliente, bool falha = false) : IClienteConsulta
    {
        public int Chamadas { get; private set; }
        public string? Cpf { get; private set; }
        public Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpfNormalizado, CancellationToken cancellationToken)
        {
            Chamadas++; Cpf = cpfNormalizado;
            if (falha) throw new TimeoutException("segredo de conexao");
            return Task.FromResult(cliente);
        }
    }

    private sealed class Contexto : ILambdaContext
    {
        public string AwsRequestId => "lambda-test";
        public IClientContext ClientContext => null!;
        public ICognitoIdentity Identity => null!;
        public string FunctionName => "oficina-auth";
        public string FunctionVersion => "1";
        public string InvokedFunctionArn => "test";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "test";
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 256;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
