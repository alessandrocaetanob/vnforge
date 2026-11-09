using VNForge.Core.Jmdict;

namespace VNForge.App.Services;

public enum OfflineDictionaryState
{
    /// <summary>Ainda não há dicionário na pasta do usuário: a primeira consulta oferece o download.</summary>
    NotInstalled,

    /// <summary>O arquivo existe e está sendo lido para a memória (cerca de 50 ms).</summary>
    Loading,
    Ready,
    Installing,

    /// <summary>O download ou a leitura falhou; <see cref="OfflineDictionary.Problem"/> diz por quê.</summary>
    Failed,
}

/// <summary>
/// O dicionário offline do inspetor (#538) visto pelo app: um só para todas as linhas e janelas. O índice do JMdict mora na pasta do usuário e só é baixado quando a pessoa
/// aceita o aviso do inspetor. A consulta é síncrona e local; nenhuma consulta usa a rede.
/// </summary>
public sealed class OfflineDictionary
{
    private readonly JmdictStore store;
    private readonly Func<HttpClient> createHttp;
    private readonly TimeProvider clock;
    private readonly Action<Action> postToUi;
    private volatile JmdictLookup? lookup;
    private volatile string created = string.Empty;
    private volatile CancellationTokenSource? installCancel;
    private readonly TimeSpan? idleTimeout;
    private int lookups;

    public OfflineDictionary(JmdictStore store, Func<HttpClient>? createHttp = null, TimeProvider? clock = null, Action<Action>? postToUi = null, TimeSpan? idleTimeout = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.createHttp = createHttp ?? (() => new HttpClient());
        this.clock = clock ?? TimeProvider.System;
        this.postToUi = postToUi ?? (action => action());
        this.idleTimeout = idleTimeout;
        State = store.IsInstalled ? OfflineDictionaryState.Loading : OfflineDictionaryState.NotInstalled;
        Loaded = State == OfflineDictionaryState.Loading ? Task.Run(LoadIndex) : Task.CompletedTask;
    }

    /// <summary>A leitura do índice instalado; os testes aguardam por ela. Completa na hora quando não há dicionário.</summary>
    public Task Loaded { get; private set; }

    public OfflineDictionaryState State { get; private set; }

    /// <summary>Quanto do download já veio, de 0 a 1; -1 enquanto o servidor não diz o tamanho ou na etapa de montagem do índice.</summary>
    public double Progress { get; private set; }

    /// <summary>A mensagem em português do último erro; nula quando tudo correu bem.</summary>
    public string? Problem { get; private set; }

    /// <summary>A pessoa disse "Agora não" ao aviso de download: o inspetor não oferece de novo até o app abrir outra vez.</summary>
    public bool OfferDismissed { get; private set; }

    /// <summary>A data de geração do JMdict instalado (<c>2026-11-09</c>).</summary>
    public string Version => created;

    /// <summary>O índice passou de <see cref="JmdictStore.StaleAfter"/>: a licença do EDRDG pede atualização regular, então o cartão oferece baixar de novo.</summary>
    public bool IsStale => State == OfflineDictionaryState.Ready && store.IsStale(clock.GetUtcNow());

    public string IndexPath => store.IndexPath;

    /// <summary>Disparado, na thread da interface, a cada mudança de estado ou de andamento.</summary>
    public event EventHandler? Changed;

    /// <summary>A palavra que cobre o caractere; nulo enquanto o dicionário não está pronto ou quando ele não tem a palavra.</summary>
    public JmdictMatch? FindWordAt(string text, int position)
    {
        Interlocked.Increment(ref lookups);
        return lookup?.FindWordAt(text, position);
    }

    /// <summary>Quantas consultas o dicionário já respondeu; os testes conferem que o mouse parado sobre o mesmo caractere não refaz a busca.</summary>
    public int LookupCount => Volatile.Read(ref lookups);

    public void DismissOffer()
    {
        OfferDismissed = true;
        Raise();
    }

    /// <summary>Interrompe o download em andamento; o dicionário que já estava instalado continua valendo e não é erro.</summary>
    public void CancelInstall() => installCancel?.Cancel();

    /// <summary>
    /// Baixa o JMdict e monta o índice (também atualiza um dicionário velho). Nada escapa como exceção: erro e tempo esgotado viram <see cref="Problem"/> e a interface os mostra,
    /// com o dicionário anterior ainda valendo (<see cref="OfflineDictionaryState.Ready"/>) ou, sem ele, <see cref="OfflineDictionaryState.Failed"/>; cancelar (por
    /// <paramref name="cancellationToken"/> ou por <see cref="CancelInstall"/>) volta ao estado de antes sem <see cref="Problem"/>.
    /// </summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (State == OfflineDictionaryState.Installing)
        {
            return;
        }

        Problem = null;
        Progress = -1;
        SetState(OfflineDictionaryState.Installing);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        installCancel = cancel;
        try
        {
            using var http = createHttp();
            var progress = new Progress<JmdictProgress>(p =>
            {
                Progress = p.Stage == JmdictStage.Downloading ? p.Fraction : -1;
                Raise();
            });
            await new JmdictInstaller(http, store, idleTimeout).InstallAsync(progress, cancel.Token).ConfigureAwait(false);
            // Sem o token: com o índice já trocado no disco, a leitura precisa acontecer para o estado e o arquivo ficarem de acordo.
            await Task.Run(LoadIndex, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetState(lookup is null ? OfflineDictionaryState.NotInstalled : OfflineDictionaryState.Ready);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Problem = e is JmdictFileException ? e.Message : $"Não foi possível instalar o dicionário: {e.Message}";
            SetState(lookup is null ? OfflineDictionaryState.Failed : OfflineDictionaryState.Ready);
        }
        finally
        {
            installCancel = null;
        }
    }

    private void LoadIndex()
    {
        try
        {
            var index = store.Open();
            lookup = index is null ? null : new JmdictLookup(index);
            created = index?.Created ?? string.Empty;
            Problem = null;
            SetState(index is null ? OfflineDictionaryState.NotInstalled : OfflineDictionaryState.Ready);
        }
        catch (JmdictFileException e)
        {
            Problem = e.Message;
            SetState(OfflineDictionaryState.Failed);
        }
    }

    private void SetState(OfflineDictionaryState state)
    {
        State = state;
        Raise();
    }

    private void Raise() => postToUi(() => Changed?.Invoke(this, EventArgs.Empty));
}
