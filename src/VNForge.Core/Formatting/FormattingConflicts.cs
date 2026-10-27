using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Formatting;

/// <summary>
/// Um ajuste de formatação que produz caractere que a estratégia de codificação do projeto não grava (Reticências = "Caractere …" na <c>cp1252</c>).
/// Os valores de <see cref="Current"/> e <see cref="Safe"/> são os do <c>formatting.json</c>, em minúsculas (<c>character</c>, <c>dots</c>) ou o próprio
/// caractere escolhido para o travessão.
/// </summary>
/// <param name="Field">Campo do perfil, no caminho do JSON sem o <c>$.</c> (<c>typography.ellipsis</c>).</param>
/// <param name="Setting">Nome do ajuste em português ("Reticências").</param>
/// <param name="Current">Valor atual do campo.</param>
/// <param name="CurrentLabel">Valor atual como a interface o chama ("Caractere …").</param>
/// <param name="Characters">Os caracteres que o ajuste produz e a estratégia não grava; para a largura cheia, os de largura cheia que a estratégia recusa.</param>
/// <param name="Safe">Valor do campo que a estratégia grava; <see langword="null"/> quando o campo fica sem valor (travessão desligado).</param>
/// <param name="SafeLabel">O valor seguro como a interface o chama ("Três pontos").</param>
public sealed record FormattingConflict(string Field, string Setting, string Current, string CurrentLabel, string Characters, string? Safe, string SafeLabel)
{
    /// <summary>O ajuste e o valor atual numa frase curta: "Reticências: Caractere …".</summary>
    public string Label => $"{Setting}: {CurrentLabel}";
}

/// <summary>
/// Acha os ajustes de formatação que o encoder do projeto não consegue gravar e devolve o perfil com o valor seguro de cada um. Só os ajustes de
/// tipografia e de largura entram: o texto das substituições do usuário é livre, e o caractere que elas produzem continua sendo acusado por linha
/// pela regra de caractere não codificável.
/// </summary>
public static class FormattingConflicts
{
    public const string QuotesField = "typography.quotes";
    public const string EllipsisField = "typography.ellipsis";
    public const string DashField = "typography.dash";
    public const string DialogueDashField = "typography.dialogueDash";
    public const string WidthField = "width.mode";

    private const int FullwidthStart = 0xFF01;
    private const int FullwidthEnd = 0xFF5E;
    private const int IdeographicSpace = 0x3000;

    /// <summary>Os ajustes de <paramref name="profile"/> que produzem caractere que <paramref name="encoder"/> não grava, na ordem dos campos do perfil.</summary>
    public static IReadOnlyList<FormattingConflict> Find(FormattingProfile profile, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(encoder);

        var conflicts = new List<FormattingConflict>();
        var typography = profile.Typography;

        if (typography.Quotes == QuoteStyle.Corner && Unwritable(encoder, "「", "」") is { Length: > 0 } quotes)
        {
            conflicts.Add(new FormattingConflict(QuotesField, "Aspas", "corner", "Aspas 「 」", quotes, "ascii", "Aspas retas"));
        }

        if (typography.Ellipsis == EllipsisStyle.Character && Unwritable(encoder, "…") is { Length: > 0 } ellipsis)
        {
            conflicts.Add(new FormattingConflict(EllipsisField, "Reticências", "character", "Caractere …", ellipsis, "dots", "Três pontos"));
        }

        if (typography.Dash is { } dash && Unwritable(encoder, dash.ToString()) is { Length: > 0 } dashes)
        {
            conflicts.Add(new FormattingConflict(DashField, "Travessão", dash.ToString(), $"Caractere {dash}", dashes, null, "Nenhum"));
        }

        if (typography.DialogueDash is { } dialogueDash && Unwritable(encoder, dialogueDash.ToString()) is { Length: > 0 } dialogueDashes)
        {
            var hyphen = encoder.FindUnencodable("-").Count == 0;
            conflicts.Add(new FormattingConflict(
                DialogueDashField, "Travessão de fala", dialogueDash.ToString(), $"Caractere {dialogueDash}", dialogueDashes, hyphen ? "-" : null, hyphen ? "Hífen (-)" : "Nenhum"));
        }

        if (profile.Width.Mode == WidthMode.Full && FullwidthUnwritable(encoder, profile.Width.Spaces) is { Length: > 0 } wide)
        {
            conflicts.Add(new FormattingConflict(WidthField, "Largura", "full", "Cheia", wide, "none", "Manter"));
        }

        return conflicts;
    }

    /// <summary>O <paramref name="profile"/> com o valor seguro de cada um dos <paramref name="conflicts"/>; o mesmo perfil quando não há conflito.</summary>
    public static FormattingProfile Resolve(FormattingProfile profile, IEnumerable<FormattingConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(conflicts);

        var resolved = profile;
        foreach (var conflict in conflicts)
        {
            resolved = conflict.Field switch
            {
                QuotesField => resolved with { Typography = resolved.Typography with { Quotes = QuoteStyle.Ascii } },
                EllipsisField => resolved with { Typography = resolved.Typography with { Ellipsis = EllipsisStyle.Dots } },
                DashField => resolved with { Typography = resolved.Typography with { Dash = null } },
                DialogueDashField => resolved with { Typography = resolved.Typography with { DialogueDash = conflict.Safe is { Length: 1 } safe ? safe[0] : null } },
                WidthField => resolved with { Width = resolved.Width with { Mode = WidthMode.None } },
                _ => resolved,
            };
        }

        return resolved;
    }

    /// <summary>O <paramref name="profile"/> com o valor seguro de todos os ajustes em conflito com <paramref name="encoder"/>.</summary>
    public static FormattingProfile Resolve(FormattingProfile profile, ITextEncoder encoder) => Resolve(profile, Find(profile, encoder));

    private static string Unwritable(ITextEncoder encoder, params string[] characters) =>
        string.Concat(characters.Where(c => encoder.FindUnencodable(c).Count > 0));

    private static string FullwidthUnwritable(ITextEncoder encoder, bool spaces)
    {
        var result = new StringBuilder();
        for (var code = FullwidthStart; code <= FullwidthEnd; code++)
        {
            // Barra e chaves nunca mudam de largura (WidthStage), então não são produzidas.
            if (code is 0xFF3C or 0xFF5B or 0xFF5D)
            {
                continue;
            }

            var character = ((char)code).ToString();
            if (encoder.FindUnencodable(character).Count > 0)
            {
                result.Append(character);
            }
        }

        if (spaces && encoder.FindUnencodable(((char)IdeographicSpace).ToString()).Count > 0)
        {
            result.Append((char)IdeographicSpace);
        }

        return result.ToString();
    }
}
