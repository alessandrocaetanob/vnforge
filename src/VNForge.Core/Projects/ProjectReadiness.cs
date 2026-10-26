using VNForge.Core.Glossary;

namespace VNForge.Core.Projects;

/// <summary>O que o projeto pede à pessoa, da mais urgente (algo que impede) para a menos.</summary>
public enum ReadinessKind
{
    /// <summary>Falta a chave do Claude: a tradução com IA fica desligada.</summary>
    ApiKey,

    /// <summary>A Conferência tem erros que travam o teste no jogo.</summary>
    QaErrors,

    /// <summary>O gasto chegou ao limite que a pessoa definiu.</summary>
    SpendLimit,

    /// <summary>Há personagens sem gênero no glossário.</summary>
    CharacterGender,

    /// <summary>Nenhum teste no jogo foi feito: os acentos ainda não foram vistos na tela do jogo.</summary>
    AccentTest,

    /// <summary>Ninguém disse quem narra.</summary>
    Narrator,

    /// <summary>Não há guia de estilo (<c>style.md</c>).</summary>
    StyleGuide,

    /// <summary>A Conferência tem avisos que não travam nada.</summary>
    QaWarnings,
}

public enum ReadinessSeverity
{
    Warning,
    Error,
}

/// <summary>Um item de "Precisa de você". <paramref name="Count"/> é a quantidade (erros, avisos, personagens) quando o item conta algo, senão zero.</summary>
public sealed record ReadinessItem(ReadinessKind Kind, ReadinessSeverity Severity, int Count = 0);

/// <summary>
/// O que a prontidão lê: a chave, o gênero dos personagens, o narrador, o guia de estilo, o último teste no jogo, a Conferência e o limite de gasto.
/// <see cref="Read"/> traz do projeto o que está nele; a chave e o limite vêm do app (o Core não os conhece) e entram com <c>with</c>.
/// </summary>
public sealed record ReadinessFacts(
    bool HasApiKey = true,
    int QaErrors = 0,
    int QaWarnings = 0,
    int CharactersWithoutGender = 0,
    bool NarratorDefined = true,
    bool HasStyleGuide = true,
    bool TestedInGame = true,
    bool SpendLimitReached = false)
{
    public const string StyleGuideFileName = "style.md";

    /// <summary>Lê do projeto o que ele guarda; um arquivo que não abre conta como vazio, e o Painel abre mesmo assim. A chave fica presente e o limite, não atingido.</summary>
    public static ReadinessFacts Read(ProjectFolder project, QaTotals? qa)
    {
        ArgumentNullException.ThrowIfNull(project);
        var characters = Try(project.LoadCharacters, []);
        return new ReadinessFacts(
            QaErrors: qa?.Errors ?? 0,
            QaWarnings: qa?.Warnings ?? 0,
            CharactersWithoutGender: characters.Count(c => c.Gender == CharacterGender.Unknown),
            NarratorDefined: Try(() => project.LoadConfig().Style.Narrator.WhoOrNull is not null, true),
            HasStyleGuide: HasText(Path.Combine(project.Root, StyleGuideFileName)),
            TestedInGame: GameStateFile.Load(project).LastTest is not null);
    }

    private static bool HasText(string path)
    {
        try
        {
            return File.Exists(path) && !string.IsNullOrWhiteSpace(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static T Try<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}

/// <summary>A lista "Precisa de você" do Painel e do Início: o que falta para a tradução andar, sem rede e sem ler o jogo.</summary>
public static class ProjectReadiness
{
    /// <summary>Os itens que valem, os erros primeiro e, dentro de cada gravidade, na ordem de <see cref="ReadinessKind"/>. Vazia quando nada falta.</summary>
    public static IReadOnlyList<ReadinessItem> Evaluate(ReadinessFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var items = new List<ReadinessItem>();
        if (!facts.HasApiKey)
        {
            items.Add(new ReadinessItem(ReadinessKind.ApiKey, ReadinessSeverity.Error));
        }

        if (facts.QaErrors > 0)
        {
            items.Add(new ReadinessItem(ReadinessKind.QaErrors, ReadinessSeverity.Error, facts.QaErrors));
        }

        if (facts.SpendLimitReached)
        {
            items.Add(new ReadinessItem(ReadinessKind.SpendLimit, ReadinessSeverity.Warning));
        }

        if (facts.CharactersWithoutGender > 0)
        {
            items.Add(new ReadinessItem(ReadinessKind.CharacterGender, ReadinessSeverity.Warning, facts.CharactersWithoutGender));
        }

        if (!facts.TestedInGame)
        {
            items.Add(new ReadinessItem(ReadinessKind.AccentTest, ReadinessSeverity.Warning));
        }

        if (!facts.NarratorDefined)
        {
            items.Add(new ReadinessItem(ReadinessKind.Narrator, ReadinessSeverity.Warning));
        }

        if (!facts.HasStyleGuide)
        {
            items.Add(new ReadinessItem(ReadinessKind.StyleGuide, ReadinessSeverity.Warning));
        }

        if (facts.QaWarnings > 0)
        {
            items.Add(new ReadinessItem(ReadinessKind.QaWarnings, ReadinessSeverity.Warning, facts.QaWarnings));
        }

        return [.. items.OrderByDescending(i => i.Severity)];
    }
}
