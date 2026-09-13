using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Oficina.Autenticacao;

public sealed class RsaEmissorToken : IEmissorToken, IDisposable
{
    private readonly RSA _rsa;
    private readonly AutenticacaoOptions _options;
    private readonly TimeProvider _time;
    private readonly SigningCredentials _credentials;
    private readonly object _gate = new();

    public RsaEmissorToken(string privateKeyPem, AutenticacaoOptions options, TimeProvider? timeProvider = null)
    {
        options.Validar();
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _rsa = RSA.Create();
        try
        {
            _rsa.ImportFromPem(privateKeyPem);
            if (_rsa.KeySize < 2048)
                throw new InvalidOperationException("Chave RSA deve ter pelo menos 2048 bits.");
            // Validacao no startup: uma chave publica sozinha nunca permite autenticar.
            _rsa.SignData(new byte[] { 1 }, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = options.KeyId }, SecurityAlgorithms.RsaSha256);
        }
        catch
        {
            _rsa.Dispose();
            throw;
        }
    }

    public Task<TokenAcesso> EmitirAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clienteId == Guid.Empty)
            throw new ArgumentException("Cliente sem identificador.", nameof(clienteId));
        var agora = DateTimeOffset.FromUnixTimeSeconds(_time.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora.AddSeconds(_options.LifetimeSeconds),
            TokenType = "at+jwt",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = clienteId.ToString(),
                ["role"] = "Cliente",
                ["scope"] = "oficina:cliente",
                ["jti"] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = _credentials
        };
        lock (_gate)
        {
            var jwt = new JsonWebTokenHandler().CreateToken(descriptor);
            return Task.FromResult(new TokenAcesso(jwt, _options.LifetimeSeconds));
        }
    }

    public object PublicarJwks()
    {
        lock (_gate)
        {
            var key = _rsa.ExportParameters(false);
            return new { keys = new[] { new
            {
                kty = "RSA", use = "sig", alg = "RS256", kid = _options.KeyId,
                n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!)
            } } };
        }
    }

    public void Dispose() { lock (_gate) _rsa.Dispose(); }
}
