namespace Oficina.Autenticacao;

// Caso de uso independente do transporte Lambda/API Gateway.
public sealed class AutenticarCliente(IClienteConsulta clientes, IEmissorToken tokens)
{
    public async Task<TokenAcesso> ExecutarAsync(string? cpf, CancellationToken cancellationToken = default)
    {
        if (!Cpf.TryNormalize(cpf, out var normalized))
            throw new ArgumentException("CPF invalido.", nameof(cpf));

        var cliente = await clientes.BuscarPorCpfAsync(normalized, cancellationToken);
        if (cliente is null || !cliente.Ativo || cliente.Id == Guid.Empty)
            throw new UnauthorizedAccessException("Cliente nao autorizado.");

        return await tokens.EmitirAsync(cliente.Id, cancellationToken);
    }
}
