using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// A lista de cenas por capítulo: um grupo por capítulo de <see cref="ScriptGrouping.Build"/>, na ordem da história e com "Menus e nomes" no fim. Criar um
/// capítulo, mover cenas e voltar ao agrupamento automático gravam o <c>group</c> no <c>scripts.json</c> pelo mesmo <see cref="ScriptGroupEditor"/> da CLI.
/// </summary>
public sealed partial class EditorViewModel
{
    private readonly HashSet<string> closedChapters = new(StringComparer.OrdinalIgnoreCase);
    private ScriptNames scriptNames = ScriptNames.Empty;
    private IReadOnlyList<string> pendingChapterKeys = [];

    /// <summary>Lê o <c>scripts.json</c> do projeto; os testes trocam por um que devolve nomes fixos. O padrão lê o disco e, sem o arquivo, devolve nomes vazios.</summary>
    public Func<string, ScriptNames>? NamesLoader { get; init; }

    /// <summary>
    /// Põe as cenas (as chaves) no capítulo dado (pasta do projeto, chaves, nome) ou, com nome nulo, devolve ao agrupamento automático. O padrão grava o
    /// <c>scripts.json</c> pelo <see cref="ScriptGroupEditor"/>; os testes trocam por um que falha.
    /// </summary>
    public Func<string, IReadOnlyList<string>, string?, ScriptGroupChange>? Grouper { get; init; }

    /// <summary>Disparado depois de um agrupamento gravado, com os capítulos como o Painel passa a mostrá-los; a janela principal atualiza o Painel com ele.</summary>
    public event EventHandler<ScriptGroupChange>? ChaptersChanged;

    /// <summary>As cenas na ordem da lista: capítulo por capítulo. É o que a lista da tela mostra; <see cref="Scripts"/> segue a ordem do projeto.</summary>
    public ObservableCollection<ScriptItemViewModel> SceneList { get; } = [];

    /// <summary>Os capítulos da lista, na ordem da história; "Menus e nomes" por último.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ChapterGroupViewModel> Chapters { get; private set; } = [];

    /// <summary>Esconde da lista as cenas sem linhas (padrão); desligado, elas aparecem marcadas como prontas. A cena vazia aberta continua aberta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyScenesText))]
    public partial bool HideEmptyScenes { get; set; } = true;

    /// <summary>Quantas cenas vazias a lista esconde agora.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenEmptyScenes), nameof(HiddenEmptyScenesText))]
    public partial int HiddenEmptyScenes { get; private set; }

    /// <summary>O projeto tem cenas sem linhas: sem elas a lista não mostra o contador nem o botão.</summary>
    [ObservableProperty]
    public partial bool HasEmptyScenes { get; private set; }

    public bool HasHiddenEmptyScenes => HiddenEmptyScenes > 0;

    /// <summary>"2 cenas vazias escondidas"; vazio quando nada está escondido.</summary>
    public string HiddenEmptyScenesText =>
        HiddenEmptyScenes == 0 ? string.Empty : HiddenEmptyScenes == 1 ? "1 cena vazia escondida" : $"{ProjectLabels.Number(HiddenEmptyScenes)} cenas vazias escondidas";

    /// <summary>O botão ao lado do contador: "Mostrar" com as cenas escondidas, "Esconder" com elas à mostra.</summary>
    public string ShowEmptyScenesText => HideEmptyScenes ? "Mostrar" : "Esconder cenas vazias";

    [RelayCommand]
    private void ToggleEmptyScenes() => HideEmptyScenes = !HideEmptyScenes;

    partial void OnHideEmptyScenesChanged(bool value) => RefreshChapterList();

    /// <summary>O campo "Nome do capítulo" da lista está aberto (menu "Novo capítulo...").</summary>
    [ObservableProperty]
    public partial bool IsNamingChapter { get; private set; }

    /// <summary>O que está escrito no campo de nome do capítulo novo.</summary>
    [ObservableProperty]
    public partial string NewChapterName { get; set; } = string.Empty;

    /// <summary>A frase sobre o campo do nome: quantas cenas o capítulo novo recebe.</summary>
    [ObservableProperty]
    public partial string NewChapterHint { get; private set; } = string.Empty;

    /// <summary>Termina quando a última releitura dos nomes pedida por <see cref="RefreshChaptersAsync"/> acaba; os testes a aguardam.</summary>
    public Task ChapterRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>Por que o agrupamento não foi feito; nulo quando nada deu errado.</summary>
    [ObservableProperty]
    public partial string? ChapterMessage { get; private set; }

    /// <summary>Abre o campo do nome do capítulo novo; as cenas marcadas na lista (ou a aberta, sem marcação) vão para ele ao confirmar.</summary>
    [RelayCommand]
    private void BeginNewChapter()
    {
        var keys = ChapterTargets();
        if (keys.Count == 0)
        {
            return;
        }

        // As cenas valem como estavam ao abrir o campo: marcar outra enquanto a pessoa escreve o nome não muda o pedido.
        pendingChapterKeys = keys;
        ChapterMessage = null;
        NewChapterName = string.Empty;
        NewChapterHint = keys.Count == 1 ? "Nome do capítulo novo, com 1 cena." : $"Nome do capítulo novo, com {keys.Count} cenas.";
        IsNamingChapter = true;
    }

    /// <summary>"Novo capítulo..." vindo do Painel: as marcas antigas saem, a cena aberta fica como a única marcada e o campo do nome abre.</summary>
    public void BeginNewChapterOnOpenScene()
    {
        ChosenScripts.Clear();
        if (SelectedScript is { } open)
        {
            ChosenScripts.Add(open);
        }

        BeginNewChapterCommand.Execute(null);
    }

    [RelayCommand]
    private void CancelNewChapter()
    {
        IsNamingChapter = false;
        NewChapterName = string.Empty;
        NewChapterHint = string.Empty;
        pendingChapterKeys = [];
        ChapterMessage = null;
    }

    [RelayCommand]
    private async Task CommitNewChapterAsync()
    {
        var name = NewChapterName.Trim();
        if (name.Length == 0)
        {
            ChapterMessage = "Escreva um nome para o capítulo.";
            return;
        }

        if (await GroupAsync(name, pendingChapterKeys))
        {
            IsNamingChapter = false;
            NewChapterName = string.Empty;
            NewChapterHint = string.Empty;
            pendingChapterKeys = [];
        }
    }

    /// <summary>"Voltar ao agrupamento automático": as cenas marcadas voltam ao capítulo que a pasta, o prefixo ou a faixa propõe.</summary>
    [RelayCommand]
    private async Task ResetChaptersAsync() => await GroupAsync(null);

    /// <summary>"Mover para o capítulo": as cenas marcadas passam para o capítulo dado.</summary>
    [RelayCommand]
    private async Task MoveToChapterAsync(ChapterGroupViewModel? chapter)
    {
        if (chapter is not null)
        {
            await GroupAsync(chapter.Name);
        }
    }

    /// <summary>As cenas que um agrupamento move: as marcadas que a pessoa vê na lista (passam pelo filtro e não estão num capítulo fechado) ou, sem marcação à vista, a aberta.</summary>
    private List<string> ChapterTargets()
    {
        var marked = ChosenScripts.ToHashSet();
        var keys = Scripts.Where(s => s.IsListed && s.Chapter is not { IsExpanded: false } && marked.Contains(s)).Select(s => s.Key).ToList();
        if (keys.Count == 0 && SelectedScript is { } open)
        {
            keys.Add(open.Key);
        }

        return keys;
    }

    private async Task<bool> GroupAsync(string? group, IReadOnlyList<string>? chosen = null)
    {
        var keys = chosen is { Count: > 0 } ? chosen : ChapterTargets();
        if (Project?.Root is not { } root || keys.Count == 0)
        {
            return false;
        }

        var version = loadVersion;
        ChapterMessage = null;
        ScriptGroupChange change;
        NamesRead read;
        try
        {
            (change, read) = await Task.Run(() => (WriteGroup(root, keys, group), ReadNames(root)));
        }
        catch (Exception e) when (e is ScriptGroupingException or VNForgeException or IOException or UnauthorizedAccessException)
        {
            ChapterMessage = e.Message;
            return false;
        }

        // Outro projeto abriu (ou o atual foi relido) enquanto o arquivo era gravado: a lista nova já cuida dos capítulos.
        // Num Reload do mesmo projeto o Painel ainda precisa saber que o scripts.json mudou; noutro projeto a mudança não é dele.
        if (version != loadVersion)
        {
            if (string.Equals(Project?.Root, root, StringComparison.Ordinal))
            {
                ChaptersChanged?.Invoke(this, change);
            }

            return true;
        }

        // Sem conseguir reler o arquivo que acabou de gravar, a lista segue o que se gravou em vez de voltar ao agrupamento automático.
        scriptNames = read.Names ?? ScriptGrouping.Regroup(scriptNames, keys, group);
        ChapterMessage = read.Error;
        RebuildChapters();
        ChaptersChanged?.Invoke(this, change);
        return true;
    }

    private ScriptGroupChange WriteGroup(string root, IReadOnlyList<string> keys, string? group)
    {
        if (Grouper is { } grouper)
        {
            return grouper(root, keys, group);
        }

        var project = ProjectFolder.Open(root);
        return group is null ? ScriptGroupEditor.Ungroup(project, keys) : ScriptGroupEditor.Group(project, keys, group);
    }

    /// <summary>Os nomes lidos do <c>scripts.json</c> ou, quando a leitura falha, o porquê em português e nenhum nome (quem chama mantém os que já tinha).</summary>
    private sealed record NamesRead(ScriptNames? Names, string? Error);

    private NamesRead ReadNames(string root)
    {
        try
        {
            return new NamesRead(NamesLoader is { } custom ? custom(root) : ProjectFolder.Open(root).LoadScriptNames(), null);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return new NamesRead(null, $"Não foi possível ler os capítulos do scripts.json: {e.Message.TrimEnd('.', ' ')}. A lista mantém os capítulos que já tinha.");
        }
    }

    /// <summary>
    /// Relê os nomes de <c>scripts.json</c> e refaz os capítulos, para quando outra tela mudou o arquivo (o renomear do Painel). A lista mantém a cena aberta e as marcadas.
    /// </summary>
    public Task RefreshChaptersAsync() => ChapterRefresh = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (Project?.Root is not { } root)
        {
            return;
        }

        var version = loadVersion;
        var read = await Task.Run(() => ReadNames(root));
        if (version != loadVersion)
        {
            return;
        }

        if (read.Names is null)
        {
            ChapterMessage = read.Error;
            return;
        }

        scriptNames = read.Names;
        RebuildChapters();
    }

    /// <summary>
    /// Refaz os capítulos e a ordem da lista a partir de <see cref="Scripts"/> e dos nomes lidos. A cena aberta e as marcadas continuam como estavam: tirar e pôr
    /// os itens da lista solta a seleção da tela, e isso não pode abrir outra cena nem apagar o que a pessoa marcou.
    /// </summary>
    private void RebuildChapters()
    {
        foreach (var old in Chapters)
        {
            old.PropertyChanged -= OnChapterPropertyChanged;
        }

        var byKey = Scripts.ToDictionary(s => s.Key, StringComparer.Ordinal);
        var files = Scripts.Select(s => new FileStatus(s.Key, s.Progress.Counts)).ToList();
        var chapters = new List<ChapterGroupViewModel>();
        foreach (var group in files.Count == 0 ? [] : ScriptGrouping.Build(scriptNames, files))
        {
            var items = group.ScriptKeys.Select(k => byKey[k]).ToList();
            var chapter = new ChapterGroupViewModel(group, items, chosen => MoveToChapterAsync(chosen)) { IsExpanded = !closedChapters.Contains(group.Name) };
            chapter.PropertyChanged += OnChapterPropertyChanged;
            for (var i = 0; i < items.Count; i++)
            {
                items[i].Chapter = chapter;
                items[i].Position = i + 1;
                items[i].SetLabel(SceneLabel(items[i].Key));
                items[i].IsShown = items[i].Matches(ScriptFilter);
            }

            chapters.Add(chapter);
        }

        Chapters = chapters;
        var open = SelectedScript is { } selected && byKey.ContainsValue(selected) ? selected : null;

        // A cena aberta vai primeiro: a lista toma o primeiro item marcado como o selecionado.
        var marked = ChosenScripts.Where(byKey.ContainsValue).OrderBy(item => !ReferenceEquals(item, open)).ToList();
        var wasApplying = applying;
        applying = true;
        try
        {
            SceneList.Clear();
            foreach (var item in chapters.SelectMany(c => c.Scripts))
            {
                SceneList.Add(item);
            }

            if (open is not null)
            {
                SelectedScript = open;
            }

            ChosenScripts.Clear();
            foreach (var item in marked)
            {
                ChosenScripts.Add(item);
            }

            if (open is not null && !ReferenceEquals(SelectedScript, open))
            {
                SelectedScript = open;
            }
        }
        finally
        {
            applying = wasApplying;
        }

        RefreshChapterList();
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(ScriptTitle));
    }

    private void OnChapterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChapterGroupViewModel chapter || e.PropertyName != nameof(ChapterGroupViewModel.IsExpanded))
        {
            return;
        }

        if (chapter.IsExpanded)
        {
            closedChapters.Remove(chapter.Name);
        }
        else
        {
            closedChapters.Add(chapter.Name);
        }

        RefreshChapterList();
    }

    /// <summary>
    /// Diz a cada cena se aparece na lista e se leva o cabeçalho do capítulo: o cabeçalho vai na primeira cena que passa pelo filtro, e um capítulo
    /// fechado deixa só ela. Um capítulo sem cena que passe pelo filtro some inteiro; as cenas vazias saem da lista enquanto <see cref="HideEmptyScenes"/> estiver ligado.
    /// </summary>
    private void RefreshChapterList()
    {
        foreach (var chapter in Chapters)
        {
            var first = chapter.Scripts.FirstOrDefault(s => s.IsShown && !IsHiddenEmpty(s));
            foreach (var script in chapter.Scripts)
            {
                script.IsGroupStart = ReferenceEquals(script, first);
                script.IsListed = script.IsShown && !IsHiddenEmpty(script) && (chapter.IsExpanded || script.IsGroupStart);
            }
        }

        HasEmptyScenes = Scripts.Any(s => s.IsEmpty);
        HiddenEmptyScenes = HideEmptyScenes ? Scripts.Count(s => s.IsEmpty) : 0;
    }

    private bool IsHiddenEmpty(ScriptItemViewModel script) => HideEmptyScenes && script.IsEmpty;
}
