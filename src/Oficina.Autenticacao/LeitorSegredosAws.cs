using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Oficina.Autenticacao;

public interface ILeitorSegredos
{
    Task<string> LerAsync(string arn, CancellationToken cancellationToken);
}

public sealed class LeitorSegredosAws(IAmazonSecretsManager client) : ILeitorSegredos
{
    public async Task<string> LerAsync(string arn, CancellationToken cancellationToken)
    {
        var response = await client.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = arn,
            VersionStage = "AWSCURRENT"
        }, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.SecretString))
            throw new InvalidOperationException("Segredo deve conter JSON textual.");
        return response.SecretString;
    }
}
