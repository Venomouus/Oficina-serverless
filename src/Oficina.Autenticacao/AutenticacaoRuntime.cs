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

    public static AutenticacaoRuntime FromEnvironment()
    {
        static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Configuracao obrigatoria ausente: {name}.");
        var seconds = Environment.GetEnvironmentVariable("JWT_LIFETIME_SECONDS") ?? "900";
        if (!int.TryParse(seconds, out var lifetime)) throw new InvalidOperationException("JWT_LIFETIME_SECONDS invalido.");
        var options = new AutenticacaoOptions(Required("JWT_ISSUER"), Required("JWT_AUDIENCE"), Required("JWT_KEY_ID"), lifetime);
        options.Validar();
        var pem = Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_PEM");
        var file = Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_FILE");
        if (!string.IsNullOrEmpty(pem) && !string.IsNullOrEmpty(file))
            throw new InvalidOperationException("Configure apenas uma fonte de chave RSA.");
        if (string.IsNullOrWhiteSpace(pem)) pem = File.ReadAllText(Required("JWT_PRIVATE_KEY_FILE"));
        var connection = new NpgsqlConnectionStringBuilder(Required("DB_CONNECTION_STRING"))
        {
            MaxPoolSize = 5, Timeout = 5, CommandTimeout = 5, IncludeErrorDetail = false,
            ApplicationName = "oficina-autenticacao"
        };
        var emissor = new RsaEmissorToken(pem, options);
        try { return new AutenticacaoRuntime(NpgsqlDataSource.Create(connection.ConnectionString), emissor, options); }
        catch { emissor.Dispose(); throw; }
    }

    public void Dispose() { _dataSource.Dispose(); _emissor.Dispose(); }
}
