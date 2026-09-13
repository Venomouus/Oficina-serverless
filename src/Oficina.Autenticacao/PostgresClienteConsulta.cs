using Npgsql;

namespace Oficina.Autenticacao;

public sealed class PostgresClienteConsulta(NpgsqlDataSource dataSource) : IClienteConsulta
{
    public async Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpfNormalizado, CancellationToken cancellationToken)
    {
        if (!Cpf.TryNormalize(cpfNormalizado, out var cpf) || cpf != cpfNormalizado)
            throw new ArgumentException("Informe CPF normalizado valido.", nameof(cpfNormalizado));

        // A API antiga tambem pode ter salvo CPF com mascara. Consulta indexavel,
        // parametrizada e limitada: dois cadastros para o mesmo CPF falham fechados.
        var formatado = $"{cpf[..3]}.{cpf.Substring(3, 3)}.{cpf.Substring(6, 3)}-{cpf[9..]}";
        await using var command = dataSource.CreateCommand("""
            SELECT "Id", "Ativo" FROM "Clientes"
            WHERE "CpfCnpj" = $1 OR "CpfCnpj" = $2
            LIMIT 2
            """);
        command.CommandTimeout = 5;
        command.Parameters.AddWithValue(cpf);
        command.Parameters.AddWithValue(formatado);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var cliente = new ClienteAutenticacao(reader.GetGuid(0), reader.GetBoolean(1));
        return await reader.ReadAsync(cancellationToken) ? null : cliente;
    }
}
