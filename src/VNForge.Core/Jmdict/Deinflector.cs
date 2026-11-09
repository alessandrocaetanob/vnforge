namespace VNForge.Core.Jmdict;

/// <summary>A classe de conjugação de uma palavra, como o JMdict a marca em <c>pos</c>. <see cref="Masu"/> é a forma intermediária terminada em <c>ます</c>, só para encadear as formas de cortesia.</summary>
[Flags]
internal enum WordClass
{
    None = 0,
    Ichidan = 1,
    Godan = 2,
    Suru = 4,
    Kuru = 8,
    AdjI = 16,
    Masu = 32,
    All = 63,
}

/// <summary>
/// Uma regra de desconjugação: troca o fim <paramref name="From"/> por <paramref name="To"/>. <paramref name="In"/> diz que tipo de palavra o trecho conjugado precisa
/// ser para a regra valer (o trecho que sobra de uma regra anterior, ou qualquer um no começo) e <paramref name="Out"/> diz de que classe tem de ser a palavra que sai.
/// </summary>
internal sealed record DeinflectionRule(string From, string To, WordClass In, WordClass Out, string Reason);

/// <summary>Uma forma de dicionário possível para um trecho, com as razões aplicadas (da última conjugação para a primeira).</summary>
internal sealed record Deinflected(string Text, WordClass Class, IReadOnlyList<string> Reasons);

/// <summary>
/// Volta uma palavra conjugada à forma de dicionário (<c>行こう</c> para <c>行く</c>), com regras encadeadas ao estilo dos leitores de japonês: cada passo troca o fim da palavra
/// e diz que classe a palavra que sai precisa ter; quem consulta confere essa classe com a que o JMdict marca na entrada. Cobre os verbos ichidan, godan, suru e kuru e os adjetivos em い.
/// </summary>
internal static class Deinflector
{
    private const int MaxDepth = 5;

    private const int MaxCandidates = 64;

    // Linhas do godan: forma de dicionário, i (cortesia), a (negativa), e (potencial, imperativa), o (volitiva), e as formas em て e た.
    private static readonly (string U, string I, string A, string E, string O, string Te, string Ta)[] GodanRows =
    [
        ("う", "い", "わ", "え", "お", "って", "った"),
        ("く", "き", "か", "け", "こ", "いて", "いた"),
        ("ぐ", "ぎ", "が", "げ", "ご", "いで", "いだ"),
        ("す", "し", "さ", "せ", "そ", "して", "した"),
        ("つ", "ち", "た", "て", "と", "って", "った"),
        ("ぬ", "に", "な", "ね", "の", "んで", "んだ"),
        ("ぶ", "び", "ば", "べ", "ぼ", "んで", "んだ"),
        ("む", "み", "ま", "め", "も", "んで", "んだ"),
        ("る", "り", "ら", "れ", "ろ", "って", "った"),
    ];

    private static readonly DeinflectionRule[] Rules = BuildRules();

    public static IReadOnlyList<Deinflected> Candidates(string text)
    {
        var found = new List<Deinflected>();
        var seen = new HashSet<(string, WordClass)>();
        var frontier = new List<Deinflected> { new(text, WordClass.All, []) };
        for (var depth = 0; depth < MaxDepth && frontier.Count > 0 && found.Count < MaxCandidates; depth++)
        {
            var next = new List<Deinflected>();
            foreach (var current in frontier)
            {
                foreach (var step in Step(current).Where(c => seen.Add((c.Text, c.Class))))
                {
                    found.Add(step);
                    next.Add(step);
                }
            }

            frontier = next;
        }

        return found;
    }

    private static IEnumerable<Deinflected> Step(Deinflected current)
    {
        foreach (var rule in Rules)
        {
            if ((current.Class & rule.In) == 0 || !current.Text.EndsWith(rule.From, StringComparison.Ordinal))
            {
                continue;
            }

            // Toda palavra de dicionário que se conjuga tem pelo menos dois caracteres; um só seria só ruído ("て" virando "る").
            var text = string.Concat(current.Text.AsSpan(0, current.Text.Length - rule.From.Length), rule.To);
            if (text.Length >= 2)
            {
                yield return new Deinflected(text, rule.Out, current.Reasons.Contains(rule.Reason) ? current.Reasons : [.. current.Reasons, rule.Reason]);
            }
        }
    }

    /// <summary>A classe de conjugação das marcas <c>pos</c> de uma entrada do JMdict.</summary>
    public static WordClass ClassOf(IEnumerable<string> partsOfSpeech)
    {
        var result = WordClass.None;
        foreach (var part in partsOfSpeech)
        {
            result |= part switch
            {
                "v1" or "v1-s" => WordClass.Ichidan,
                "vs-i" or "vs-s" or "vs-c" => WordClass.Suru,
                "vk" => WordClass.Kuru,
                "adj-i" or "adj-ix" => WordClass.AdjI,
                _ when part.StartsWith("v5", StringComparison.Ordinal) => WordClass.Godan,
                _ => WordClass.None,
            };
        }

        return result;
    }

    private static DeinflectionRule[] BuildRules()
    {
        var rules = new List<DeinflectionRule>();
        AddPolite(rules);
        AddAdjective(rules);
        foreach (var row in GodanRows)
        {
            AddGodan(rules, row);
        }

        AddIchidan(rules);
        AddSuru(rules);
        AddKuru(rules);
        return [.. rules];
    }

    private static void Add(List<DeinflectionRule> rules, string from, string to, WordClass input, WordClass output, string reason) =>
        rules.Add(new DeinflectionRule(from, to, input, output, reason));

    private static void AddPolite(List<DeinflectionRule> rules)
    {
        Add(rules, "ませんでした", "ません", WordClass.All, WordClass.Masu, "passada");
        Add(rules, "ません", "ます", WordClass.Masu, WordClass.Masu, "negativa");
        Add(rules, "ました", "ます", WordClass.All, WordClass.Masu, "passada");
        Add(rules, "ましょう", "ます", WordClass.All, WordClass.Masu, "volitiva");
    }

    private static void AddAdjective(List<DeinflectionRule> rules)
    {
        Add(rules, "かった", "い", WordClass.All, WordClass.AdjI, "passada");
        Add(rules, "くない", "い", WordClass.AdjI, WordClass.AdjI, "negativa");
        Add(rules, "くて", "い", WordClass.All, WordClass.AdjI, "em て");
        Add(rules, "ければ", "い", WordClass.All, WordClass.AdjI, "condicional");
        Add(rules, "く", "い", WordClass.All, WordClass.AdjI, "adverbial");
        Add(rules, "さ", "い", WordClass.All, WordClass.AdjI, "nominalizada");
    }

    private static void AddGodan(List<DeinflectionRule> rules, (string U, string I, string A, string E, string O, string Te, string Ta) row)
    {
        Add(rules, row.I + "ます", row.U, WordClass.Masu, WordClass.Godan, "de cortesia");
        Add(rules, row.A + "ない", row.U, WordClass.AdjI, WordClass.Godan, "negativa");
        Add(rules, row.Te, row.U, WordClass.All, WordClass.Godan, "em て");
        Add(rules, row.Ta, row.U, WordClass.All, WordClass.Godan, "passada");
        Add(rules, row.I + "たい", row.U, WordClass.AdjI, WordClass.Godan, "desiderativa");
        Add(rules, row.E + "る", row.U, WordClass.Ichidan, WordClass.Godan, "potencial");
        Add(rules, row.A + "れる", row.U, WordClass.Ichidan, WordClass.Godan, "passiva");
        Add(rules, row.A + "せる", row.U, WordClass.Ichidan, WordClass.Godan, "causativa");
        Add(rules, row.O + "う", row.U, WordClass.All, WordClass.Godan, "volitiva");
        Add(rules, row.E + "ば", row.U, WordClass.All, WordClass.Godan, "condicional");
        Add(rules, row.E, row.U, WordClass.All, WordClass.Godan, "imperativa");
        if (row.U == "く")
        {
            // 行く é a exceção: o て e o た dele são って e った, como os de う, つ e る.
            Add(rules, "って", "く", WordClass.All, WordClass.Godan, "em て");
            Add(rules, "った", "く", WordClass.All, WordClass.Godan, "passada");
        }
    }

    private static void AddIchidan(List<DeinflectionRule> rules)
    {
        Add(rules, "ます", "る", WordClass.Masu, WordClass.Ichidan, "de cortesia");
        Add(rules, "ない", "る", WordClass.AdjI, WordClass.Ichidan, "negativa");
        Add(rules, "た", "る", WordClass.All, WordClass.Ichidan, "passada");
        Add(rules, "て", "る", WordClass.All, WordClass.Ichidan, "em て");
        Add(rules, "たい", "る", WordClass.AdjI, WordClass.Ichidan, "desiderativa");
        Add(rules, "られる", "る", WordClass.Ichidan, WordClass.Ichidan, "potencial ou passiva");
        Add(rules, "させる", "る", WordClass.Ichidan, WordClass.Ichidan, "causativa");
        Add(rules, "よう", "る", WordClass.All, WordClass.Ichidan, "volitiva");
        Add(rules, "れば", "る", WordClass.All, WordClass.Ichidan, "condicional");
        Add(rules, "ろ", "る", WordClass.All, WordClass.Ichidan, "imperativa");
    }

    private static void AddSuru(List<DeinflectionRule> rules)
    {
        Add(rules, "します", "する", WordClass.Masu, WordClass.Suru, "de cortesia");
        Add(rules, "しない", "する", WordClass.AdjI, WordClass.Suru, "negativa");
        Add(rules, "した", "する", WordClass.All, WordClass.Suru, "passada");
        Add(rules, "して", "する", WordClass.All, WordClass.Suru, "em て");
        Add(rules, "したい", "する", WordClass.AdjI, WordClass.Suru, "desiderativa");
        Add(rules, "される", "する", WordClass.Ichidan, WordClass.Suru, "passiva");
        Add(rules, "させる", "する", WordClass.Ichidan, WordClass.Suru, "causativa");
        Add(rules, "しよう", "する", WordClass.All, WordClass.Suru, "volitiva");
        Add(rules, "すれば", "する", WordClass.All, WordClass.Suru, "condicional");
        Add(rules, "しろ", "する", WordClass.All, WordClass.Suru, "imperativa");
    }

    private static void AddKuru(List<DeinflectionRule> rules)
    {
        // 来る escreve igual em formas lidas de jeitos diferentes; o kana segue a leitura (き, こ, く).
        foreach (var (stem, to) in new[] { ("来", "来る"), ("き", "くる"), ("こ", "くる") })
        {
            Add(rules, stem + "ます", to, WordClass.Masu, WordClass.Kuru, "de cortesia");
            Add(rules, stem + "ない", to, WordClass.AdjI, WordClass.Kuru, "negativa");
            Add(rules, stem + "た", to, WordClass.All, WordClass.Kuru, "passada");
            Add(rules, stem + "て", to, WordClass.All, WordClass.Kuru, "em て");
            Add(rules, stem + "よう", to, WordClass.All, WordClass.Kuru, "volitiva");
            Add(rules, stem + "られる", to, WordClass.Ichidan, WordClass.Kuru, "potencial ou passiva");
            Add(rules, stem + "させる", to, WordClass.Ichidan, WordClass.Kuru, "causativa");
        }

        Add(rules, "来れば", "来る", WordClass.All, WordClass.Kuru, "condicional");
        Add(rules, "くれば", "くる", WordClass.All, WordClass.Kuru, "condicional");
        Add(rules, "来い", "来る", WordClass.All, WordClass.Kuru, "imperativa");
        Add(rules, "こい", "くる", WordClass.All, WordClass.Kuru, "imperativa");
    }
}
