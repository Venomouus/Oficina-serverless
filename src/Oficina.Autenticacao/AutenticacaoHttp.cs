using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Oficina.Autenticacao;

public sealed record RespostaHttp(int StatusCode, string Body, Dictionary<string, string> Headers);

// Adaptador compartilhado pelo evento HTTP API v2 da Lambda e pelo host local.
public sealed class AutenticacaoHttp(AutenticarCliente autenticar, RsaEmissorToken emissor, AutenticacaoOptions options)
{
    public const int MaxBodyBytes = 2048;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RespostaHttp> ExecutarAsync(string? method, string? path, string? body,
        bool base64, string? correlationId, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var id = Correlacao(correlationId);
        var operation = path switch { "/auth/cpf" => "autenticar", "/.well-known/jwks.json" => "jwks",
            "/.well-known/openid-configuration" => "discovery", _ => "rota_desconhecida" };
        RespostaHttp response;
        string? errorType = null;
        try
        {
            if (method == "GET" && path == "/.well-known/jwks.json")
                response = Responder(200, emissor.PublicarJwks(), id, cachePublico: true);
            else if (method == "GET" && path == "/.well-known/openid-configuration")
                response = Responder(200, new { issuer = options.Issuer, jwks_uri = options.Issuer + "/.well-known/jwks.json" }, id, cachePublico: true);
            else if (operation == "rota_desconhecida")
                response = Responder(404, new { message = "Rota nao encontrada." }, id);
            else if (method != "POST" || path != "/auth/cpf")
                response = Responder(405, new { message = "Metodo nao permitido." }, id);
            else
            {
                var request = LerCorpo(body, base64);
                var token = await autenticar.ExecutarAsync(request.Cpf, cancellationToken);
                response = Responder(200, token, id);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException)
        {
            response = Responder(400, new { message = "Informe um CPF valido em um corpo JSON." }, id);
        }
        catch (UnauthorizedAccessException)
        {
            response = Responder(401, new { message = "Cliente nao autorizado." }, id);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            errorType = ex.GetType().Name;
            response = Responder(503, new { message = "Autenticacao temporariamente indisponivel." }, id);
        }
        catch (Exception ex)
        {
            errorType = ex.GetType().Name;
            response = Responder(500, new { message = "Falha ao processar autenticacao." }, id);
        }
        // Nunca registrar corpo, CPF, token, chave ou mensagem/stack da excecao.
        Console.WriteLine(JsonSerializer.Serialize(new { correlationId = id, operation, statusCode = response.StatusCode,
            durationMs = watch.ElapsedMilliseconds, errorType }, Json));
        return response;
    }

    public static RespostaHttp Indisponivel(string? correlationId)
    {
        var id = Correlacao(correlationId);
        Console.WriteLine(JsonSerializer.Serialize(new { correlationId = id, operation = "inicializacao", statusCode = 503 }, Json));
        return Responder(503, new { message = "Autenticacao temporariamente indisponivel." }, id);
    }

    private static PedidoCpf LerCorpo(string? body, bool base64)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyBytes * 2)
            throw new ArgumentException("Corpo invalido.");
        var bytes = base64 ? Convert.FromBase64String(body) : Encoding.UTF8.GetBytes(body);
        if (bytes.Length > MaxBodyBytes) throw new ArgumentException("Corpo invalido.");
        return JsonSerializer.Deserialize<PedidoCpf>(bytes, Json) ?? throw new ArgumentException("Corpo invalido.");
    }

    private static string Correlacao(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 128
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':') ? id : Guid.NewGuid().ToString("N");

    private static RespostaHttp Responder(int code, object body, string id, bool cachePublico = false) => new(code,
        JsonSerializer.Serialize(body, Json), new()
        {
            ["Content-Type"] = "application/json; charset=utf-8",
            ["Cache-Control"] = cachePublico ? "public, max-age=300" : "no-store",
            ["X-Correlation-ID"] = id
        });

    private sealed record PedidoCpf(string? Cpf);
}
