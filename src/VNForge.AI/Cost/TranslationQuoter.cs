using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>O que a pessoa escolhe traduzir na janela "Traduzir com IA".</summary>
public enum TranslationScope
{
    /// <summary>O script aberto no editor.</summary>
    Scene,

    /// <summary>A faixa de scripts a que o script aberto pertence.</summary>
    Chapter,

    /// <summary>Todos os scripts do projeto.</summary>
    Game,
}

/// <summary>O preço e o tempo de traduzir um escopo com uma qualidade.</summary>
public sealed record QualityQuote(QualityProfile Profile, CostQuote Quote, TimeSpan Time)
{
    public TranslationQuality Quality => Profile.Quality;

    /// <summary>O valor estimado em reais, com a cotação que a pessoa informou.</summary>
    public decimal Brl => Quote.Brl;
}

/// <summary>As três qualidades orçadas para as mesmas linhas, da Rápida para a Caprichada.</summary>
/// <param name="Lines">Linhas que iriam à API.</param>
public sealed record TranslationQuotes(int Lines, IReadOnlyList<QualityQuote> Qualities)
{
    public QualityQuote For(TranslationQuality quality) => Qualities.Single(q => q.Quality == quality);

    /// <summary>O pedido cabe no limite: o gasto do projeto mais o pedido não passa dele. Sem limite, sempre cabe.</summary>
    public static bool Fits(decimal request, decimal spent, decimal? limit) => limit is not { } cap || spent + request <= cap;

    /// <summary>A qualidade mais cara que ainda cabe no que sobra do limite; <see langword="null"/> quando nem a Rápida cabe.</summary>
    public QualityQuote? MostCarefulThatFits(decimal spent, decimal? limit) =>
        Qualities.OrderByDescending(q => q.Quality).FirstOrDefault(q => Fits(q.Brl, spent, limit));
}

/// <summary>
/// Calcula o que a janela "Traduzir com IA" mostra: quais scripts entram em cada escopo, quais linhas faltam e quanto custa e demora
/// cada qualidade. Não chama a API: a estimativa é a de <see cref="CostEstimator"/>.
/// </summary>
public static class TranslationQuoter
{
    /// <summary>Scripts por capítulo enquanto o projeto não tem os grupos de scripts que a pessoa define; o capítulo é a faixa numérica em que a cena cai.</summary>
    public const int ChapterSize = 20;

    /// <summary>As chaves dos scripts do escopo, na ordem. Vazio quando o escopo depende de uma cena que não existe.</summary>
    public static IReadOnlyList<string> KeysFor(TranslationScope scope, IEnumerable<string> allKeys, string? sceneKey)
    {
        ArgumentNullException.ThrowIfNull(allKeys);
        var ordered = allKeys.Order(StringComparer.Ordinal).ToList();
        var index = sceneKey is null ? -1 : ordered.IndexOf(sceneKey);
        return scope switch
        {
            TranslationScope.Game => ordered,
            TranslationScope.Scene => index < 0 ? [] : [ordered[index]],
            TranslationScope.Chapter => index < 0 ? [] : [.. ordered.Skip(index / ChapterSize * ChapterSize).Take(ChapterSize)],
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }

    /// <summary>As linhas dos scripts <paramref name="keys"/> que ainda não têm tradução e têm original.</summary>
    public static IReadOnlyList<TextUnit> PendingLines(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(keys);
        return
        [
            .. keys
                .Where(texts.ContainsKey)
                .SelectMany(key => texts[key])
                .Where(u => u.Status == TranslationStatus.Untranslated && !string.IsNullOrWhiteSpace(u.Source)),
        ];
    }

    /// <summary>O preço em reais e o tempo de cada qualidade para <paramref name="lines"/>.</summary>
    public static TranslationQuotes Quote(IReadOnlyList<TextUnit> lines, ExchangeRate rate, TranslationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(rate);
        var byModel = CostEstimator.QuoteForModels(lines, QualityProfiles.All.Select(p => p.Model), rate, cache);
        var pending = byModel.Values.First().Estimate.Lines;
        return new TranslationQuotes(
            pending,
            [.. QualityProfiles.All.Select(p => new QualityQuote(p, byModel[p.Model], p.TimeFor(pending)))]);
    }
}
