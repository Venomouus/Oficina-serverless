namespace Oficina.Autenticacao;

public sealed record AutenticacaoOptions(string Issuer, string Audience, string KeyId, int LifetimeSeconds = 900)
{
    public void Validar()
    {
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || Issuer.EndsWith('/'))
            throw new InvalidOperationException("JWT_ISSUER deve ser HTTPS sem barra final (HTTP apenas em loopback local).");
        if (string.IsNullOrWhiteSpace(Audience) || Audience.Length > 200
            || string.IsNullOrWhiteSpace(KeyId) || KeyId.Length > 100
            || LifetimeSeconds is < 60 or > 900)
            throw new InvalidOperationException("Configuracao JWT invalida; validade permitida entre 60 e 900 segundos.");
    }
}
