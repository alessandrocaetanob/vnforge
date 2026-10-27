using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Confere os honoríficos da tradução contra a escolha da aba Estilo: "manter" exige que o honorífico do original apareça,
/// "remover" não aceita nenhum e "adaptar" aceita só senpai (que segue o glossário). Só avisa; quem decide como reescrever é a pessoa,
/// e o aviso não trava o build (<see cref="QaReport.BlockingIssues"/>).
/// </summary>
public sealed partial class HonorificsRule : UnitQaRule
{
    public const string RuleId = "honorifico-fora-do-estilo";

    private static readonly string[] NameSuffixes = ["さん", "さま", "様", "ちゃん", "くん"];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly ConditionalWeakTable<QaContext, Regex> SourceMatchers = new();

    /// <inheritdoc />
    public override string Id => RuleId;

    /// <inheritdoc />
    public override string Description => "Honorífico (-san, -kun, -chan, -sama, senpai) diferente da escolha do estilo do projeto.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>Os honoríficos que a <paramref name="translation"/> mantém (-chan, -san, senpai...), sem repetição e na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> FindInTranslation(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        return [.. TextQaRules.PlainRuns(translation)
            .SelectMany(run => TranslatedHonorific().Matches(run.Text).Select(m => m.Value.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// O original traz um honorífico: 先輩; ちゃん ou くん depois de um nome em kanji ou katakana (menos os termos de parentesco, como
    /// 赤ちゃん, e o advérbio ちゃんと); ou さん, さま, 様, ちゃん e くん depois de um dos <paramref name="names"/> (o falante, os nomes
    /// de <c>characters.json</c> e do glossário). Sem o nome conhecido, さん e 様 não contam: 母さん, お客様, 同様, 模様 e ご馳走さま
    /// são palavras comuns, não tratamento.
    /// </summary>
    public static bool HasHonorificInSource(string source, IEnumerable<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BuildSourceMatcher(names ?? []).IsMatch(source);
    }

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (!unit.HasTranslation)
        {
            return [];
        }

        var found = FindInTranslation(unit.Translation);
        return context.Style.Honorifics switch
        {
            HonorificsPolicy.Keep when found.Count == 0 && SourceHasHonorific(unit, context) =>
                ["O original tem honorífico, mas o estilo do projeto manda manter e a tradução não traz nenhum (-san, -kun, -chan, -sama, senpai)."],
            HonorificsPolicy.Remove => [.. found.Select(h => $"“{h}” ficou na tradução; o estilo do projeto manda remover os honoríficos.")],
            HonorificsPolicy.Adapt => [.. found.Where(h => h != "senpai" && h != "sempai").Select(h => $"“{h}” ficou na tradução; o estilo do projeto manda adaptar os honoríficos.")],
            _ => [],
        };
    }

    private static bool SourceHasHonorific(TextUnit unit, QaContext context)
    {
        var matcher = SourceMatchers.GetValue(context, c => BuildSourceMatcher(KnownNames(c)));
        return matcher.IsMatch(unit.Source)
            || NameTokens(unit.Speaker).Any(name => NameSuffixes.Any(suffix => unit.Source.Contains(name + suffix, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> KnownNames(QaContext context) =>
        context.Characters.SelectMany(c => NameTokens(c.Source)).Concat(context.Glossary.SelectMany(t => NameTokens(t.Source)));

    /// <summary>O nome inteiro e cada pedaço separado por espaço (inclusive o ideográfico): "佐藤 美咲" vale como "佐藤" e "美咲".</summary>
    private static IEnumerable<string> NameTokens(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        var parts = trimmed.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? [trimmed, .. parts] : [trimmed];
    }

    private static Regex BuildSourceMatcher(IEnumerable<string> names)
    {
        var escaped = names.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
        var named = escaped.Count == 0 ? string.Empty : $"|(?:{string.Join('|', escaped)})(?:さん(?!ぽ)|さま|様|ちゃん|くん)";
        return new Regex(GenericSourceHonorific + named, RegexOptions.CultureInvariant, MatchTimeout);
    }

    // Nome em kanji ou katakana + ちゃん/くん, fora os termos de parentesco (赤ちゃん, お母ちゃん...) e o advérbio ちゃんと; ou 先輩.
    private const string GenericSourceHonorific =
        @"(?<=[一-鿿゠-ヿ])(?<![赤婆爺兄姉母父叔伯祖妹弟嬢])(?:ちゃん(?!と)|くん)|先輩|せんぱい|センパイ";

    [GeneratedRegex(@"-(?:san|kun|chan|sama)(?!\p{L})|(?<!\p{L})se[nm]pai(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TranslatedHonorific();
}

/// <summary>
/// Fala traduzida de um personagem que está em <c>characters.json</c> sem gênero definido. Sem gênero a IA e quem revisa adivinham a
/// concordância ("cansado" ou "cansada"). Dá um aviso por personagem, preso à primeira fala dele, com a contagem de falas; só avisa, e
/// a pessoa define o gênero no Glossário.
/// </summary>
public sealed class GenderRule : IQaRule
{
    public const string RuleId = "personagem-sem-genero";

    /// <inheritdoc />
    public string Id => RuleId;

    /// <inheritdoc />
    public string Description => "Personagem sem gênero definido em characters.json que tem falas traduzidas.";

    /// <inheritdoc />
    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckCharacters(context);
    }

    private static IEnumerable<QaIssue> CheckCharacters(QaContext context)
    {
        var book = new CharacterBook(context.Characters);
        var byCharacter = new Dictionary<string, (CharacterEntry Character, TextUnit First, string Key, int Count)>(StringComparer.Ordinal);
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                if (!unit.HasTranslation || book.Find(unit.Speaker) is not { Gender: CharacterGender.Unknown } character)
                {
                    continue;
                }

                var entry = byCharacter.TryGetValue(character.Source, out var seen) ? seen with { Count = seen.Count + 1 } : (character, unit, key, 1);
                byCharacter[character.Source] = entry;
            }
        }

        foreach (var (character, first, key, count) in byCharacter.Values)
        {
            var lines = count == 1 ? "1 fala traduzida" : $"{count} falas traduzidas";
            yield return new QaIssue(
                RuleId, QaSeverity.Warning, first.Id, key,
                $"“{character.Source}” não tem gênero definido em characters.json ({lines}); a concordância da tradução pode sair errada.");
        }
    }
}
