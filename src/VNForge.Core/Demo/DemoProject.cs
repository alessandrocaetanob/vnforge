using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Demo;

/// <summary>
/// Projeto de demonstração gerado em código com os nomes, números e estados dos artboards do canvas "VNForge v2 · App Windows"
/// (Painel, Editor, Conferir): 312 scripts, 48.211 linhas, 21% aprovadas, 18% revisadas, 6% traduzidas, 17% IA e 38% pendentes
/// (18.320 pendentes), os oito scripts da tabela do Painel com as suas linhas e os 23 personagens com a contagem de linhas da tabela.
/// Nenhum arquivo ou texto de jogo: as falas são as do artboard, escritas para o design.
/// </summary>
public static class DemoProject
{
    public const string Name = "Minha VN — PT-BR";
    public const string Root = @"D:\Traducoes\minha-vn-ptbr";
    public const int ScriptCount = 312;
    public const int LineCount = 48_211;
    public const string ModelId = "claude-opus-5-5";

    /// <summary>Os totais do Painel: 10.124 aprovadas, 8.678 revisadas, 2.893 traduzidas, 8.196 da IA e 18.320 pendentes.</summary>
    public static readonly StatusCounts Counts = new(Untranslated: 18_320, Machine: 8_196, Translated: 2_893, Reviewed: 8_678, Approved: 10_124);

    /// <summary>Os oito scripts da tabela "Por script" do Painel: chave, nome, linhas e a porcentagem de cada status.</summary>
    public static readonly IReadOnlyList<DemoScript> Named =
    [
        new("yst00012", "Prólogo", 412, 80, 20, 0),
        new("yst00013", "Capítulo 1 — Manhã de chuva", 890, 34, 21, 19),
        new("yst00014", "Capítulo 1 — Sala de aula", 1034, 15, 20, 30),
        new("yst00015", "Capítulo 1 — Intervalo", 611, 0, 0, 0),
        new("yst00016", "Capítulo 1 — Depois da aula", 728, 0, 0, 12),
        new("yst00017", "Capítulo 2 — Festival", 1302, 0, 0, 0),
        new("yst00090", "Sistema — menus e escolhas", 211, 40, 18, 20),
        new("yst00091", "Nomes de personagens", 121, 100, 0, 0),
    ];

    /// <summary>Os personagens da tabela "Por personagem" (japonês, português, linhas); o resto soma as 48.211 linhas em 15 coadjuvantes.</summary>
    public static readonly IReadOnlyList<DemoCharacter> Characters = BuildCharacters();

    /// <summary>O script aberto no artboard do Editor.</summary>
    public const string CurrentScript = "yst00013";

    private static readonly Lazy<(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts, IReadOnlyList<QaIssue> Issues)> Generated = new(Generate);

    /// <summary>Todos os textos, por chave de script, na ordem ordinal. Gerado uma vez por processo.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts => Generated.Value.Texts;

    public static string Label(string key) => Named.FirstOrDefault(s => s.Key == key)?.Label ?? string.Empty;

    /// <summary>As linhas e o progresso por script, como o Editor as recebe, com o relatório de <see cref="Report"/>.</summary>
    public static ProjectLines Lines() => ProjectLines.Create(Texts, Report);

    /// <summary>
    /// O QA do artboard: 2 erros (marcador diferente do original) e 37 avisos (espaço duplo ou antes de pontuação), cada um numa linha em que o
    /// defeito foi plantado no texto. É o relatório que o Painel e o Editor de demonstração mostram; a pasta de <see cref="Create"/> roda o QA de verdade.
    /// </summary>
    public static QaReport Report => new(Generated.Value.Issues, [], ["marcador-diferente", "espaco-duplo", "espaco-antes-pontuacao"]);

    public static ProjectSummary Summary(string? root = null) =>
        new(Name, root ?? Root, "yuris", 500, "cp1252", ModelId, ScriptCount, Counts);

    /// <summary>Texto de demonstração do artboard: a pasta do jogo que o projeto de exemplo registra. Não existe na máquina e nunca é aberta.</summary>
    private const string ArtboardGameDirectory = @"D:\Jogos\MinhaVN";

    /// <summary>Grava a pasta do projeto (<c>project.json</c>, <c>text/*.json</c>, glossário e personagens) em <paramref name="parent"/> e devolve o caminho.</summary>
    /// <param name="gameDirectory">Pasta do jogo registrada no projeto; sem ela, a do artboard (<c>D:\Jogos\MinhaVN</c>, que não existe aqui).</param>
    public static string Create(string parent, string? gameDirectory = null)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            Name,
            new GameConfig(gameDirectory ?? ArtboardGameDirectory, "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        foreach (var (key, units) in Texts)
        {
            folder.SaveText(key, units);
        }

        folder.SaveState(new PipelineState([], new Dictionary<string, StageState> { [ProjectExtractor.ExtractStage] = new(StageStatus.Completed) }));
        folder.SaveScriptNames(ChapterNames());
        // O gasto do cartão "Uso da IA" do Painel: US$ 26,40 em 2,1 M tokens de entrada e 0,9 M de saída.
        AiUsageLog.TryRecord(folder, new AiUsageTotals(2_100_000, 900_000, 0, 0, 26.40m, 1));
        folder.SaveCharacters([.. Characters.Where(c => c.Japanese is not null).Select(c => new CharacterEntry(c.Japanese!, c.Portuguese))]);
        folder.SaveGlossary(
        [
            new GlossaryTerm("遅刻", "atraso"),
            new GlossaryTerm("傘", "guarda-chuva"),
            new GlossaryTerm("担任の先生", "professor responsável"),
            new GlossaryTerm("校門", "portão da escola"),
        ]);
        return folder.Root;
    }

    /// <summary>Os sete capítulos do Painel, que a pessoa do artboard já renomeou; os scripts de menus e nomes ficam de fora, na pasta do sistema.</summary>
    public static readonly IReadOnlyList<string> ChapterTitles =
        ["Manhã de chuva", "O clube de leitura", "Festival da escola", "Depois da aula", "A promessa", "A volta para casa", "Último dia"];

    /// <summary>
    /// O <c>scripts.json</c> do projeto: o nome legível dos oito scripts da tabela, a pasta de cada um no jogo (<c>userscript/NN_capN</c>, como o
    /// <c>yst_list.ybn</c> as traz; <c>userscript/system</c> para menus e nomes) e o nome dos sete capítulos.
    /// </summary>
    public static ScriptNames ChapterNames()
    {
        var keys = Enumerable.Range(1, ScriptCount).Select(n => $"yst{n:D5}").ToList();
        var system = new HashSet<string>(["yst00090", "yst00091"], StringComparer.Ordinal);
        var story = keys.Where(k => !system.Contains(k)).ToList();
        var chapterOf = story.Select((k, i) => (k, chapter: i * ChapterTitles.Count / story.Count)).ToDictionary(p => p.k, p => p.chapter, StringComparer.Ordinal);
        var names = ScriptNames.Empty.WithDetected(keys.ToDictionary(
            k => k,
            k => new DetectedScript(
                Named.FirstOrDefault(s => s.Key == k)?.Label,
                chapterOf.TryGetValue(k, out var chapter) ? $"userscript/{chapter + 1:D2}_cap{chapter + 1}" : "userscript/system"),
            StringComparer.Ordinal));
        return story.Aggregate(names, (current, key) => current.WithGroup(key, ChapterTitles[chapterOf[key]]));
    }

    private static List<DemoCharacter> BuildCharacters()
    {
        var list = new List<DemoCharacter>
        {
            new(null, "Narração", 14_884),
            new("美咲", "Misaki", 8_120),
            new("健太", "Kenta", 6_431),
            new("陽菜", "Hina", 3_877),
            new("蓮", "Ren", 2_958),
            new("葵", "Aoi", 2_410),
            new("先生", "Professor", 1_020),
            new("健太の母", "Mãe do Kenta", 640),
        };
        var rest = LineCount - list.Sum(c => c.Lines);
        for (var i = 0; i < 15; i++)
        {
            list.Add(new DemoCharacter($"脇役{i + 1:D2}", $"Coadjuvante {i + 1:D2}", rest / 15 + (i < rest % 15 ? 1 : 0)));
        }

        return list;
    }

    private static (IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts, IReadOnlyList<QaIssue> Issues) Generate()
    {
        // Contagem por status dos oito scripts nomeados e o que sobra para os 304 outros.
        var budget = new Dictionary<TranslationStatus, int>
        {
            [TranslationStatus.Approved] = Counts.Approved,
            [TranslationStatus.Reviewed] = Counts.Reviewed,
            [TranslationStatus.Translated] = Counts.Translated,
            [TranslationStatus.Machine] = Counts.Machine,
            [TranslationStatus.Untranslated] = Counts.Untranslated,
        };
        var namedPlans = new Dictionary<string, StatusCounts>();
        foreach (var script in Named)
        {
            var plan = script.Plan();
            namedPlans[script.Key] = plan;
            budget[TranslationStatus.Approved] -= plan.Approved;
            budget[TranslationStatus.Reviewed] -= plan.Reviewed;
            budget[TranslationStatus.Translated] -= plan.Translated;
            budget[TranslationStatus.Machine] -= plan.Machine;
            budget[TranslationStatus.Untranslated] -= plan.Untranslated;
        }

        // Os scripts de fora da tabela andam do mais adiantado ao mais atrasado: aprovadas primeiro, pendentes por último.
        var stream = new Queue<TranslationStatus>(
            new[] { TranslationStatus.Approved, TranslationStatus.Reviewed, TranslationStatus.Translated, TranslationStatus.Machine, TranslationStatus.Untranslated }
                .SelectMany(s => Enumerable.Repeat(s, budget[s])));

        var otherLines = LineCount - Named.Sum(s => s.Lines);
        var others = Enumerable.Range(1, ScriptCount).Select(n => $"yst{n:D5}").Where(k => Named.All(s => s.Key != k)).ToList();
        var lengths = new Dictionary<string, int>();
        for (var i = 0; i < others.Count; i++)
        {
            lengths[others[i]] = otherLines / others.Count + (i < otherLines % others.Count ? 1 : 0);
        }

        var speakerOrder = SpeakerOrder();
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        var global = 0;
        var planted = new PlantedCounter();
        for (var n = 1; n <= ScriptCount; n++)
        {
            var key = $"yst{n:D5}";
            var named = Named.FirstOrDefault(s => s.Key == key);
            var length = named?.Lines ?? lengths[key];
            var statuses = new List<TranslationStatus>(length);
            if (named is not null)
            {
                var plan = namedPlans[key];
                foreach (var (status, count) in new[]
                {
                    (TranslationStatus.Approved, plan.Approved), (TranslationStatus.Reviewed, plan.Reviewed), (TranslationStatus.Translated, plan.Translated),
                    (TranslationStatus.Machine, plan.Machine), (TranslationStatus.Untranslated, plan.Untranslated),
                })
                {
                    statuses.AddRange(Enumerable.Repeat(status, count));
                }
            }
            else
            {
                for (var i = 0; i < length; i++)
                {
                    statuses.Add(stream.Dequeue());
                }
            }

            var units = new TextUnit[length];
            for (var i = 0; i < length; i++)
            {
                units[i] = Line(key, i, global++, statuses[i], speakerOrder, planted);
            }

            texts[key] = units;
        }

        return (texts, planted.Issues);
    }

    private sealed class PlantedCounter
    {
        public List<QaIssue> Issues { get; } = [];

        public int Errors => Issues.Count(i => i.Severity == QaSeverity.Error);

        public int Warnings => Issues.Count - Errors;
    }

    private static TextUnit Line(string key, int index, int global, TranslationStatus status, int[] speakerOrder, PlantedCounter planted)
    {
        var character = Characters[speakerOrder[global % speakerOrder.Length]];
        var scene = Scenes[global % Scenes.Length];
        var id = $"{key}:{index + 1:D4}";
        var source = scene.Source + @"\p";
        string? translation = status == TranslationStatus.Untranslated ? null : scene.Translation + @"\p";

        // Defeitos plantados nas linhas da IA e traduzidas, uma a cada 211: 2 marcadores faltando e 37 espaços duplos ou antes de pontuação.
        if (translation is not null && (status is TranslationStatus.Machine or TranslationStatus.Translated) && global % 211 == 5 && planted.Issues.Count < 39)
        {
            if (planted.Errors < 2)
            {
                translation = scene.Translation;
                planted.Issues.Add(new QaIssue("marcador-diferente", QaSeverity.Error, id, key, "Marcador diferente do original."));
            }
            else if (planted.Warnings % 2 == 0)
            {
                translation = scene.Translation.Replace(" ", "  ", StringComparison.Ordinal) + @"\p";
                planted.Issues.Add(new QaIssue("espaco-duplo", QaSeverity.Warning, id, key, "Espaço duplo."));
            }
            else
            {
                translation = scene.Translation.TrimEnd('.', '?', '!', '…') + @" !\p";
                planted.Issues.Add(new QaIssue("espaco-antes-pontuacao", QaSeverity.Warning, id, key, "Espaço antes de pontuação."));
            }
        }

        return new TextUnit(id, character.Japanese is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, character.Japanese, source, translation, status);
    }

    /// <summary>Uma permutação das 48.211 posições: cada personagem aparece exatamente tantas vezes quanto a tabela do Painel diz, espalhado pelo projeto.</summary>
    private static int[] SpeakerOrder()
    {
        var order = new int[LineCount];
        var owners = new int[LineCount];
        var at = 0;
        for (var c = 0; c < Characters.Count; c++)
        {
            for (var i = 0; i < Characters[c].Lines; i++)
            {
                owners[at++] = c;
            }
        }

        // 7919 é primo e não divide 48.211 = 37 × 1.303, então a multiplicação percorre todas as posições uma vez.
        for (var g = 0; g < LineCount; g++)
        {
            order[g] = owners[(int)((long)g * 7919 % LineCount)];
        }

        return order;
    }

    private static readonly (string Source, string Translation)[] Scenes =
    [
        ("窓の外では、朝から雨が降り続いている。", "Lá fora, a chuva não parou desde cedo."),
        ("「おはよう、健太くん。今日も遅刻ギリギリだね」", "Bom dia, Kenta. Quase atrasado de novo, hein?"),
        ("「うるさいな……傘が見つからなかったんだよ」", "Não enche... Eu não achava o guarda-chuva."),
        ("「ふふっ。じゃあ、一緒に入っていく？」", "Hehe. Quer dividir o meu, então?"),
        ("差し出された傘は、二人で使うには少し小さかった。", "O guarda-chuva que ela me ofereceu era um pouco pequeno para dois."),
        ("「……いいのか？濡れるぞ、お前」", "...Tem certeza? Você vai se molhar."),
        ("「平気平気！ほら、行こう！」", "Tranquilo! Vamos, anda!"),
        ("校門の前で、担任の先生が腕を組んで立っていた。", "No portão, nosso professor já nos esperava de braços cruzados."),
        ("「相沢、佐倉。二人そろって五分遅刻だ」", "Aizawa, Sakura. Os dois, cinco minutos atrasados."),
        ("「えへへ……すみませーん」", "Ehehe... Desculpaaa."),
        ("「なんで俺まで……」", "Por que eu também..."),
        ("こうして、俺たちの騒がしい一日が始まった。", "E assim começou o nosso dia barulhento."),
    ];
}

/// <summary>Um script da tabela do Painel e a porcentagem de linhas aprovadas, revisadas e da IA; o resto é pendente.</summary>
public sealed record DemoScript(string Key, string Label, int Lines, int ApprovedPercent, int ReviewedPercent, int MachinePercent)
{
    public StatusCounts Plan()
    {
        var approved = (int)Math.Round(Lines * ApprovedPercent / 100.0);
        var reviewed = (int)Math.Round(Lines * ReviewedPercent / 100.0);
        var machine = (int)Math.Round(Lines * MachinePercent / 100.0);
        return new StatusCounts(Lines - approved - reviewed - machine, machine, 0, reviewed, approved);
    }
}

public sealed record DemoCharacter(string? Japanese, string Portuguese, int Lines);
