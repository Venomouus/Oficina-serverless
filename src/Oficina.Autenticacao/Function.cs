using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Oficina.Autenticacao;

public sealed class Function
{
    private static readonly Lazy<AutenticacaoRuntime> Runtime = new(AutenticacaoRuntime.FromEnvironment);
    private readonly Func<AutenticacaoHttp> _http;
    public Function() : this(() => Runtime.Value.Http) { }
    public Function(Func<AutenticacaoHttp> http) => _http = http;

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var id = request.RequestContext?.RequestId ?? context.AwsRequestId;
        AutenticacaoHttp http;
        try { http = _http(); }
        catch { return Converter(AutenticacaoHttp.Indisponivel(id)); }
        // Reserva tempo para devolver a resposta antes do timeout da Lambda.
        var limite = context.RemainingTime - TimeSpan.FromMilliseconds(250);
        using var cancellation = new CancellationTokenSource(limite > TimeSpan.Zero ? limite : TimeSpan.Zero);
        return Converter(await http.ExecutarAsync(request.RequestContext?.Http?.Method, request.RawPath,
            request.Body, request.IsBase64Encoded, id, cancellation.Token));
    }

    private static APIGatewayHttpApiV2ProxyResponse Converter(RespostaHttp response) => new()
    {
        StatusCode = response.StatusCode, Body = response.Body, Headers = response.Headers, IsBase64Encoded = false
    };
}
