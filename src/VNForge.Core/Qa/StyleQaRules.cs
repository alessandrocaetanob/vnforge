using System.Text.RegularExpressions;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Confere os honoríficos da tradução contra a escolha da aba Estilo: "manter" exige que o honorífico do original apareça,
/// "remover" não aceita nenhum e "adaptar" aceita só senpai (que segue o glossário). Só avisa; quem decide como reescrever é a pessoa,
/// e o aviso não trava o build (<see cref="QaReport.Blocks"/>).
/// </summary>
public sealed partial class HonorificsRule : UnitQaRule
{
    public const string RuleId = "honorifico-fora-do-estilo";

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

    /// <summary>O original traz um sufixo de tratamento japonês (ちゃん, くん, さん, さま, 様) depois de um nome, ou 先輩.</summary>
    public static bool HasHonorificInSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return SourceHonorific().IsMatch(source);
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
            HonorificsPolicy.Keep when found.Count == 0 && HasHonorificInSource(unit.Source) =>
                ["O original tem honorífico, mas o estilo do projeto manda manter e a tradução não traz nenhum (-san, -kun, -chan, -sama, senpai)."],
            HonorificsPolicy.Remove => [.. found.Select(h => $"'{h}' ficou na tradução; o estilo do projeto manda remover os honoríficos.")],
            HonorificsPolicy.Adapt => [.. found.Where(h => h != "senpai" && h != "sempai").Select(h => $"'{h}' ficou na tradução; o estilo do projeto manda adaptar os honoríficos.")],
            _ => [],
        };
    }

    [GeneratedRegex(@"-(?:san|kun|chan|sama)(?!\p{L})|(?<!\p{L})se[nm]pai(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TranslatedHonorific();

    [GeneratedRegex(@"[一-鿿゠-ヿ](?:ちゃん|くん|さん|さま|様)|先輩|せんぱい|センパイ")]
    private static partial Regex SourceHonorific();
}

/// <summary>
/// Fala de um personagem que está em <c>characters.json</c> sem gênero definido. Sem gênero a IA e quem revisa adivinham a concordância
/// ("cansado" ou "cansada"). Só avisa; a pessoa define o gênero no Glossário.
/// </summary>
public sealed class GenderRule : IQaRule
{
    public const string RuleId = "personagem-sem-genero";

    /// <inheritdoc />
    public string Id => RuleId;

    /// <inheritdoc />
    public string Description => "Fala de personagem sem gênero definido em characters.json.";

    /// <inheritdoc />
    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var book = new CharacterBook(context.Characters);
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                if (unit.HasTranslation && book.Find(unit.Speaker) is { Gender: CharacterGender.Unknown } character)
                {
                    yield return new QaIssue(
                        RuleId, QaSeverity.Warning, unit.Id, key,
                        $"'{character.Source}' não tem gênero definido em characters.json; a concordância da tradução pode sair errada.");
                }
            }
        }
    }
}
