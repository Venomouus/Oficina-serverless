using System.Text.Json;
using Npgsql;

namespace Oficina.Autenticacao;

// Classe, nao record: ToString nao deve revelar a conexao ou o PEM.
public sealed class ConfiguracaoAutenticacao
{
    public string ConnectionString { get; }
    public string PrivateKeyPem { get; }
    public AutenticacaoOptions Options { get; }

    private ConfiguracaoAutenticacao(string connectionString, string pem, AutenticacaoOptions options)
    {
        ConnectionString = connectionString;
        PrivateKeyPem = pem;
        Options = options;
    }

    public static async Task<ConfiguracaoAutenticacao> CarregarAsync(
        Func<string, string?> environment, ILeitorSegredos? secrets = null, CancellationToken cancellationToken = default)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(environment(name))
            ? environment(name)! : throw new InvalidOperationException($"Configuracao obrigatoria ausente: {name}.");
        var seconds = environment("JWT_LIFETIME_SECONDS") ?? "900";
        if (!int.TryParse(seconds, out var lifetime))
            throw new InvalidOperationException("JWT_LIFETIME_SECONDS invalido.");

        var dbArn = environment("DB_SECRET_ARN");
        var keyArn = environment("JWT_SECRET_ARN");
        var aws = !string.IsNullOrEmpty(environment("AWS_LAMBDA_FUNCTION_NAME"))
            || !string.IsNullOrEmpty(dbArn) || !string.IsNullOrEmpty(keyArn);
        string pem;
        string keyId;
        NpgsqlConnectionStringBuilder connection;

        if (aws)
        {
            if (new[] { "DB_CONNECTION_STRING", "JWT_PRIVATE_KEY_FILE", "JWT_PRIVATE_KEY_PEM", "JWT_KEY_ID" }
                .Any(name => !string.IsNullOrEmpty(environment(name))))
                throw new InvalidOperationException("Na AWS, configure apenas ARNs dos segredos e parametros publicos.");
            Required("DB_SECRET_ARN");
            Required("JWT_SECRET_ARN");
            if (secrets is null) throw new InvalidOperationException("Leitor de segredos ausente.");
            if (!Uri.TryCreate(Required("JWT_ISSUER"), UriKind.Absolute, out var issuer) || issuer.Scheme != "https")
                throw new InvalidOperationException("JWT_ISSUER deve ser HTTPS na AWS.");

            // Nunca aceitar host/banco/usuario arbitrarios vindos do JSON do segredo.
            var username = Required("DB_USERNAME");
            var host = Required("DB_HOST");
            var database = Required("DB_NAME");
            var certificate = Required("DB_SSL_ROOT_CERTIFICATE");
            if (!File.Exists(certificate))
                throw new InvalidOperationException("Bundle publico de CA do RDS ausente.");
            if (host.Contains(',') || Uri.CheckHostName(host) != UriHostNameType.Dns)
                throw new InvalidOperationException("DB_HOST deve ser um hostname unico do RDS.");
            try
            {
                using var db = Parse(await secrets.LerAsync(dbArn!, cancellationToken));
                using var key = Parse(await secrets.LerAsync(keyArn!, cancellationToken));
                if (String(db, "username") != username)
                    throw new InvalidOperationException();
                connection = new NpgsqlConnectionStringBuilder
                {
                    Host = host, Port = 5432, Database = database,
                    Username = username, Password = String(db, "password"),
                    SslMode = SslMode.VerifyFull, RootCertificate = certificate
                };
                pem = String(key, "privateKeyPem");
                keyId = String(key, "keyId");
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Sem inner exception: JSON/SDK podem conter material sensivel.
                throw new InvalidOperationException("Nao foi possivel carregar a configuracao dos segredos.");
            }
        }
        else
        {
            pem = environment("JWT_PRIVATE_KEY_PEM") ?? "";
            var file = environment("JWT_PRIVATE_KEY_FILE");
            if (!string.IsNullOrEmpty(pem) && !string.IsNullOrEmpty(file))
                throw new InvalidOperationException("Configure apenas uma fonte de chave RSA.");
            if (string.IsNullOrWhiteSpace(pem))
                pem = File.ReadAllText(Required("JWT_PRIVATE_KEY_FILE"));
            keyId = Required("JWT_KEY_ID");
            connection = new NpgsqlConnectionStringBuilder(Required("DB_CONNECTION_STRING"));
        }

        var options = new AutenticacaoOptions(Required("JWT_ISSUER"), Required("JWT_AUDIENCE"), keyId, lifetime);
        options.Validar();
        connection.MaxPoolSize = 5;
        connection.Timeout = 5;
        connection.CommandTimeout = 5;
        connection.IncludeErrorDetail = false;
        connection.ApplicationName = "oficina-autenticacao";
        return new ConfiguracaoAutenticacao(connection.ConnectionString, pem, options);
    }

    private static JsonDocument Parse(string json)
    {
        if (json.Length > 65536) throw new InvalidOperationException();
        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
    }

    private static string String(JsonDocument document, string property)
    {
        var value = document.RootElement.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException();
    }
}
