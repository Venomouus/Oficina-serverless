using System.Security.Cryptography;
using System.Text;
using Oficina.Autenticacao;

if (args.Length == 2 && args[0] == "--generate-dev-key")
{
    using var rsa = RSA.Create(2048);
    using var file = new FileStream(Path.GetFullPath(args[1]), FileMode.CreateNew, FileAccess.Write);
    using var writer = new StreamWriter(file);
    writer.Write(rsa.ExportPkcs8PrivateKeyPem());
    Console.WriteLine("Chave local criada. Mantenha o arquivo fora do Git.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:5081");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = AutenticacaoHttp.MaxBodyBytes);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
using var runtime = AutenticacaoRuntime.FromEnvironment();
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapPost("/auth/cpf", (Func<HttpContext, Task>)Encaminhar).Accepts<PedidoCpfLocal>("application/json").Produces<TokenAcesso>()
    .Produces(400).Produces(401).Produces(503);
app.MapGet("/.well-known/jwks.json", (Func<HttpContext, Task>)Encaminhar);
app.MapGet("/.well-known/openid-configuration", (Func<HttpContext, Task>)Encaminhar);
app.MapFallback(Encaminhar);
app.Run();

async Task Encaminhar(HttpContext context)
{
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync(context.RequestAborted);
    var result = await runtime.Http.ExecutarAsync(context.Request.Method, context.Request.Path.Value,
        body, false, context.TraceIdentifier, context.RequestAborted);
    context.Response.StatusCode = result.StatusCode;
    foreach (var header in result.Headers) context.Response.Headers[header.Key] = header.Value;
    await context.Response.WriteAsync(result.Body, context.RequestAborted);
}

public sealed record PedidoCpfLocal(string Cpf);
