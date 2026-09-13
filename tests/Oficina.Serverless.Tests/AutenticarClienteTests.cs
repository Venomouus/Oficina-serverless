using Oficina.Autenticacao;
using Xunit;

namespace Oficina.Serverless.Tests;

public class AutenticarClienteTests
{
    [Fact]
    public async Task CpfInvalidoNaoConsultaBaseNemEmiteToken()
    {
        var consulta = new ConsultaFake(new(Guid.NewGuid(), true));
        var emissor = new EmissorFake();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AutenticarCliente(consulta, emissor).ExecutarAsync("00000000000"));
        Assert.Null(consulta.CpfConsultado);
        Assert.Null(emissor.ClienteId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ClienteInexistenteOuInativoNaoRecebeToken(bool existe, bool ativo)
    {
        var consulta = new ConsultaFake(existe ? new(Guid.NewGuid(), ativo) : null);
        var emissor = new EmissorFake();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new AutenticarCliente(consulta, emissor).ExecutarAsync("52998224725"));
        Assert.Null(emissor.ClienteId);
    }

    [Fact]
    public async Task ClienteAtivoConsultaCpfNormalizadoEEmiteParaIdentificadorDaBase()
    {
        var clienteId = Guid.NewGuid();
        var consulta = new ConsultaFake(new(clienteId, true));
        var emissor = new EmissorFake();
        using var cancellation = new CancellationTokenSource();
        var result = await new AutenticarCliente(consulta, emissor)
            .ExecutarAsync("529.982.247-25", cancellation.Token);

        Assert.Equal("52998224725", consulta.CpfConsultado);
        Assert.Equal(clienteId, emissor.ClienteId);
        Assert.Equal(cancellation.Token, consulta.CancellationToken);
        Assert.Equal(cancellation.Token, emissor.CancellationToken);
        Assert.Same(emissor.Resultado, result);
    }

    [Fact]
    public async Task FalhaNaConsultaNaoEmiteToken()
    {
        var emissor = new EmissorFake();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            new AutenticarCliente(new ConsultaIndisponivel(), emissor).ExecutarAsync("52998224725"));
        Assert.Null(emissor.ClienteId);
    }

    private sealed class ConsultaFake(ClienteAutenticacao? cliente) : IClienteConsulta
    {
        public string? CpfConsultado { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpf, CancellationToken cancellationToken)
        {
            CpfConsultado = cpf;
            CancellationToken = cancellationToken;
            return Task.FromResult(cliente);
        }
    }

    private sealed class ConsultaIndisponivel : IClienteConsulta
    {
        public Task<ClienteAutenticacao?> BuscarPorCpfAsync(string cpf, CancellationToken cancellationToken)
            => throw new TimeoutException("Base indisponivel.");
    }

    private sealed class EmissorFake : IEmissorToken
    {
        // Sentinela de teste, nao e um JWT e nao e aceita por uma API.
        public TokenAcesso Resultado { get; } = new("token-apenas-de-teste", 900);
        public Guid? ClienteId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<TokenAcesso> EmitirAsync(Guid clienteId, CancellationToken cancellationToken)
        {
            ClienteId = clienteId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Resultado);
        }
    }
}
