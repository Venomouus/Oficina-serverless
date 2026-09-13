using Oficina.Notificacoes;
using Xunit;

namespace Oficina.Serverless.Tests;

public class NotificarOrdemServicoTests
{
    [Fact]
    public async Task EventoValidoPreservaIdentificadoresNoEnvio()
    {
        var sender = new SenderFake();
        var notificacao = new NotificacaoOrdemServico(
            Guid.NewGuid(), Guid.NewGuid(), "cliente@example.com", "Orcamento disponivel.");
        await new NotificarOrdemServico(sender).ExecutarAsync(notificacao);
        Assert.Same(notificacao, sender.Enviada);
    }

    [Theory]
    [InlineData("nao e email")]
    [InlineData("")]
    [InlineData("Nome <cliente@example.com>")]
    public async Task DestinatarioInvalidoNaoAcionaProvedor(string destinatario)
    {
        var sender = new SenderFake();
        await Assert.ThrowsAsync<ArgumentException>(() => new NotificarOrdemServico(sender)
            .ExecutarAsync(new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Mensagem")));
        Assert.Null(sender.Enviada);
    }

    [Fact]
    public async Task EventoSemIdentificadorNaoAcionaProvedor()
    {
        var sender = new SenderFake();
        await Assert.ThrowsAsync<ArgumentException>(() => new NotificarOrdemServico(sender)
            .ExecutarAsync(new(Guid.Empty, Guid.NewGuid(), "cliente@example.com", "Mensagem")));
        Assert.Null(sender.Enviada);
    }

    [Fact]
    public async Task FalhaDoProvedorNaoETratadaComoEntregaBemSucedida()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => new NotificarOrdemServico(new SenderIndisponivel())
            .ExecutarAsync(new(Guid.NewGuid(), Guid.NewGuid(), "cliente@example.com", "Mensagem")));
    }

    private sealed class SenderFake : INotificacaoSender
    {
        public NotificacaoOrdemServico? Enviada { get; private set; }
        public Task EnviarAsync(NotificacaoOrdemServico notificacao, CancellationToken cancellationToken)
        {
            Enviada = notificacao;
            return Task.CompletedTask;
        }
    }

    private sealed class SenderIndisponivel : INotificacaoSender
    {
        public Task EnviarAsync(NotificacaoOrdemServico notificacao, CancellationToken cancellationToken)
            => throw new TimeoutException("Provedor indisponivel.");
    }
}
