using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Memory;

namespace VNForge.App.ViewModels;

/// <summary>Uma sugestão da memória de tradução no inspetor: o percentual em selo, a tradução e o projeto de origem com a data da aprovação.</summary>
public sealed record MemorySuggestionViewModel(int Percent, string Target, string Origin)
{
    public bool IsExact => Percent == 100;

    public string PercentText => $"{Percent}%";

    public string UseName => $"Usar a sugestão de {PercentText}: {Target}";
}

public sealed partial class LineDetailViewModel
{
    /// <summary>A memória de tradução do projeto; nula quando o Editor não a montou (projeto de exemplo, testes).</summary>
    public IMemorySuggestions? Memory { get; init; }

    /// <summary>As traduções aprovadas em outros projetos para o original desta linha, com a exata primeiro. Vazia enquanto a busca roda e quando não há nada parecido.</summary>
    public ObservableCollection<MemorySuggestionViewModel> MemorySuggestions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneCollapsed), nameof(TranslationMinHeight), nameof(ShowMemoryCard), nameof(IsCompactInspector), nameof(UsesSceneToggle), nameof(ShowExplainBlock))]
    public partial bool HasMemory { get; private set; }

    /// <summary>
    /// O inspetor compacto: o cartão da memória (#537) ou o da passada de editor (#532) está aberto. A cena fica recolhida, o campo da tradução baixo e o número de caracteres sobe
    /// para o cabeçalho, para o cartão e a caixa do jogo caberem na tela (quadros do #537 e do #532).
    /// </summary>
    public bool IsCompactInspector => HasMemory || HasEditorSuggestion;

    /// <summary>Altura mínima do campo da tradução: 44 px normalmente (60 com a margem e o contorno, perto dos 62 da caixa de duas linhas do quadro do Editor) e 28 px (44 com a margem) com o cartão da memória aberto, para a caixa do jogo e o "Cabe" ficarem na tela (quadro do #537).</summary>
    public double TranslationMinHeight => IsCompactInspector ? 28 : 44;

    /// <summary>O aviso do cartão quando o arquivo da memória não pôde ser lido: diz o caminho e que nada foi alterado. Nulo quando a busca correu bem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryProblem), nameof(ShowMemoryCard))]
    public partial string? MemoryProblem { get; private set; }

    public bool HasMemoryProblem => !string.IsNullOrEmpty(MemoryProblem);

    /// <summary>O cartão aparece com sugestões ou com o aviso de que a memória não pôde ser lida.</summary>
    public bool ShowMemoryCard => HasMemory || HasMemoryProblem;

    /// <summary>"3 em 2 projetos": quantas sugestões o cartão mostra e de quantos projetos elas vêm.</summary>
    [ObservableProperty]
    public partial string MemorySummary { get; private set; } = string.Empty;

    /// <summary>A busca da memória em andamento; os testes aguardam por ela. A busca roda fora da thread da interface: com muitos projetos ela passa de um piscar de olhos.</summary>
    public Task MemoryLoading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Com o cartão da memória aberto o "Na cena" fica recolhido para o cartão ter espaço (quadro do #537); a pessoa abre pelo cabeçalho. Sem o cartão a cena aparece como sempre.
    /// </summary>
    public bool IsSceneCollapsed => UsesSceneToggle && !IsSceneOpen;

    /// <summary>O "Na cena" é um cabeçalho que abre e fecha, e não uma lista sempre aberta: com o cartão da memória ou da sugestão no inspetor e com o "Resumo da cena" da bíblia (quadro do #530: "Na cena, 2 falas").</summary>
    public bool UsesSceneToggle => IsCompactInspector || HasSceneSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneCollapsed))]
    public partial bool IsSceneOpen { get; set; }

    /// <summary>"2 falas antes" ao lado do "Na cena" recolhido: a cena é o que vem antes da linha aberta.</summary>
    public string SceneCountText => Scene.Count == 1 ? "1 fala antes" : $"{Scene.Count} falas antes";

    [RelayCommand]
    private void ToggleScene() => IsSceneOpen = !IsSceneOpen;

    /// <summary>Busca a memória para o original desta linha. Chamado pelo Editor depois de montar o painel; sem <see cref="Memory"/> não faz nada.</summary>
    public void ShowMemory()
    {
        if (Memory is not { } memory)
        {
            return;
        }

        var source = unit.Source;
        MemoryLoading = LoadMemoryAsync(memory, source);
    }

    private async Task LoadMemoryAsync(IMemorySuggestions memory, string source)
    {
        var found = await Task.Run(() => memory.Find(source), CancellationToken.None).ConfigureAwait(false);
        postToUi(() =>
        {
            if (disposed)
            {
                return;
            }

            ApplyMemory(found);
            MemoryProblem = memory.Problem;
        });
    }

    private void ApplyMemory(IReadOnlyList<MemoryMatch> found)
    {
        var current = Translation.Trim();
        var shown = found.Where(m => m.Entry.Target.Trim() != current).ToList();
        MemorySuggestions.Clear();
        var now = clock.GetLocalNow();
        foreach (var match in shown)
        {
            MemorySuggestions.Add(new MemorySuggestionViewModel(match.Percent, match.Entry.Target, OriginText(match.Entry, now)));
        }

        var projects = shown.Select(m => m.Entry.ProjectKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        MemorySummary = $"{shown.Count} em {projects} {(projects == 1 ? "projeto" : "projetos")}";
        HasMemory = shown.Count > 0;
    }

    private static string OriginText(MemoryEntry entry, DateTimeOffset now)
    {
        var when = entry.ApprovedAt.ToOffset(now.Offset);
        var date = when.Year == now.Year ? when.ToString("dd/MM", CultureInfo.InvariantCulture) : when.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return $"{entry.ProjectName} · aprovada em {date}";
    }

    /// <summary>"Usar": coloca a tradução da sugestão no campo. Nada grava sozinho além da gravação de sempre depois de digitar, e aprovar continua sendo da pessoa.</summary>
    [RelayCommand]
    private void UseMemory(MemorySuggestionViewModel? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        Translation = suggestion.Target;
    }
}
