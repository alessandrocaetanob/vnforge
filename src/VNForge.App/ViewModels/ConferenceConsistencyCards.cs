using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha de uma variante como link: "Cena 3 · 41", que abre a linha no Editor.</summary>
/// <param name="Label">A cena e a posição da linha no script, como o resto da tela as nomeia.</param>
/// <param name="Request">O pedido que o link manda ao Editor.</param>
public sealed record ConferenceLineLink(string Label, LineRequest Request);

/// <summary>
/// Uma tradução do cartão: o texto com o que difere da referência marcado, quantas linhas a usam e os links dessas linhas. Mostra as três primeiras;
/// "+6" abre as outras. "Usar em todas" grava esta tradução em todas as linhas do original.
/// </summary>
public sealed partial class ConferenceVariantViewModel : ObservableObject
{
    private const int PreviewLines = 3;

    private readonly IReadOnlyList<ConferenceLineLink> links;

    public ConferenceVariantViewModel(ConferenceConsistencyCardViewModel card, ConsistencyVariant variant, Func<ConsistencyLine, string> label)
    {
        Card = card;
        Variant = variant;
        links = [.. variant.Lines.Select(l => new ConferenceLineLink(label(l), new LineRequest(l.ScriptKey, l.UnitId)))];
        CountText = variant.Lines.Count == 1 ? "1 linha" : $"{ProjectLabels.Number(variant.Lines.Count)} linhas";
        UseLabel = "Usar em todas";
        UseAutomationName = $"Usar esta tradução em todas as {ProjectLabels.Number(card.Group.LineCount)} linhas do original";
        VisibleLines = links.Take(PreviewLines).ToList();
    }

    public ConferenceConsistencyCardViewModel Card { get; }

    public ConsistencyVariant Variant { get; }

    public string Translation => Variant.Translation;

    /// <summary>A mais usada: as outras variantes se medem contra ela.</summary>
    public bool IsReference => Variant.IsReference;

    /// <summary>O marcador da frente da variante: cheio na referência, vazio nas outras.</summary>
    public string Marker => IsReference ? "●" : "○";

    /// <summary>O texto da variante em trechos; o que difere da referência vem como <see cref="TextDiffKind.Added"/> ou <see cref="TextDiffKind.Missing"/>.</summary>
    public IReadOnlyList<TextDiffSegment> Segments => Variant.Diff;

    /// <summary>"9 linhas" ou "1 linha".</summary>
    public string CountText { get; }

    public string UseLabel { get; }

    public string UseAutomationName { get; }

    public string AutomationName => $"{CountText}: {Translation}";

    /// <summary>Os links à vista: os três primeiros ou, depois de "+N", todos.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ConferenceLineLink> VisibleLines { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreLabel))]
    public partial bool IsExpanded { get; private set; }

    /// <summary>Há linhas além das três primeiras.</summary>
    public bool HasMore => links.Count > PreviewLines;

    /// <summary>"+6" fechado, "Mostrar menos" aberto.</summary>
    public string MoreLabel => IsExpanded ? "Mostrar menos" : $"+{(links.Count - PreviewLines).ToString(CultureInfo.InvariantCulture)}";

    [RelayCommand]
    private void ToggleLines()
    {
        IsExpanded = !IsExpanded;
        VisibleLines = IsExpanded ? links : links.Take(PreviewLines).ToList();
    }
}

/// <summary>
/// O cartão de um original com mais de uma tradução (<see cref="ConsistencyGroup"/>), no lugar de um aviso por linha: o original, cada variante com as
/// linhas e a diferença marcada, e as ações "Usar em todas" (por variante) e "Manter diferentes de propósito" (do original).
/// </summary>
public sealed class ConferenceConsistencyCardViewModel
{
    public ConferenceConsistencyCardViewModel(ConsistencyGroup group, Func<ConsistencyLine, string> label, IReadOnlyList<ConferenceIssueRowViewModel> rows, IReadOnlyDictionary<string, int?> positions)
    {
        Group = group;
        Positions = positions;
        Rows = rows;
        Variants = [.. group.Variants.Select(v => new ConferenceVariantViewModel(this, v, label))];
        Title = $"Mesmo original, {ProjectLabels.Number(group.Variants.Count)} traduções";
        LinesText = $"{ProjectLabels.Number(group.LineCount)} linhas";
        KeepLabel = "Manter diferentes de propósito";
    }

    public ConsistencyGroup Group { get; }

    /// <summary>A posição de cada linha no script (de 1 em diante), pelo id da unidade; o aviso "Desfazer" a cita.</summary>
    public IReadOnlyDictionary<string, int?> Positions { get; }

    /// <summary>Os avisos que o grupo representa (uma linha por linha fora da referência); contam nos totais, mas a lista mostra só o cartão.</summary>
    public IReadOnlyList<ConferenceIssueRowViewModel> Rows { get; }

    public IReadOnlyList<ConferenceVariantViewModel> Variants { get; }

    public string Source => Group.Source;

    /// <summary>"Mesmo original, 3 traduções".</summary>
    public string Title { get; }

    /// <summary>"12 linhas".</summary>
    public string LinesText { get; }

    public string KeepLabel { get; }

    /// <summary>Quantos avisos o cartão representa nos totais da tela.</summary>
    public int Count => Rows.Count;

    public string AutomationName => $"{Title}, {LinesText}: {Source}";
}
