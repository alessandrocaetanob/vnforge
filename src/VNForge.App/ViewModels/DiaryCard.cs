using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// Os textos do Diário de tradução do Início, escritos a partir do <see cref="ReviewDiary"/>: o número de dias seguidos, o que foi aprovado ontem e hoje e a
/// frase do mapa de calor para leitores de tela.
/// </summary>
public sealed class DiaryCard(ReviewDiary diary, bool isLoading = false)
{
    public static DiaryCard Empty { get; } = new(ReviewDiary.Empty);

    /// <summary>O diário que ainda não foi lido: o painel não mostra números nem o convite, para não piscar "nenhuma aprovação" antes da leitura.</summary>
    public static DiaryCard Pending { get; } = new(ReviewDiary.Empty, isLoading: true);

    /// <summary>A leitura do registro ainda não chegou.</summary>
    public bool IsLoading { get; } = isLoading;

    /// <summary>"Últimas 12 semanas · um quadrado por dia", o que o mapa mostra.</summary>
    public static string RangeText => $"Últimas {ReviewDiary.Weeks} semanas · um quadrado por dia";

    /// <summary>O diário que o mapa de calor desenha.</summary>
    public ReviewDiary Source { get; } = diary;

    public bool HasActivity => !IsLoading && Source.HasActivity;

    public bool IsEmpty => !IsLoading && !Source.HasActivity;

    /// <summary>O número grande: "3".</summary>
    public string StreakValue => IsLoading ? string.Empty : ProjectLabels.Number(Source.Streak);

    public string StreakCaption => IsLoading ? string.Empty : StreakCaptionFor(Source.Streak);

    /// <summary>"Ontem você aprovou 120 linhas." ou "Ontem você não aprovou linhas.".</summary>
    public string YesterdayText => IsLoading ? string.Empty : YesterdayFor(Source.Yesterday);

    /// <summary>"Hoje: 30 linhas." ou "Hoje ainda não há linhas aprovadas.".</summary>
    public string TodayText => IsLoading ? string.Empty : TodayFor(Source.Today);

    /// <summary>"1.240 linhas aprovadas nas últimas 12 semanas."</summary>
    public string TotalText => $"{Lines(Source.Total)} {(Source.Total == 1 ? "aprovada" : "aprovadas")} nas últimas {ReviewDiary.Weeks} semanas.";

    public static string EmptyText => "Aprove algumas linhas e o diário começa a se preencher.";

    /// <summary>A frase que lê o mapa inteiro para quem não o enxerga.</summary>
    public string MapDescription
    {
        get
        {
            if (IsLoading)
            {
                return "Mapa das aprovações por dia, carregando.";
            }

            return Source.HasActivity
                ? $"Mapa das aprovações por dia nas últimas {ReviewDiary.Weeks} semanas. {TotalText} {Source.Streak} {StreakCaption}."
                : $"Mapa das aprovações por dia nas últimas {ReviewDiary.Weeks} semanas, ainda sem aprovações.";
        }
    }

    /// <summary>
    /// O resumo da sessão que aparece ao fechar o app, ou nulo quando nada foi aprovado nela:
    /// "Nesta sessão: 42 linhas aprovadas em cerca de 35 min. Já são 3 dias seguidos.".
    /// </summary>
    public static string? SessionText(SessionSummary session, ReviewDiary diary)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(diary);
        if (session.Lines <= 0)
        {
            return null;
        }

        var text = $"Nesta sessão: {Lines(session.Lines)} {(session.Lines == 1 ? "aprovada" : "aprovadas")} em {ProjectLabels.Duration(session.Worked)}.";
        return diary.Streak >= 2 ? $"{text} Já são {diary.Streak} dias seguidos." : text;
    }

    private static string StreakCaptionFor(int streak) => streak == 1 ? "dia seguido" : "dias seguidos";

    private static string YesterdayFor(int lines) =>
        lines == 0 ? "Ontem você não aprovou linhas." : $"Ontem você aprovou {Lines(lines)}.";

    private static string TodayFor(int lines) =>
        lines == 0 ? "Hoje ainda não há linhas aprovadas." : $"Hoje: {Lines(lines)}.";

    private static string Lines(int count) => count == 1 ? "1 linha" : $"{ProjectLabels.Number(count)} linhas";
}
