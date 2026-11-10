using System.Globalization;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da fila de tradução na Biblioteca: o que o Core sabe do jogo (<see cref="TranslationQueueEntry"/>) escrito para as colunas da tabela.</summary>
public sealed class LibraryQueueRowViewModel(LibraryCardViewModel card, TranslationQueueEntry entry, int? position)
{
    /// <summary>Quantas linhas de texto um jogo tem por hora de leitura, para estimar o texto pela duração do VNDB.</summary>
    private const int LinesPerHour = 2000;

    public LibraryCardViewModel Card { get; } = card;

    public TranslationQueueEntry Entry { get; } = entry;

    /// <summary>O lugar na fila; vazio para o jogo que a fila não traduz e quando a lista não está na ordem da fila.</summary>
    public string PositionText { get; } = position?.ToString(CultureInfo.GetCultureInfo("pt-BR")) ?? string.Empty;

    public string PortugueseText => Entry.Portuguese switch
    {
        PortugueseStatus.None => "Não há",
        PortugueseStatus.MachineOnly => "Só automática",
        PortugueseStatus.Partial => "Parcial",
        PortugueseStatus.Complete => "Completa",
        _ when Entry.Game.IsConfirmed => "Não conferido",
        _ => "Sem jogo no VNDB",
    };

    /// <summary>O tom da pílula, na chave que o estilo <c>lib-pill</c> lê: o que ainda falta traduzir é "ok" (verde), o que já existe em parte ou por inteiro é "warn".</summary>
    public string PortugueseTone => Entry.Portuguese switch
    {
        PortugueseStatus.None or PortugueseStatus.MachineOnly => "ok",
        PortugueseStatus.Partial or PortugueseStatus.Complete => "warn",
        _ => string.Empty,
    };

    public string EngineText => Entry.Game.Engine.Translatable ? $"{Entry.Game.Engine.Name} · traduz" : LibraryCards.ChipText(Entry.Game.Engine);

    public string EngineTone => Entry.Game.Engine.Translatable ? "ok" : "warn";

    /// <summary>"≈ 72 mil linhas", pela duração do VNDB; "·" quando o VNDB não tem duração.</summary>
    public string TextEstimate => Entry.ApproximateMinutes is { } minutes ? EstimateLines(minutes) : "·";

    public string Duration => LibraryLength.Short(Entry.Game.Info?.LengthMinutes, Entry.Game.Info?.Length) ?? "·";

    /// <summary>"Traduzir este jogo" para o que ainda não tem projeto e a engine é traduzida; "Abrir projeto" para o que já tem; "Sem suporte ainda" para o resto.</summary>
    public string ActionText => Entry.State switch
    {
        TranslationQueueState.InProgress => "Abrir projeto",
        TranslationQueueState.UnsupportedEngine => "Sem suporte ainda",
        _ => "Traduzir este jogo",
    };

    public bool CanAct => Entry.State != TranslationQueueState.UnsupportedEngine;

    public bool IsTranslate => Entry.State is TranslationQueueState.Queued or TranslationQueueState.AlreadyTranslated;

    public string AutomationName => $"{Card.Title}. Português no VNDB: {PortugueseText}. {EngineText}. {ActionText}";

    private static string EstimateLines(int minutes)
    {
        var thousands = (int)Math.Round(minutes / 60.0 * LinesPerHour / 1000, MidpointRounding.AwayFromZero);
        return thousands < 1 ? "menos de 1 mil linhas" : $"≈ {thousands.ToString(CultureInfo.GetCultureInfo("pt-BR"))} mil linhas";
    }
}
