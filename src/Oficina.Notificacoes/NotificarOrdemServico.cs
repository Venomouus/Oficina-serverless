using System.Net.Mail;

namespace Oficina.Notificacoes;

public sealed record NotificacaoOrdemServico(
    Guid EventoId,
    Guid OrdemServicoId,
    string Destinatario,
    string Mensagem);

public interface INotificacaoSender
{
    Task EnviarAsync(NotificacaoOrdemServico notificacao, CancellationToken cancellationToken);
}

// O consumidor SQS, a deduplicacao persistente e o provedor de e-mail sao proximas etapas.
public sealed class NotificarOrdemServico(INotificacaoSender sender)
{
    public Task ExecutarAsync(NotificacaoOrdemServico notificacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificacao);
        if (notificacao.EventoId == Guid.Empty || notificacao.OrdemServicoId == Guid.Empty)
            throw new ArgumentException("Evento e ordem de servico devem ser identificados.");
        if (!MailAddress.TryCreate(notificacao.Destinatario, out var address)
            || address.Address != notificacao.Destinatario)
            throw new ArgumentException("Destinatario invalido.");
        if (string.IsNullOrWhiteSpace(notificacao.Mensagem))
            throw new ArgumentException("Mensagem obrigatoria.");

        // Falhas do provedor propagam para que o futuro consumidor possa solicitar retry.
        return sender.EnviarAsync(notificacao, cancellationToken);
    }
}
