using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Demo;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Um dos quatro passos numerados da primeira vez no Início.</summary>
public sealed record FirstStep(int Number, string Title, string Description);

/// <summary>Tela Início: abrir um projeto, ver os recentes e o progresso do projeto selecionado.</summary>
public sealed partial class StartViewModel(RecentProjectsFile recents, IFolderPicker picker, TimeProvider clock, string? sampleParent = null) : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly FormattingProfileCache profiles = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedInEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenNextStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    public partial RecentProjectItem? Selected { get; set; }

    /// <summary>Falha ao abrir um projeto: o fato e a consequência, em português.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Aviso que não é erro, como a lista de recentes que não pôde ser gravada.</summary>
    [ObservableProperty]
    public partial string? NoticeMessage { get; set; }

    /// <summary>A lista de recentes existe em disco e não pôde ser lida; diz o arquivo e o que acontece com a lista. Persiste até a lista voltar a ser legível.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentsProblem))]
    public partial string? RecentsProblem { get; private set; }

    public bool HasRecentsProblem => RecentsProblem is not null;

    public ObservableCollection<RecentProjectItem> Recents { get; } = [];

    /// <summary>
    /// Os números do projeto selecionado, lidos pela mesma lógica do Painel: o próximo passo, a frase das horas, o gasto e o estado do jogo.
    /// É uma instância à parte da do Painel, que mostra só o projeto aberto.
    /// </summary>
    public DashboardViewModel Detail { get; } = new(clock: clock);

    /// <summary>
    /// A fala onde a pessoa vai continuar, na caixa do jogo do cartão "Continuar de onde parou": a primeira que espera revisão na cena do próximo passo
    /// ou, sem nenhuma, a última já revisada. Nula quando a cena não tem fala traduzida; o cartão esconde a caixa.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinueLayout))]
    public partial TextboxLayout? ContinueLayout { get; private set; }

    /// <summary>Diz o que a fala da caixa é: "Próxima fala a revisar" ou "Última fala revisada".</summary>
    [ObservableProperty]
    public partial string ContinueLineCaption { get; private set; } = string.Empty;

    public bool HasContinueLayout => ContinueLayout is not null;

    /// <summary>A leitura do <see cref="Detail"/> em curso; os testes esperam por ela.</summary>
    public Task DetailLoading { get; private set; } = Task.CompletedTask;

    /// <summary>A chave do Claude está guardada; a lista "O que você precisa" da primeira vez marca esse item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyNeedText))]
    public partial bool ApiKeyConfigured { get; set; }

    public bool HasRecents => Recents.Count > 0;

    /// <summary>O botão "Experimentar com uma cena de exemplo" só aparece na primeira vez, enquanto não há projeto nos recentes.</summary>
    public bool ShowSampleButton => !HasRecents;

    /// <summary>Os quatro passos da primeira vez, na ordem em que a pessoa os faz.</summary>
    public static IReadOnlyList<FirstStep> FirstSteps { get; } =
    [
        new(1, "Apontar a pasta do jogo", "O app reconhece o jogo e tira o texto dele, sem mexer nos arquivos originais."),
        new(2, "Escolher os acentos", "Mostra como as letras acentuadas aparecem no jogo e deixa você ajustar se algo sair errado."),
        new(3, "Traduzir", "A IA traduz o jogo todo, você diz a qualidade e vê quanto vai custar antes de começar."),
        new(4, "Testar no jogo", "O app copia a tradução para o jogo, com cópia de segurança, para você ver como ficou."),
    ];

    /// <summary>O título da área para soltar a pasta: com projetos ela fala de "outro jogo", na primeira vez, do jogo.</summary>
    public string DropTitle => HasRecents ? "Traduzir outro jogo" : "Comece pela pasta do jogo";

    /// <summary>O que falta ou o que já está pronto da chave do Claude, para a lista "O que você precisa".</summary>
    public string ApiKeyNeedText => ApiKeyConfigured
        ? "A chave já está guardada neste computador."
        : "Crie uma chave na conta da Anthropic e cole nos Ajustes. Sem ela a tradução com IA fica desligada.";

    /// <summary>A pasta que contém o projeto aberto por último, onde os próximos costumam ficar; nula sem recentes.</summary>
    public string? DefaultFolder => Recents.Count == 0 ? null : FolderOf(Recents[0].Path);

    /// <summary>O rodapé da lista: "3 projetos". A pasta padrão fica fora: caminho de pasta só aparece em "Detalhes técnicos".</summary>
    public string RecentsSummary => Recents.Count switch
    {
        0 => "Nenhum projeto",
        1 => "1 projeto",
        var n => $"{n} projetos",
    };

    /// <summary>Um projeto foi aberto pelo seletor de pasta.</summary>
    public event EventHandler<ProjectSummary>? ProjectOpened;

    /// <summary>O usuário pediu o assistente de novo projeto.</summary>
    public event EventHandler? NewProjectRequested;

    /// <summary>Uma pasta de jogo foi solta na área da lista ou escolhida em "Escolher a pasta"; o Novo projeto abre com ela preenchida.</summary>
    public event EventHandler<string>? GameFolderChosen;

    /// <summary>O usuário apertou o botão do "Próximo passo" do projeto selecionado; o projeto já foi aberto e o destino é o do mesmo botão no Painel.</summary>
    public event EventHandler<DashboardNavigation>? NextStepRequested;

    /// <summary>O usuário pediu para continuar o projeto selecionado no Editor; <see cref="ProjectSummary.LastEditedScript"/> diz em que script.</summary>
    public event EventHandler<ProjectSummary>? EditorRequested;

    /// <summary>O usuário abriu o projeto selecionado e quer o Painel dele; o projeto já foi entregue por <see cref="ProjectOpened"/>.</summary>
    public event EventHandler<ProjectSummary>? DashboardRequested;

    /// <summary>Lê a lista de recentes; mantém a seleção (ou seleciona <paramref name="selectPath"/>) quando o item ainda existe.</summary>
    public async Task LoadAsync(string? selectPath = null)
    {
        // Duas cargas ao mesmo tempo (abertura da janela e um projeto aberto logo em seguida) embaralhariam a lista.
        await loadGate.WaitAsync(CancellationToken.None);
        try
        {
            var keep = selectPath ?? Selected?.Path;
            var now = clock.GetUtcNow();
            var list = recents.Read();
            var items = await Task.WhenAll(list.Projects.Select(recent => Task.Run(() => Describe(recent, now), CancellationToken.None)));
            RecentsProblem = list.Problem is { } problem
                ? $"{problem.Replace($"'{recents.Path}'", $"'{System.IO.Path.GetFileName(recents.Path)}'", StringComparison.Ordinal)} A lista aparece vazia; abrir um projeto a recria."
                : null;

            Recents.Clear();
            foreach (var item in items)
            {
                Recents.Add(item);
            }

            // A lista recarregada tem itens novos: o detalhe lê o projeto de novo (o ritmo, o gasto e o próximo passo podem ter mudado fora do Início).
            forceDetailReload = true;
            Selected = Recents.FirstOrDefault(i => SamePath(i.Path, keep)) ?? Recents.FirstOrDefault();
            forceDetailReload = false;
            OnPropertyChanged(nameof(HasRecents));
            OnPropertyChanged(nameof(ShowSampleButton));
            OnPropertyChanged(nameof(DropTitle));
            OnPropertyChanged(nameof(DefaultFolder));
            OnPropertyChanged(nameof(RecentsSummary));
        }
        finally
        {
            loadGate.Release();
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        ClearMessages();
        var path = await picker.PickFolderAsync("Abrir projeto do VNForge");
        if (path is not null)
        {
            await OpenAsync(path);
        }
    }

    /// <summary>
    /// Abre a pasta, registra nos recentes e seleciona. Em caso de falha, mostra <see cref="ErrorMessage"/> e não registra nada.
    /// Devolve o resumo do projeto aberto, ou <c>null</c> quando ele não abriu ou não pôde ser carregado.
    /// </summary>
    public async Task<ProjectSummary?> OpenAsync(string path)
    {
        ProjectSummary summary;
        try
        {
            summary = await Task.Run(() => ProjectSummaryReader.Read(path), CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"{WithoutPath(e.Message, path)} O projeto não foi aberto.";
            return null;
        }

        try
        {
            recents.Add(summary.Root, clock.GetUtcNow());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A lista de recentes é conveniência: o projeto abre mesmo sem ela.
            NoticeMessage = "O projeto abriu, mas não foi possível gravar a lista de recentes.";
        }

        await LoadAsync(summary.Root);
        try
        {
            ProjectOpened?.Invoke(this, summary);
        }
        catch (Exception e) when (IsExpected(e))
        {
            // Os assinantes carregam os arquivos do projeto (glossário, textos); um arquivo ilegível não pode escapar do comando.
            ErrorMessage = $"{WithoutPath(e.Message, path)} O projeto não pôde ser carregado.";
            return null;
        }

        return summary;
    }

    /// <summary>"Abrir projeto" do detalhe: abre de novo a pasta selecionada, para ler o que mudou nela, e leva ao Painel.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private async Task OpenSelectedAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        ClearMessages();
        if (await OpenAsync(item.Path) is { } summary)
        {
            DashboardRequested?.Invoke(this, summary);
        }
    }

    /// <summary>"Abrir" da linha da lista: abre aquele projeto, seja ele o selecionado ou não, e leva ao Painel dele.</summary>
    [RelayCommand]
    private async Task OpenRecentAsync(RecentProjectItem? item)
    {
        if (item is not { IsSupported: true })
        {
            return;
        }

        ClearMessages();
        if (await OpenAsync(item.Path) is { } summary)
        {
            DashboardRequested?.Invoke(this, summary);
        }
    }

    /// <summary>
    /// "Experimentar com uma cena de exemplo": grava (ou reaproveita) o projeto sintético na pasta de exemplos do usuário, nunca na de um jogo,
    /// abre e leva ao Painel. A IA dele é a falsa; não precisa de jogo nem de chave.
    /// </summary>
    [RelayCommand]
    private async Task OpenSampleAsync()
    {
        ClearMessages();
        string root;
        var parent = sampleParent ?? System.IO.Path.Combine(RecentProjectsFile.DefaultDirectory(), "exemplo");
        try
        {
            root = await Task.Run(() => SampleProject.Create(parent), CancellationToken.None);
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"Não foi possível preparar a cena de exemplo: {WithoutPath(e.Message, parent)}";
            return;
        }

        if (await OpenAsync(root) is { } summary)
        {
            DashboardRequested?.Invoke(this, summary);
        }
    }

    /// <summary>"Escolher a pasta" da área para soltar: abre o seletor e trata a pasta como se tivesse sido solta.</summary>
    [RelayCommand]
    private async Task ChooseGameFolderAsync()
    {
        ClearMessages();
        var path = await picker.PickFolderAsync("Pasta do jogo a traduzir");
        if (path is not null)
        {
            await DropFolderAsync(path);
        }
    }

    /// <summary>
    /// Uma pasta foi solta na tela: abre o Novo projeto com ela; um arquivo vale pela pasta onde está. Se a pasta já é um projeto do VNForge
    /// (tem <c>vnforge.json</c>), abre o projeto em vez do assistente, que o recusaria. Com mais de uma pasta solta só a primeira vale.
    /// </summary>
    public async Task DropFolderAsync(string path, int dropped = 1)
    {
        ClearMessages();
        var folder = File.Exists(path) ? System.IO.Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ErrorMessage = "Essa pasta não existe mais. Solte outra ou use Escolher a pasta.";
            return;
        }

        if (dropped > 1)
        {
            NoticeMessage = "Você soltou mais de uma pasta. Só a primeira foi usada; solte as outras uma de cada vez.";
        }

        if (File.Exists(System.IO.Path.Combine(folder, ProjectFolder.ConfigFileName)))
        {
            await OpenAsync(folder);
            return;
        }

        GameFolderChosen?.Invoke(this, folder);
    }

    /// <summary>O botão do "Próximo passo": abre o projeto selecionado e vai direto ao passo que o cartão mostra.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private async Task OpenNextStepAsync()
    {
        if (Selected is not { } item || Detail.NextStepTarget is not { } target)
        {
            return;
        }

        ClearMessages();
        if (await OpenAsync(item.Path) is not null)
        {
            NextStepRequested?.Invoke(this, target);
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        ClearMessages();
        NewProjectRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private void OpenSelectedInEditor()
    {
        if (Selected?.Summary is { } summary)
        {
            EditorRequested?.Invoke(this, summary);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task RemoveSelectedAsync() => RemoveRecentAsync(Selected);

    /// <summary>Tira um projeto da lista de recentes (o menu da linha); a pasta dele não é tocada.</summary>
    [RelayCommand]
    private async Task RemoveRecentAsync(RecentProjectItem? item)
    {
        if (item is null)
        {
            return;
        }

        recents.Remove(item.Path);
        if (ReferenceEquals(Selected, item))
        {
            Selected = null;
        }

        await LoadAsync();
    }

    /// <summary>Quanto tempo a seleção espera antes de ler o projeto quando a pessoa percorre a lista com as setas.</summary>
    private static readonly TimeSpan DetailDebounce = TimeSpan.FromMilliseconds(150);

    private CancellationTokenSource? detailDebounce;
    private long lastSelectionAt;
    private bool forceDetailReload;

    partial void OnSelectedChanged(RecentProjectItem? value)
    {
        var project = value?.IsSupported == true ? value.Summary : null;
        var force = forceDetailReload;
        if (!force && project is not null && Equals(Detail.Project, project))
        {
            return;
        }

        CancelDetailDebounce();
        if (project is null || !SamePath(project.Root, continueRoot))
        {
            ShowContinueLine(null, string.Empty, null);
        }

        var rapid = lastSelectionAt != 0 && Stopwatch.GetElapsedTime(lastSelectionAt) < TimeSpan.FromMilliseconds(500);
        lastSelectionAt = Stopwatch.GetTimestamp();
        Detail.Project = project;
        if (project is null)
        {
            DetailLoading = Detail.ReloadAsync();
            return;
        }

        var source = detailDebounce = new CancellationTokenSource();
        DetailLoading = ReloadDetailAsync(rapid && !force ? DetailDebounce : TimeSpan.Zero, source.Token);
    }

    /// <summary>Cancela e descarta a espera anterior; quem a usa já guardou o token, que continua válido depois do descarte.</summary>
    private void CancelDetailDebounce()
    {
        var previous = detailDebounce;
        detailDebounce = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    public void Dispose()
    {
        CancelDetailDebounce();
    }

    private async Task ReloadDetailAsync(TimeSpan wait, CancellationToken token)
    {
        if (wait > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(wait, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!token.IsCancellationRequested)
        {
            await Detail.ReloadAsync();
            OnPropertyChanged(nameof(DetailError));
            await ReloadContinueLineAsync(token);
        }
    }

    /// <summary>
    /// Lê, na cena do próximo passo, a fala da caixa do cartão. A fala é enfeite: qualquer falha na leitura só esconde a caixa e nunca sobe para
    /// <see cref="DetailLoading"/>, que ninguém observa no app. Enquanto a leitura corre, a fala do mesmo projeto fica na tela.
    /// </summary>
    private async Task ReloadContinueLineAsync(CancellationToken token)
    {
        var root = Selected?.IsSupported == true ? Selected.Path : null;
        var key = Detail.NextStepTarget?.ScriptKey;
        if (root is null || key is null)
        {
            ShowContinueLine(null, string.Empty, null);
            return;
        }

        (TextboxLayout? Layout, string Caption) found;
        try
        {
            found = await Task.Run(() => ReadContinueLine(root, key), CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Debug.WriteLine($"Início: não foi possível ler a fala do cartão: {e}");
            found = (null, string.Empty);
        }

        if (!token.IsCancellationRequested && SamePath(root, Selected?.Path))
        {
            ShowContinueLine(found.Layout, found.Caption, root);
        }
    }

    private string? continueRoot;

    private void ShowContinueLine(TextboxLayout? layout, string caption, string? root)
    {
        continueRoot = layout is null ? null : root;
        ContinueLayout = layout;
        ContinueLineCaption = caption;
    }

    /// <summary>Quantas falas a leitura tenta montar antes de desistir: uma fala que o pipeline recusa passa a vez para a próxima da cena.</summary>
    private const int MaxContinueTries = 20;

    private (TextboxLayout? Layout, string Caption) ReadContinueLine(string root, string key)
    {
        try
        {
            var folder = ProjectFolder.Open(root);
            var units = folder.LoadText(key);
            var naming = SpeakerNaming.Read(folder);
            var speech = units.Where(u => u.Kind is TextUnitKind.Dialogue or TextUnitKind.Narration && !string.IsNullOrWhiteSpace(u.Translation)).ToList();
            var toReview = speech.Where(u => u.Status is TranslationStatus.Machine or TranslationStatus.Translated);
            var reviewed = speech.Where(u => u.Status is TranslationStatus.Reviewed or TranslationStatus.Approved).Reverse();
            var profile = profiles.Get(root) ?? FormattingProfile.Default;
            foreach (var (unit, isToReview) in toReview.Select(u => (u, true)).Concat(reviewed.Select(u => (u, false))).Take(MaxContinueTries))
            {
                if (new TextboxPreviewViewModel(unit, profile, unit.Translation!, naming: naming).Layout is { } layout)
                {
                    return (layout, isToReview ? "Próxima fala a revisar" : "Última fala revisada");
                }
            }

            return (null, string.Empty);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return (null, string.Empty);
        }
    }

    /// <summary>A mensagem de um erro de leitura sem o caminho da pasta: caminho só aparece em "Detalhes técnicos".</summary>
    private static string WithoutPath(string message, string path)
    {
        var root = path.TrimEnd('\\', '/');
        var folder = "a pasta escolhida";
        return message
            .Replace($" em '{root}'", " na " + folder[2..], StringComparison.OrdinalIgnoreCase)
            .Replace($"'{root}'", folder, StringComparison.OrdinalIgnoreCase)
            .Replace(root + System.IO.Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(root + System.IO.Path.AltDirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(root, folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>O erro de leitura do <see cref="Detail"/> do projeto selecionado, sem o caminho.</summary>
    public string? DetailError => Detail.ErrorMessage is { } error && Selected is { } item ? WithoutPath(error, item.Path) : Detail.ErrorMessage;

    private bool CanOpenSelected() => Selected?.IsSupported == true;

    private bool HasSelection() => Selected is not null;

    private void ClearMessages()
    {
        ErrorMessage = null;
        NoticeMessage = null;
    }

    private static RecentProjectItem Describe(RecentProject recent, DateTimeOffset now)
    {
        if (!Directory.Exists(recent.Path))
        {
            return RecentProjectItem.FromProblem(recent.Path, "A pasta do projeto não foi encontrada. Remova-o da lista ou restaure a pasta.", recent.OpenedAt, now);
        }

        try
        {
            return RecentProjectItem.FromSummary(ProjectSummaryReader.Read(recent.Path), recent.OpenedAt, now);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return RecentProjectItem.FromProblem(recent.Path, WithoutPath(e.Message, recent.Path), recent.OpenedAt, now);
        }
    }

    private static bool IsExpected(Exception e) => e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException;

    private static string? FolderOf(string path) =>
        System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/')) is { Length: > 0 } folder ? folder : null;

    private static bool SamePath(string a, string? b) =>
        b is not null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
