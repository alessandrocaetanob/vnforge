using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>O campo "Nome do capítulo" da lista está aberto (menu "Novo capítulo...").</summary>
    [ObservableProperty]
    public partial bool IsNamingChapter { get; private set; }

    /// <summary>O que está escrito no campo de nome do capítulo novo.</summary>
    [ObservableProperty]
    public partial string NewChapterName { get; set; } = string.Empty;

    /// <summary>Por que o agrupamento não foi feito; nulo quando nada deu errado.</summary>
    [ObservableProperty]
    public partial string? ChapterMessage { get; private set; }

    /// <summary>Abre o campo do nome do capítulo novo; as cenas marcadas na lista (ou a aberta, sem marcação) vão para ele ao confirmar.</summary>
    [RelayCommand]
    private void BeginNewChapter()
    {
        if (ChapterTargets().Count == 0)
        {
            return;
        }

        ChapterMessage = null;
        NewChapterName = string.Empty;
        IsNamingChapter = true;
    }

    [RelayCommand]
    private void CancelNewChapter()
    {
        IsNamingChapter = false;
        NewChapterName = string.Empty;
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

        if (await GroupAsync(name))
        {
            IsNamingChapter = false;
            NewChapterName = string.Empty;
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

    /// <summary>As cenas que um agrupamento move: as marcadas na lista que passam pelo filtro ou, sem marcação, a aberta.</summary>
    private List<string> ChapterTargets()
    {
        var marked = ChosenScripts.ToHashSet();
        var keys = Scripts.Where(s => s.IsShown && marked.Contains(s)).Select(s => s.Key).ToList();
        if (keys.Count == 0 && SelectedScript is { } open)
        {
            keys.Add(open.Key);
        }

        return keys;
    }

    private async Task<bool> GroupAsync(string? group)
    {
        var keys = ChapterTargets();
        if (Project?.Root is not { } root || keys.Count == 0)
        {
            return false;
        }

        var version = loadVersion;
        ChapterMessage = null;
        ScriptGroupChange change;
        ScriptNames names;
        try
        {
            (change, names) = await Task.Run(() => (WriteGroup(root, keys, group), ReadNames(root)));
        }
        catch (Exception e) when (e is ScriptGroupingException or VNForgeException or IOException or UnauthorizedAccessException)
        {
            ChapterMessage = e.Message;
            return false;
        }

        // Outro projeto abriu (ou o atual foi relido) enquanto o arquivo era gravado: a lista nova já cuida dos capítulos.
        if (version != loadVersion)
        {
            return false;
        }

        scriptNames = names;
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

    private ScriptNames ReadNames(string root)
    {
        try
        {
            return NamesLoader is { } loader ? loader(root) : ProjectFolder.Open(root).LoadScriptNames();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            // Sem scripts.json legível os capítulos saem só da contagem das cenas; o erro aparece onde o arquivo é gravado.
            return ScriptNames.Empty;
        }
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
            foreach (var item in items)
            {
                item.Chapter = chapter;
            }

            chapters.Add(chapter);
        }

        Chapters = chapters;
        var open = SelectedScript is { } selected && byKey.ContainsValue(selected) ? selected : null;
        var marked = ChosenScripts.Where(byKey.ContainsValue).ToList();
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
        }
        finally
        {
            applying = wasApplying;
        }

        RefreshChapterList();
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
    /// fechado deixa só ela. Um capítulo sem cena que passe pelo filtro some inteiro.
    /// </summary>
    private void RefreshChapterList()
    {
        foreach (var chapter in Chapters)
        {
            var first = chapter.Scripts.FirstOrDefault(s => s.IsShown);
            foreach (var script in chapter.Scripts)
            {
                script.IsGroupStart = ReferenceEquals(script, first);
                script.IsListed = script.IsShown && (chapter.IsExpanded || script.IsGroupStart);
            }
        }
    }
}
