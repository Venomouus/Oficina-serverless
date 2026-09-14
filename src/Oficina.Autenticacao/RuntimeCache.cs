namespace Oficina.Autenticacao;

// Cache por ambiente de execucao Lambda. Somente inicializacoes bem-sucedidas ficam armazenadas.
// Atualizacao/rotacao exige novo ambiente de execucao; nao habilitar rotacao automatica de RSA.
public sealed class RuntimeCache(Func<CancellationToken, Task<AutenticacaoRuntime>> factory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AutenticacaoRuntime? _value;

    public async Task<AutenticacaoHttp> ObterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _value) is { } cached) return cached.Http;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_value is null)
            {
                var created = await factory(cancellationToken);
                Volatile.Write(ref _value, created);
            }
            return _value.Http;
        }
        finally { _gate.Release(); }
    }
}
