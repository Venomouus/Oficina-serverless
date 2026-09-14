using Amazon.SecretsManager;
using Npgsql;

namespace Oficina.Autenticacao;

public sealed class AutenticacaoRuntime : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RsaEmissorToken _emissor;
    public AutenticacaoHttp Http { get; }

    private AutenticacaoRuntime(NpgsqlDataSource dataSource, RsaEmissorToken emissor, AutenticacaoOptions options)
    {
        _dataSource = dataSource;
        _emissor = emissor;
        Http = new AutenticacaoHttp(new AutenticarCliente(new PostgresClienteConsulta(dataSource), emissor), emissor, options);
    }

    public static AutenticacaoRuntime FromEnvironment() =>
        Criar(ConfiguracaoAutenticacao.CarregarAsync(Environment.GetEnvironmentVariable).GetAwaiter().GetResult());

    public static async Task<AutenticacaoRuntime> FromAwsEnvironmentAsync(CancellationToken cancellationToken)
    {
        using var client = new AmazonSecretsManagerClient(new AmazonSecretsManagerConfig { MaxErrorRetry = 1 });
        var configuration = await ConfiguracaoAutenticacao.CarregarAsync(
            Environment.GetEnvironmentVariable, new LeitorSegredosAws(client), cancellationToken);
        return Criar(configuration);
    }

    public static AutenticacaoRuntime Criar(ConfiguracaoAutenticacao configuration)
    {
        var emissor = new RsaEmissorToken(configuration.PrivateKeyPem, configuration.Options);
        try
        {
            return new AutenticacaoRuntime(NpgsqlDataSource.Create(configuration.ConnectionString), emissor, configuration.Options);
        }
        catch { emissor.Dispose(); throw; }
    }

    public void Dispose() { _dataSource.Dispose(); _emissor.Dispose(); }
}
