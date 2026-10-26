using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um dos quatro passos numerados da primeira vez no Início.</summary>
public sealed record FirstStep(int Number, string Title, string Description);

/// <summary>Tela Início: abrir um projeto, ver os recentes e o progresso do projeto selecionado.</summary>
public sealed partial class StartViewModel(RecentProjectsFile recents, IFolderPicker picker, TimeProvider clock) : ObservableObject
{
    private readonly SemaphoreSlim loadGate = new(1, 1);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedInEditorCommand))]
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

    /// <summary>A leitura do <see cref="Detail"/> em curso; os testes esperam por ela.</summary>
    public Task DetailLoading { get; private set; } = Task.CompletedTask;

    /// <summary>A chave do Claude está guardada; a lista "O que você precisa" da primeira vez marca esse item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyNeedText))]
    public partial bool ApiKeyConfigured { get; set; }

    public bool HasRecents => Recents.Count > 0;

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
        await loadGate.WaitAsync();
        try
        {
            var keep = selectPath ?? Selected?.Path;
            var now = clock.GetUtcNow();
            var list = recents.Read();
            var items = await Task.WhenAll(list.Projects.Select(recent => Task.Run(() => Describe(recent, now))));
            RecentsProblem = list.Problem is { } problem
                ? $"{problem} A lista aparece vazia; abrir um projeto a recria."
                : null;

            Recents.Clear();
            foreach (var item in items)
            {
                Recents.Add(item);
            }

            Selected = Recents.FirstOrDefault(i => SamePath(i.Path, keep)) ?? Recents.FirstOrDefault();
            OnPropertyChanged(nameof(HasRecents));
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
            summary = await Task.Run(() => ProjectSummaryReader.Read(path));
        }
        catch (Exception e) when (IsExpected(e))
        {
            ErrorMessage = $"{e.Message} O projeto não foi aberto.";
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
            ErrorMessage = $"{e.Message} O projeto não pôde ser carregado.";
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

    /// <summary>"Escolher a pasta" da área para soltar: abre o seletor e trata a pasta como se tivesse sido solta.</summary>
    [RelayCommand]
    private async Task ChooseGameFolderAsync()
    {
        ClearMessages();
        var path = await picker.PickFolderAsync("Pasta do jogo a traduzir");
        if (path is not null)
        {
            DropFolder(path);
        }
    }

    /// <summary>Uma pasta foi solta na tela: abre o Novo projeto com ela. Um arquivo vale pela pasta onde está.</summary>
    public void DropFolder(string path)
    {
        ClearMessages();
        var folder = File.Exists(path) ? System.IO.Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ErrorMessage = "Essa pasta não existe mais. Solte outra ou use Escolher a pasta.";
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

    partial void OnSelectedChanged(RecentProjectItem? value)
    {
        Detail.Project = value?.IsSupported == true ? value.Summary : null;
        DetailLoading = Detail.ReloadAsync();
    }

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
            return RecentProjectItem.FromProblem(recent.Path, $"O projeto não foi encontrado em '{recent.Path}'. Remova-o da lista ou restaure a pasta.", recent.OpenedAt, now);
        }

        try
        {
            return RecentProjectItem.FromSummary(ProjectSummaryReader.Read(recent.Path), recent.OpenedAt, now);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return RecentProjectItem.FromProblem(recent.Path, e.Message, recent.OpenedAt, now);
        }
    }

    private static bool IsExpected(Exception e) => e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException;

    private static string? FolderOf(string path) =>
        System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/')) is { Length: > 0 } folder ? folder : null;

    private static bool SamePath(string a, string? b) =>
        b is not null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
