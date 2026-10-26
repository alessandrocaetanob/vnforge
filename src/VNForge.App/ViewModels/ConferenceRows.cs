using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Título curto e explicação de cada regra do <see cref="QaEngine"/>, no tom da tela Conferir. Regra fora da tabela cai na descrição do Core.</summary>
public static class ConferenceRuleTexts
{
    private static readonly Dictionary<string, (string Title, string Explanation)> ById = new(StringComparer.Ordinal)
    {
        [UntranslatedQaRule.RuleId] = ("Não traduzida", "A fala ainda não tem tradução. No teste no jogo ela aparece em japonês, então não trava nada."),
        ["traducao-vazia"] = ("Tradução vazia", "A fala está marcada como traduzida, mas o texto está vazio. O jogo mostraria uma caixa de texto vazia."),
        ["marcador-diferente"] = ("Falta ou sobra um marcador", "No fim de cada fala o jogo espera o clique para virar a página, e o nome do jogador também vem num selo. Cada virada de página e cada nome do original precisam estar na tradução, senão o jogo não abre a cena. A quebra de linha não entra na conta, porque o teste no jogo a refaz. Não corrijo sozinho porque o lugar do selo pode mudar na tradução."),
        ["glossario-diferente"] = ("Termo diferente do glossário", "O glossário fixa a tradução deste termo. Se a fala pede outra escolha, use Está certo assim para registrar a exceção na linha."),
        ["personagem-diferente"] = ("Nome de personagem diferente", "O nome do personagem foi traduzido diferente do que está no Glossário. Use Está certo assim se a variação for de propósito."),
        ["traducao-inconsistente"] = ("Mesmo original, traduções diferentes", "O mesmo original foi traduzido de jeitos diferentes. Pode ser de propósito (personagens diferentes); a lista completa abre na busca."),
        ["espaco-duplo"] = ("Espaço duplo", "Dois espaços seguidos viram uma coluna a mais na caixa e costumam ser sobra de edição."),
        ["espaco-antes-pontuacao"] = ("Espaço antes de pontuação", "Em português não há espaço antes de vírgula, ponto e vírgula, dois-pontos, interrogação ou exclamação. A fala fica errada até ser corrigida."),
        ["aspas-desbalanceadas"] = ("Aspas ou parênteses sem par", "Falta fechar ou abrir um par de aspas ou parênteses. Confira se a fala termina onde o original termina."),
        [UnencodableCharacterRule.RuleId] = ("Caractere fora da codificação", "A estratégia de acentuação do projeto não grava este caractere no jogo. Troque por um que ela aceite."),
        ["japones-na-traducao"] = ("Japonês na tradução", "Há hiragana, katakana ou kanji na tradução. Costuma ser um trecho que ficou sem traduzir."),
        [BoxLineLimitRule.RuleId] = ("Não cabe na caixa", "A tradução ocupa mais linhas que a caixa do jogo. Divida a fala em páginas ou peça à IA uma versão mais curta."),
        [ExtraPageQaRule.RuleId] = ("Página a mais", "A tradução vira a página mais vezes que o original: a fala foi dividida em páginas. Confira se a divisão está no lugar certo; se foi de propósito, use Está certo assim."),
        [FieldByteLimitRule.RuleId] = ("Passa do limite do campo", "Este campo tem um tamanho máximo no jogo e a tradução passa dele. Encurte o texto."),
        [HonorificsRule.RuleId] = ("Honorífico fora do estilo", "A tradução trata -san, -kun, -chan, -sama ou senpai de um jeito diferente do estilo do projeto. Mude o texto ou a escolha em Ajustes, aba Estilo; use Está certo assim se a exceção for de propósito."),
        [GenderRule.RuleId] = ("Personagem sem gênero", "A fala é de um personagem sem gênero no Glossário. Sem ele, a concordância (cansado ou cansada) é um palpite. Defina o gênero do personagem no Glossário antes de traduzir as falas dele."),
    };

    /// <summary>Título curto da regra; sem entrada, a descrição do Core (ou o id).</summary>
    public static string Title(string ruleId, string? description = null) =>
        ById.TryGetValue(ruleId, out var texts) ? texts.Title : description ?? ruleId;

    /// <summary>O que a regra confere e o que fazer; sem entrada, a descrição do Core (ou o id).</summary>
    public static string Explanation(string ruleId, string? description = null) =>
        ById.TryGetValue(ruleId, out var texts) ? texts.Explanation : description ?? ruleId;

    /// <summary>Verdadeiro quando a regra tem texto próprio; um teste exige isso de todas as regras do <see cref="QaEngine.Default"/>.</summary>
    public static bool Has(string ruleId) => ById.ContainsKey(ruleId);
}

/// <summary>
/// Uma linha da lista de problemas: uma ocorrência do QA ou, para "não traduzida", o resumo de um script inteiro (<see cref="Count"/> linhas).
/// Só leitura: a lista pode ter milhares delas.
/// </summary>
public sealed class ConferenceIssueRowViewModel
{
    private readonly string? ruleDescription;
    private readonly IReadOnlyList<QaIssue> occurrences;

    /// <param name="issue">A ocorrência; no resumo de um script, a primeira dele.</param>
    /// <param name="proposal">A correção proposta pelo <see cref="IQaFixer"/> da regra, quando há.</param>
    /// <param name="unit">A linha do projeto, para mostrar original e tradução no detalhe.</param>
    /// <param name="count">Quantas ocorrências a linha representa; maior que 1 no resumo por script e quando a regra aponta a mesma linha mais de uma vez.</param>
    /// <param name="ruleDescription">Descrição da regra no Core, usada quando o título não está na tabela.</param>
    /// <param name="linePosition">A posição da linha no script (de 1 em diante); o id da unidade é opaco e não serve de número.</param>
    /// <param name="sceneName">O nome da cena (<c>scripts.json</c>); sem ele a linha se localiza pelo id do script.</param>
    /// <param name="occurrences">Todas as ocorrências que a linha agrupa (a mesma regra na mesma linha); a lista usa isso para nomear cada caractere em vez de só o primeiro.</param>
    public ConferenceIssueRowViewModel(QaIssue issue, QaFix? proposal = null, TextUnit? unit = null, int count = 1, string? ruleDescription = null, string? sceneName = null, int? linePosition = null, IReadOnlyList<QaIssue>? occurrences = null)
    {
        Issue = issue;
        this.occurrences = occurrences is { Count: > 0 } ? occurrences : [issue];
        Proposal = proposal;
        Unit = unit;
        Count = count;
        this.ruleDescription = ruleDescription;
        SceneName = string.IsNullOrWhiteSpace(sceneName) ? null : sceneName;
        LineNumber = linePosition;
    }

    /// <summary>O nome da cena da linha; nulo quando o projeto ainda não tem nome para ela.</summary>
    public string? SceneName { get; }

    /// <summary>A cena como a pessoa a conhece: o nome, ou o id do script quando não há nome.</summary>
    public string SceneLabel => SceneName ?? ScriptKey;

    /// <summary>Onde está o problema: "Manhã de chuva · linha 43"; no resumo por script, só a cena.</summary>
    public string Where => IsScriptSummary || LineNumber is not { } number ? SceneLabel : $"{SceneLabel} · linha {number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A posição da linha no script, de 1 em diante, a mesma que o Editor e a paleta mostram; nula quando a linha não veio de um projeto.</summary>
    public int? LineNumber { get; }

    public QaIssue Issue { get; }

    public QaFix? Proposal { get; }

    public TextUnit? Unit { get; }

    /// <summary>Quantas ocorrências esta linha representa: 1, o total de linhas sem tradução do script no resumo, ou as repetições da mesma regra na mesma linha.</summary>
    public int Count { get; }

    public string RuleId => Issue.RuleId;

    public string UnitId => Issue.UnitId;

    public string ScriptKey => Issue.ScriptKey;

    public string Message => Issue.Message;

    public string Category => QaCategories.Of(RuleId);

    public bool IsError => Issue.Severity == QaSeverity.Error;

    /// <summary>"Não traduzida" de um script inteiro numa linha só.</summary>
    public bool IsScriptSummary => RuleId == UntranslatedQaRule.RuleId;

    /// <summary>A regra propõe a correção: "Corrigir e ir para o próximo" resolve a linha de uma vez.</summary>
    public bool IsOneClick => HasProposal;

    public SeverityLevel Level => IsError ? SeverityLevel.Error : SeverityLevel.Warning;

    public string SeverityLabel => IsError ? "Erro" : "Aviso";

    /// <summary>Só o erro bloqueia o build, e só o aviso de uma linha pode ser marcado como ok.</summary>
    public bool CanMarkOk => !IsError && !IsScriptSummary;

    public bool HasProposal => Proposal is not null;

    public string? ProposedText => Proposal?.After;

    /// <summary>Coluna "Linha" em mono: <c>yst00012:0042</c>; no resumo, só o script.</summary>
    public string LineLabel => IsScriptSummary ? ScriptKey : FormatLine(UnitId);

    public string Title => IsMarkerMismatch ? MarkerParts.Title : ConferenceRuleTexts.Title(RuleId, ruleDescription);

    private const string MarkerRuleId = "marcador-diferente";

    private bool IsMarkerMismatch => RuleId == MarkerRuleId;

    private MarkerDifference MarkerParts => MarkerDifference.Read(Message);

    public string Explanation => ConferenceRuleTexts.Explanation(RuleId, ruleDescription);

    /// <summary>O trecho que a lista mostra sob o título: o pedaço que a correção muda, ou a mensagem da regra.</summary>
    public string Detail
    {
        get
        {
            if (IsScriptSummary)
            {
                return Count == 1 ? "1 linha sem tradução neste script." : $"{ProjectLabels.Number(Count)} linhas sem tradução neste script.";
            }

            if (IsMarkerMismatch)
            {
                return MarkerParts.Detail;
            }

            if (Proposal is { } p)
            {
                return PlainMarkers(Excerpt(p.Before, p.After));
            }

            if (RuleId == UnencodableCharacterRule.RuleId)
            {
                return UnencodableSentence();
            }

            return PlainMarkers(Count > 1 ? $"{Message} (+{Count - 1})" : Message);
        }
    }

    /// <summary>A linha lista os caracteres que a acentuação não grava (todos os do grupo); o detalhe da tela mostra <see cref="CharacterSentence"/>.</summary>
    public bool HasCharacterList => RuleId == UnencodableCharacterRule.RuleId;

    private static readonly Regex UnencodablePattern = new(@"'[^']+' \(U\+(?<code>[0-9A-Fa-f]{4,6})\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// "O ★ não existe na acentuação do projeto."; com vários caracteres na linha, "O 。, o ～ e o ★ não existem…". Caractere invisível
    /// (espaço de largura zero, controle) sai como <c>U+200B</c>. O detalhe técnico (a estratégia) fica fora da lista.
    /// </summary>
    public string CharacterSentence => UnencodableSentence();

    private string UnencodableSentence()
    {
        var labels = new List<string>();
        foreach (var occurrence in occurrences)
        {
            var match = UnencodablePattern.Match(occurrence.Message);
            if (match.Success && int.TryParse(match.Groups["code"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
            {
                var label = CodePointLabel(codePoint);
                if (!labels.Contains(label))
                {
                    labels.Add(label);
                }
            }
        }

        if (labels.Count == 0)
        {
            return "Há um caractere que a acentuação do projeto não grava.";
        }

        var named = labels.Count == 1
            ? $"O {labels[0]} não existe"
            : $"O {string.Join(", o ", labels.Take(labels.Count - 1))} e o {labels[^1]} não existem";
        return $"{named} na acentuação do projeto.";
    }

    /// <summary>O caractere, ou <c>U+XXXX</c> quando ele não aparece na tela (controle, espaço, formato, substituto solto).</summary>
    private static string CodePointLabel(int codePoint)
    {
        if (codePoint is >= 0xD800 and <= 0xDFFF or > 0x10FFFF)
        {
            return $"U+{codePoint:X4}";
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
            ? $"U+{codePoint:X4}"
            : char.ConvertFromUtf32(codePoint);
    }

    private static readonly Regex MarkerPattern = new(@"\\[npcu\\{]|\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Troca cada marcador cru do texto pelo nome em português, entre parênteses: <c>anda !\p</c> vira <c>anda !(virar a página)</c>.</summary>
    public static string PlainMarkers(string text) =>
        MarkerPattern.Replace(text, m => $"({MarkerLabels.Name(m.Value, m.Value.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control)})");

    /// <summary>"Um clique" quando a regra propõe uma correção para a linha; vazio nas outras.</summary>
    public string Tag => HasProposal ? "Um clique" : string.Empty;

    /// <summary>Texto do original no detalhe; vazio quando a linha não veio do projeto.</summary>
    public string Original => Unit?.Source ?? string.Empty;

    public bool HasOriginal => Original.Length > 0;

    /// <summary>Tradução gravada no detalhe; vazia no resumo por script e em linha sem tradução.</summary>
    public string Translation => IsScriptSummary ? string.Empty : Unit?.Translation ?? string.Empty;

    public bool HasTranslation => Translation.Length > 0;

    /// <summary>Título do detalhe: o da regra; no resumo, o script e a contagem.</summary>
    public string DetailTitle => IsScriptSummary ? $"{Title} em {SceneLabel}" : Title;

    /// <summary><c>yst00012:000042</c> vira <c>yst00012:0042</c>; id fora do padrão volta como veio.</summary>
    public static string FormatLine(string unitId)
    {
        var colon = unitId.LastIndexOf(':');
        return colon > 0 && int.TryParse(unitId.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? $"{unitId[..colon]}:{number.ToString("D4", CultureInfo.InvariantCulture)}"
            : unitId;
    }

    /// <summary>
    /// O trecho de <paramref name="before"/> que <paramref name="after"/> muda, com a palavra de cada lado e entre aspas: <c>“certeza?  Você”</c>.
    /// </summary>
    private const int MaxExcerpt = 60;

    public static string Excerpt(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        var start = prefix;
        while (start > 0 && before[start - 1] == ' ')
        {
            start--;
        }

        while (start > 0 && before[start - 1] != ' ')
        {
            start--;
        }

        var end = Math.Max(prefix, before.Length - suffix);
        while (end < before.Length && before[end] == ' ')
        {
            end++;
        }

        while (end < before.Length && before[end] != ' ')
        {
            end++;
        }

        var snippet = before[start..end];
        return snippet.Length > MaxExcerpt ? $"“{snippet[..MaxExcerpt]}…”" : $"“{snippet}”";
    }
}

/// <summary>Os filtros da tela, em linguagem comum, na ordem em que aparecem.</summary>
public enum ConferenceFilterKind
{
    /// <summary>Tudo o que a conferência achou.</summary>
    All,

    /// <summary>Os erros: travam o teste no jogo.</summary>
    Blocking,

    /// <summary>Os problemas que têm correção proposta.</summary>
    OneClick,

    /// <summary>A tradução passa do tamanho da caixa do jogo.</summary>
    Box,

    /// <summary>Glossário, nomes de personagem e traduções diferentes para o mesmo original.</summary>
    WordsAndNames,

    /// <summary>Caracteres que a acentuação do projeto não grava.</summary>
    Accents,
}

/// <summary>
/// Um filtro da faixa de cima: o nome em linguagem comum e quantos problemas do projeto inteiro passam por ele (não só os da lista à vista).
/// </summary>
public sealed partial class ConferenceFilterViewModel(ConferenceFilterKind kind, string label) : ObservableObject
{
    public ConferenceFilterKind Kind { get; } = kind;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Verdadeiro quando o problema <paramref name="row"/> aparece neste filtro.</summary>
    public bool Passes(ConferenceIssueRowViewModel row) => Kind switch
    {
        ConferenceFilterKind.Blocking => row.IsError,
        ConferenceFilterKind.OneClick => row.IsOneClick,
        ConferenceFilterKind.Box => row.Category == QaCategories.TextBox,
        ConferenceFilterKind.WordsAndNames => row.Category is QaCategories.Glossary or QaCategories.Consistency,
        ConferenceFilterKind.Accents => row.Category == QaCategories.Accents,
        _ => true,
    };
}

/// <summary>O que a regra de marcadores achou, em português: "Falta virar a página." no lugar do marcador cru.</summary>
internal readonly record struct MarkerDifference(IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, bool Invalid)
{
    public static MarkerDifference Read(string message)
    {
        if (message.StartsWith("marcador inválido", StringComparison.Ordinal))
        {
            return new MarkerDifference([], [], Invalid: true);
        }

        var colon = message.IndexOf(':');
        var parts = (colon < 0 ? message : message[(colon + 1)..]).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<string> missing = [];
        IReadOnlyList<string> extra = [];
        foreach (var part in parts)
        {
            if (part.StartsWith("falta ", StringComparison.Ordinal))
            {
                missing = Names(part["falta ".Length..]);
            }
            else if (part.StartsWith("sobra ", StringComparison.Ordinal))
            {
                extra = Names(part["sobra ".Length..]);
            }
        }

        return new MarkerDifference(missing, extra, Invalid: false);
    }

    public string Title => (Missing.Count, Extra.Count, Invalid) switch
    {
        (_, _, true) => "Marcador inválido na tradução",
        (> 0, > 0, _) => "Faltam e sobram marcadores",
        (_, > 0, _) => "Sobra um marcador na tradução",
        _ => "Falta um marcador do original",
    };

    public string Detail
    {
        get
        {
            if (Invalid)
            {
                return "Há uma barra ou uma chave sem par na tradução.";
            }

            List<string> sentences = [];
            if (Missing.Count > 0)
            {
                sentences.Add($"Falta {string.Join(", ", Missing)}.");
            }

            if (Extra.Count > 0)
            {
                sentences.Add($"Sobra {string.Join(", ", Extra)}.");
            }

            return sentences.Count > 0 ? string.Join(" ", sentences) : "Os marcadores não batem com os do original.";
        }
    }

    private static List<string> Names(string markers) =>
        [.. markers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => MarkerLabels.Name(m, m.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({g.Count()} vezes)")];
}
