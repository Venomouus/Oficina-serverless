using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public class PostgresClienteConsultaTests
{
    [PostgresFact]
    public async Task ConsultaRealDeveTratarAtivoInativoLegadoAusenteEAmbiguidade()
    {
        await using var db = await BancoTeste.CriarAsync();
        var id = Guid.NewGuid();
        await db.InserirAsync(id, "52998224725", true);
        var consulta = new PostgresClienteConsulta(db.DataSource);
        Assert.Equal(new ClienteAutenticacao(id, true), await consulta.BuscarPorCpfAsync("52998224725", default));
        Assert.Null(await consulta.BuscarPorCpfAsync("12345678909", default));
        await Assert.ThrowsAsync<ArgumentException>(() => consulta.BuscarPorCpfAsync("' OR 1=1 --", default));
        await db.InserirAsync(Guid.NewGuid(), "123.456.789-09", false);
        Assert.False((await consulta.BuscarPorCpfAsync("12345678909", default))!.Ativo);
        await db.InserirAsync(Guid.NewGuid(), "529.982.247-25", true);
        Assert.Null(await consulta.BuscarPorCpfAsync("52998224725", default));
    }

    [PostgresFact]
    public async Task CasoDeUsoComPostgresERsaDeveNegarInativoEAssinarParaClienteDaBase()
    {
        await using var db = await BancoTeste.CriarAsync();
        var id = Guid.NewGuid();
        await db.InserirAsync(id, "52998224725", true);
        await db.InserirAsync(Guid.NewGuid(), "12345678909", false);
        using var rsa = RSA.Create(2048);
        using var emissor = new RsaEmissorToken(rsa.ExportPkcs8PrivateKeyPem(), RsaEmissorTokenTests.Options);
        var auth = new AutenticarCliente(new PostgresClienteConsulta(db.DataSource), emissor);
        var token = await auth.ExecutarAsync("529.982.247-25");
        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, RsaEmissorTokenTests.Parameters(emissor));
        Assert.True(validated.IsValid);
        Assert.Equal(id.ToString(), new JsonWebToken(token.AccessToken).Subject);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.ExecutarAsync("12345678909"));
    }

    // Cada teste usa schema exclusivo dentro de um banco explicitamente de testes.
    // Nunca executa migrations nem modifica tabelas da aplicacao principal.
    private sealed class BancoTeste(string connectionString, string schema, NpgsqlDataSource source) : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource => source;
        public static async Task<BancoTeste> CriarAsync()
        {
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OFICINA_TEST_POSTGRES"));
            if (connection.Database is null || !connection.Database.StartsWith("oficina_auth_test", StringComparison.Ordinal))
                throw new InvalidOperationException("Use somente um banco oficina_auth_test* dedicado aos testes.");
            var schema = "auth_test_" + Guid.NewGuid().ToString("N");
            var original = connection.ConnectionString;
            await using (var admin = new NpgsqlConnection(original))
            {
                await admin.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}; CREATE TABLE {schema}.\"Clientes\" (\"Id\" uuid PRIMARY KEY, \"CpfCnpj\" varchar(14) UNIQUE NOT NULL, \"Ativo\" boolean NOT NULL DEFAULT true)", admin);
                await command.ExecuteNonQueryAsync();
            }
            connection.SearchPath = schema;
            return new BancoTeste(original, schema, NpgsqlDataSource.Create(connection.ConnectionString));
        }

        public async Task InserirAsync(Guid id, string cpf, bool ativo)
        {
            await using var command = source.CreateCommand("INSERT INTO \"Clientes\" (\"Id\",\"CpfCnpj\",\"Ativo\") VALUES ($1,$2,$3)");
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(cpf); command.Parameters.AddWithValue(ativo);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await source.DisposeAsync();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OFICINA_TEST_POSTGRES")))
            Skip = "Defina OFICINA_TEST_POSTGRES para executar contra PostgreSQL de testes (obrigatorio no CI).";
    }
}
