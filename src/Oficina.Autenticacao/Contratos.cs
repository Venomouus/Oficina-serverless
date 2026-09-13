namespace Oficina.Autenticacao;

public sealed record ClienteAutenticacao(Guid Id, bool Ativo);
public sealed record TokenAcesso(string AccessToken, int ExpiresIn, string TokenType = "Bearer");

public interface IClienteConsulta
{
    Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpfNormalizado, CancellationToken cancellationToken);
}

public interface IEmissorToken
{
    // O adaptador futuro deve emitir JWT assinado, com sub, role, iss, aud e exp.
    Task<TokenAcesso> EmitirAsync(Guid clienteId, CancellationToken cancellationToken);
}
