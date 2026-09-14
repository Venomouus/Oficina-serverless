using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Oficina.Autenticacao;

public sealed class Function
{
    private static readonly RuntimeCache Runtime = new(AutenticacaoRuntime.FromAwsEnvironmentAsync);
    private readonly Func<CancellationToken, Task<AutenticacaoHttp>> _http;
    public Function() : this(Runtime.ObterAsync) { }
    public Function(Func<AutenticacaoHttp> http) : this(_ => Task.FromResult(http())) { }
    public Function(Func<CancellationToken, Task<AutenticacaoHttp>> http) => _http = http;

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var id = request.RequestContext?.RequestId ?? context.AwsRequestId;
        // Inclui a leitura de segredos no limite total da invocacao.
        var limite = context.RemainingTime - TimeSpan.FromMilliseconds(250);
        using var cancellation = new CancellationTokenSource(limite > TimeSpan.Zero ? limite : TimeSpan.Zero);
        AutenticacaoHttp http;
        try { http = await _http(cancellation.Token).WaitAsync(cancellation.Token); }
        catch { return Converter(AutenticacaoHttp.Indisponivel(id)); }
        return Converter(await http.ExecutarAsync(request.RequestContext?.Http?.Method, request.RawPath,
            request.Body, request.IsBase64Encoded, id, cancellation.Token));
    }

    private static APIGatewayHttpApiV2ProxyResponse Converter(RespostaHttp response) => new()
    {
        StatusCode = response.StatusCode, Body = response.Body, Headers = response.Headers, IsBase64Encoded = false
    };
}
