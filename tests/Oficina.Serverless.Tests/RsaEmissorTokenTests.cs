using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public class RsaEmissorTokenTests
{
    internal static readonly AutenticacaoOptions Options = new("https://auth.example.test", "oficina-api", "test-key", 900);

    [Fact]
    public async Task TokenRealDeveValidarComChavePublicaESomentePerfilCliente()
    {
        using var rsa = RSA.Create(2048);
        using var emissor = new RsaEmissorToken(rsa.ExportPkcs8PrivateKeyPem(), Options);
        var clienteId = Guid.NewGuid();
        var token = await emissor.EmitirAsync(clienteId, default);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, Parameters(emissor));
        Assert.True(validation.IsValid, validation.Exception?.GetType().Name);
        var jwt = new JsonWebToken(token.AccessToken);
        Assert.Equal(clienteId.ToString(), jwt.Subject);
        Assert.Equal("Cliente", jwt.GetClaim("role").Value);
        Assert.Equal("oficina:cliente", jwt.GetClaim("scope").Value);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal("at+jwt", jwt.Typ);
        Assert.Equal(Options.KeyId, jwt.Kid);
        Assert.Equal(900, (jwt.ValidTo - jwt.IssuedAt).TotalSeconds);
        Assert.Equal(900, token.ExpiresIn);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("cpf", StringComparison.OrdinalIgnoreCase));
        var second = new JsonWebToken((await emissor.EmitirAsync(clienteId, default)).AccessToken);
        Assert.NotEqual(jwt.Id, second.Id);
        using var jwks = JsonDocument.Parse(JsonSerializer.Serialize(emissor.PublicarJwks()));
        Assert.Equal(new[] { "alg", "e", "kid", "kty", "n", "use" },
            jwks.RootElement.GetProperty("keys")[0].EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task TokenAdulteradoOuAudienceIncorretaDeveSerRejeitado()
    {
        using var rsa = RSA.Create(2048);
        using var emissor = new RsaEmissorToken(rsa.ExportPkcs8PrivateKeyPem(), Options);
        var token = (await emissor.EmitirAsync(Guid.NewGuid(), default)).AccessToken;
        var parts = token.Split('.');
        parts[1] = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("Cliente", "Admin"));
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(string.Join('.', parts), Parameters(emissor))).IsValid);
        var parameters = Parameters(emissor);
        parameters.ValidAudience = "outra-api";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters)).IsValid);
    }

    [Fact]
    public async Task TokenExpiradoDeveSerRejeitado()
    {
        using var rsa = RSA.Create(2048);
        using var emissor = new RsaEmissorToken(rsa.ExportPkcs8PrivateKeyPem(), Options, new RelogioExpirado());
        var token = await emissor.EmitirAsync(Guid.NewGuid(), default);
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, Parameters(emissor))).IsValid);
    }

    [Fact]
    public void ChavePublicaOuFracaNaoPodeAssinar()
    {
        using var rsa = RSA.Create(2048);
        Assert.ThrowsAny<CryptographicException>(() => new RsaEmissorToken(rsa.ExportSubjectPublicKeyInfoPem(), Options));
        using var fraca = RSA.Create(1024);
        Assert.Throws<InvalidOperationException>(() => new RsaEmissorToken(fraca.ExportPkcs8PrivateKeyPem(), Options));
    }

    [Theory]
    [InlineData("http://auth.example.test", 900)]
    [InlineData("https://auth.example.test/", 900)]
    [InlineData("https://auth.example.test", 0)]
    [InlineData("https://auth.example.test", 901)]
    public void ConfiguracaoInseguraDeveSerRejeitada(string issuer, int lifetime) =>
        Assert.Throws<InvalidOperationException>(() => new AutenticacaoOptions(issuer, "api", "key", lifetime).Validar());

    internal static TokenValidationParameters Parameters(RsaEmissorToken emissor) => new()
    {
        ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
        ValidIssuer = Options.Issuer, ValidAudience = Options.Audience, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidAlgorithms = new[] { "RS256" }, ValidTypes = new[] { "at+jwt" }, ClockSkew = TimeSpan.Zero,
        IssuerSigningKeys = new JsonWebKeySet(JsonSerializer.Serialize(emissor.PublicarJwks())).GetSigningKeys()
    };

    private sealed class RelogioExpirado : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(-1);
    }
}
