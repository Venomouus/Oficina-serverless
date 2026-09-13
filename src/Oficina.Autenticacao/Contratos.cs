namespace Oficina.Autenticacao;

public sealed record ClienteAutenticacao(Guid Id, bool Ativo);
public sealed record TokenAcesso(string AccessToken, int ExpiresIn, string TokenType = "Bearer");

public interface IClienteConsulta
{
    Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpfNormalizado, CancellationToken cancellationToken);
}

public interface IEmissorToken
{
    // Emite token para o identificador retornado pela base, nunca para o CPF informado.
    Task<TokenAcesso> EmitirAsync(Guid clienteId, CancellationToken cancellationToken);
}
