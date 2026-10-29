using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Formatting;
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
        ["traducao-inconsistente"] = ("Mesmo original, traduções diferentes", "O mesmo original foi traduzido de jeitos diferentes. O cartão mostra cada tradução, as linhas dela e o que difere da mais usada. Pode ser de propósito (personagens diferentes): use Manter diferentes de propósito."),
        [SpellingRule.RuleId] = ("Possível erro de digitação", "A palavra não está no dicionário de português nem no Glossário. Confira a grafia e os acentos; nomes de personagem e termos entram no Glossário. Se a grafia for de propósito, use Está certo assim."),
        ["espaco-duplo"] = ("Espaço duplo", "Dois espaços seguidos viram uma coluna a mais na caixa e costumam ser sobra de edição."),
        ["espaco-antes-pontuacao"] = ("Espaço antes de pontuação", "Em português não há espaço antes de vírgula, ponto e vírgula, dois-pontos, interrogação ou exclamação. A fala fica errada até ser corrigida."),
        ["aspas-desbalanceadas"] = ("Aspas ou parênteses sem par", "Falta fechar ou abrir um par de aspas ou parênteses. Confira se a fala termina onde o original termina."),
        [UnencodableCharacterRule.RuleId] = ("Caractere fora da codificação", "A estratégia de acentuação do projeto não grava este caractere no jogo. Troque por um que ela aceite."),
        [FormattingConflictRule.RuleId] = ("Ajuste que a acentuação não grava", "Um ajuste de formatação do projeto gera um caractere que a estratégia de acentuação não grava no jogo. A tradução está certa: troque o ajuste em Ajustes, na aba Formatação."),
        [SpeakerEncodingRule.RuleId] = ("Nome do personagem fora da codificação", "O nome japonês deste personagem não existe na codificação do jogo, então a linha não pode ir para o jogo. Cadastre a tradução do nome no Glossário > Personagens."),
        [GameTitleEncodingRule.RuleId] = ("Título do jogo fora da codificação", "O título da janela do jogo tem um caractere que a estratégia de acentuação não grava. Troque a tradução do título em Ajustes, na aba Estilo, ou a estratégia na aba Acentos."),
        ["japones-na-traducao"] =("Japonês na tradução", "Há hiragana, katakana ou kanji na tradução. Costuma ser um trecho que ficou sem traduzir."),
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

/// <summary>Como uma linha da Conferência se nomeia na tela.</summary>
/// <param name="RuleDescription">Descrição da regra no Core, usada quando o título não está na tabela.</param>
/// <param name="SceneName">Como a cena se chama na tela: o nome do <c>scripts.json</c> ou "Cena N" quando o projeto não o guarda (N é a posição do script entre todos os do projeto, em ordem de chave, a partir de 1; não vem do id); sem ele (só em teste) a linha cai no id do script.</param>
/// <param name="LinePosition">A posição da linha no script (de 1 em diante); o id da unidade é opaco e não serve de número.</param>
public sealed record ConferenceRowLabels(string? RuleDescription = null, string? SceneName = null, int? LinePosition = null);

/// <summary>O que a dica e o painel do caractere precisam saber do projeto.</summary>
/// <param name="Encoder">O encoder da estratégia do projeto; a dica do caractere usa o substituto que o corretor grava. Sem ele, a dica cai na tabela de pontuação japonesa.</param>
/// <param name="Settings">Os ajustes de formatação em conflito com a estratégia; um caractere que vem de um deles tem o painel explicando o ajuste.</param>
/// <param name="SettingField">O campo do ajuste (<see cref="FormattingConflict.Field"/>) de que esta linha é uma ocorrência; a lista não a mostra, o card do ajuste a representa.</param>
public sealed record ConferenceRowCharacterContext(ITextEncoder? Encoder = null, IReadOnlyList<FormattingConflict>? Settings = null, string? SettingField = null);

/// <summary>
/// Uma linha da lista de problemas: uma ocorrência do QA ou, para "não traduzida", o resumo de um script inteiro (<see cref="Count"/> linhas).
/// Só leitura: a lista pode ter milhares delas.
/// </summary>
public sealed class ConferenceIssueRowViewModel
{
    private readonly string? ruleDescription;
    private readonly IReadOnlyList<QaIssue> occurrences;
    private readonly ITextEncoder? encoder;
    private readonly IReadOnlyList<FormattingConflict> settings;

    /// <param name="issue">A ocorrência; no resumo de um script, a primeira dele.</param>
    /// <param name="proposal">A correção proposta pelo <see cref="IQaFixer"/> da regra, quando há.</param>
    /// <param name="unit">A linha do projeto, para mostrar original e tradução no detalhe.</param>
    /// <param name="count">Quantas ocorrências a linha representa; maior que 1 no resumo por script e quando a regra aponta a mesma linha mais de uma vez.</param>
    /// <param name="occurrences">Todas as ocorrências que a linha agrupa (a mesma regra na mesma linha); a lista usa isso para nomear cada caractere em vez de só o primeiro.</param>
    /// <param name="labels">Como a linha se nomeia na tela: descrição da regra, cena e posição no script.</param>
    /// <param name="characters">O que a dica e o painel do caractere precisam: encoder, ajustes em conflito e o campo do ajuste.</param>
    public ConferenceIssueRowViewModel(QaIssue issue, QaFix? proposal = null, TextUnit? unit = null, int count = 1, IReadOnlyList<QaIssue>? occurrences = null,
        ConferenceRowLabels? labels = null, ConferenceRowCharacterContext? characters = null)
    {
        labels ??= new ConferenceRowLabels();
        characters ??= new ConferenceRowCharacterContext();
        encoder = characters.Encoder;
        settings = characters.Settings ?? [];
        SettingField = characters.SettingField;
        Issue = issue;
        this.occurrences = occurrences is { Count: > 0 } ? occurrences : [issue];
        Proposal = proposal;
        Unit = unit;
        Count = count;
        ruleDescription = labels.RuleDescription;
        SceneName = string.IsNullOrWhiteSpace(labels.SceneName) ? null : labels.SceneName;
        LineNumber = labels.LinePosition;
    }

    /// <summary>A cena da linha como a pessoa a lê ("Manhã de chuva" ou "Cena 13", a posição do script no projeto e não um número tirado do id); nula só quando a linha foi montada sem projeto.</summary>
    public string? SceneName { get; }

    /// <summary>A cena como a pessoa a conhece; o id do script só entra quando a linha não veio de um projeto.</summary>
    public string SceneLabel => SceneName ?? ScriptKey;

    /// <summary>Onde está o problema: "Manhã de chuva · linha 43"; no resumo por script, só a cena.</summary>
    public string Where => IsScriptSummary || LineNumber is not { } number ? SceneLabel : $"{SceneLabel} · linha {number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A posição da linha no script, de 1 em diante, a mesma que o Editor e a paleta mostram; nula quando a linha não veio de um projeto.</summary>
    public int? LineNumber { get; }

    public QaIssue Issue { get; }

    public QaFix? Proposal { get; }

    public TextUnit? Unit { get; }

    /// <summary>O nome de quem fala exatamente como o texto o guarda (sem aparar nem normalizar): o falante escolhido na linha, quando há, senão o extraído; nulo na narração e no resumo por script.</summary>
    public string? Speaker => IsScriptSummary || string.IsNullOrEmpty(Unit?.EffectiveSpeaker) ? null : Unit.EffectiveSpeaker;

    public bool HasSpeaker => Speaker is not null;

    /// <summary>O erro é o do nome do falante sem cadastro: o painel oferece "Cadastrar personagem" com o <see cref="Speaker"/> já preenchido.</summary>
    public bool CanRegisterCharacter => RuleId == SpeakerEncodingRule.RuleId && HasSpeaker;

    /// <summary>Quantas ocorrências esta linha representa: 1, o total de linhas sem tradução do script no resumo, ou as repetições da mesma regra na mesma linha.</summary>
    public int Count { get; }

    public string RuleId => Issue.RuleId;

    public string UnitId => Issue.UnitId;

    public string ScriptKey => Issue.ScriptKey;

    public string Message => Issue.Message;

    public string Category => QaCategories.Of(RuleId);

    public bool IsError => Issue.Severity == QaSeverity.Error;

    /// <summary>O campo do ajuste de formatação de que esta linha é uma ocorrência (<c>typography.ellipsis</c>); nulo nas outras. Essas linhas contam, mas o card do ajuste as representa na lista.</summary>
    public string? SettingField { get; }

    /// <summary>O aviso é de "mesmo original, traduções diferentes" e o cartão do original (<see cref="ConferenceConsistencyCardViewModel"/>) o representa na lista; conta nos totais.</summary>
    public bool InGroup { get; init; }

    /// <summary>"Não traduzida" de um script inteiro numa linha só.</summary>
    public bool IsScriptSummary => RuleId == UntranslatedQaRule.RuleId;

    /// <summary>A regra propõe a correção: "Corrigir e ir para o próximo" resolve a linha de uma vez.</summary>
    public bool IsOneClick => HasProposal || SettingField is not null;

    public SeverityLevel Level => IsError ? SeverityLevel.Error : SeverityLevel.Warning;

    public string SeverityLabel => IsError ? "Erro" : "Aviso";

    /// <summary>Só o erro bloqueia o build, e só o aviso de uma linha pode ser marcado como ok.</summary>
    public bool CanMarkOk => !IsError && !IsScriptSummary;

    public bool HasProposal => Proposal is not null;

    public string? ProposedText => Proposal?.After;

    /// <summary>Coluna "Linha" em mono: <c>yst00012:0042</c>; no resumo, só o script.</summary>
    public string LineLabel => IsScriptSummary ? ScriptKey : RuleId == GameTitleEncodingRule.RuleId ? "Título do jogo" : FormatLine(UnitId);

    public string Title => IsMarkerMismatch ? MarkerParts.Title : ConferenceRuleTexts.Title(RuleId, ruleDescription);

    private const string MarkerRuleId = "marcador-diferente";

    private bool IsMarkerMismatch => RuleId == MarkerRuleId;

    private MarkerDifference MarkerParts => MarkerDifference.Read(Message);

    public string Explanation => SettingExplanation() ?? ConferenceRuleTexts.Explanation(RuleId, ruleDescription);

    /// <summary>
    /// Quando o caractere vem de um ajuste de formatação (ou a linha é a de um ajuste), o painel explica o ajuste em vez de mandar trocar o caractere:
    /// a tradução gravada está certa e quem muda é o ajuste.
    /// </summary>
    private string? SettingExplanation()
    {
        if (RuleId != UnencodableCharacterRule.RuleId && RuleId != FormattingConflictRule.RuleId)
        {
            return null;
        }

        var codePoints = RuleId == UnencodableCharacterRule.RuleId ? UnencodableCodePoints() : [];
        var conflict = settings.FirstOrDefault(c => RuleId == FormattingConflictRule.RuleId
            ? Issue.Message.StartsWith($"O ajuste {c.Setting} ({c.CurrentLabel}) ", StringComparison.Ordinal)
            : codePoints.Any(code => c.Characters.EnumerateRunes().Any(r => r.Value == code)));
        if (conflict is null)
        {
            return null;
        }

        return $"O ajuste {conflict.Setting} ({conflict.CurrentLabel}) gera este caractere, e a estratégia de acentuação do projeto não o grava no jogo. A tradução está certa: troque o ajuste em Ajustes, na aba Formatação.";
    }

    /// <summary>O trecho que a lista mostra sob o título, com os marcadores por extenso entre parênteses: <c>anda !(virar a página)</c>. Serve de dica e de nome acessível.</summary>
    public string Detail => HasRawMarkers ? PlainMarkers(RawDetail) : RawDetail;

    /// <summary>
    /// O mesmo trecho com os marcadores crus (<c>anda !\p</c>), para a lista trocá-los por selos (<c>MarkedText</c>). A mensagem do Core não muda: a CLI a imprime como está.
    /// Texto com marcador fora da gramática do jogo cai em <see cref="Detail"/>, porque o selo só vale para marcador que o jogo lê.
    /// </summary>
    public string DetailMarked
    {
        get
        {
            var raw = RawDetail;
            if (!HasRawMarkers)
            {
                return raw;
            }

            try
            {
                ControlMarkers.Split(raw);
                return raw;
            }
            catch (ArgumentException)
            {
                return PlainMarkers(raw);
            }
        }
    }

    /// <summary>O trecho da lista leva selo de marcador: a linha dele é mais alta que a das outras.</summary>
    public bool HasDetailMarkers => !string.Equals(DetailMarked, Detail, StringComparison.Ordinal);

    private string RawDetail
    {
        get
        {
            if (IsScriptSummary)
            {
                return Count == 1 ? "1 linha sem tradução nesta cena." : $"{ProjectLabels.Number(Count)} linhas sem tradução nesta cena.";
            }

            if (IsMarkerMismatch)
            {
                return MarkerParts.Detail;
            }

            if (RuleId == UnencodableCharacterRule.RuleId)
            {
                return UnencodableSentence();
            }

            if (Proposal is { } p)
            {
                return Excerpt(p.Before, p.After);
            }

            var sentence = ProjectLabels.Sentence(Message);
            return Count > 1 ? $"{sentence} (+{ProjectLabels.Number(Count - 1)})" : sentence;
        }
    }

    /// <summary>O <see cref="RawDetail"/> é texto de tradução ou mensagem do QA, que pode trazer marcadores; as frases próprias da tela (falta, caractere, contagem) não.</summary>
    private bool HasRawMarkers => !IsScriptSummary && !IsMarkerMismatch && RuleId != UnencodableCharacterRule.RuleId;

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
        var codePoints = UnencodableCodePoints();
        if (codePoints.Count == 0)
        {
            return "Há um caractere que a acentuação do projeto não grava.";
        }

        var labels = codePoints.Select(CodePointLabel).ToList();
        var named = labels.Count == 1
            ? $"O {labels[0]} não existe"
            : $"O {string.Join(", o ", labels.Take(labels.Count - 1))} e o {labels[^1]} não existem";
        var replacements = codePoints.Select(ReplacementHint).OfType<string>().ToList();
        var hint = replacements.Count == 0 ? string.Empty : $" Use {string.Join(", ", replacements)}.";
        return $"{named} na acentuação do projeto.{hint}";
    }

    /// <summary>Os caracteres distintos que as ocorrências da linha apontam, na ordem em que aparecem.</summary>
    private List<int> UnencodableCodePoints()
    {
        var codePoints = new List<int>();
        foreach (var occurrence in occurrences)
        {
            var match = UnencodablePattern.Match(occurrence.Message);
            if (match.Success
                && int.TryParse(match.Groups["code"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint)
                && !codePoints.Contains(codePoint))
            {
                codePoints.Add(codePoint);
            }
        }

        return codePoints;
    }

    private static readonly Dictionary<int, (string Article, string Name)> ConfusableNames = BuildConfusableNames();

    private static readonly Dictionary<string, string> SubstituteNames = new(StringComparer.Ordinal)
    {
        ["..."] = "três pontos",
        ["\""] = "aspas retas",
        ["'"] = "aspa reta",
        ["-"] = "hífen",
    };

    private static Dictionary<int, (string Article, string Name)> BuildConfusableNames()
    {
        var names = new Dictionary<int, (string, string)>();
        foreach (var code in new[] { 0x201C, 0x201D, 0x201E })
        {
            names[code] = ("das", "aspas curvas");
        }

        foreach (var code in new[] { 0x2018, 0x2019, 0x201A })
        {
            names[code] = ("da", "aspa curva");
        }

        names[0x2026] = ("das", "reticências");
        names[0x2013] = ("da", "meia-risca");
        names[0x2014] = ("do", "travessão");
        return names;
    }

    /// <summary>
    /// "~ no lugar do til japonês (～)" para o que o corretor ou a tabela de <see cref="JapanesePunctuation"/> cobre; nulo para o resto. Quando o substituto se
    /// confunde com o original na tela (três pontos e reticências, aspas retas e curvas, hífen e travessão), cada lado vem por extenso. O caractere citado
    /// sempre fecha entre parênteses, para o ponto final da frase não colar nele.
    /// </summary>
    private string? ReplacementHint(int codePoint)
    {
        var substitute = (encoder is null ? null : UnwritableCharacters.SubstituteFor(codePoint, encoder)) ?? JapanesePunctuation.KeyboardFor(codePoint);
        if (substitute is null)
        {
            return null;
        }

        var replacement = SubstituteLabel(substitute);
        var shown = CodePointLabel(codePoint).Split(' ')[0];
        if (ConfusableNames.TryGetValue(codePoint, out var confusable))
        {
            return $"{replacement} no lugar {confusable.Article} {confusable.Name} ({(codePoint is 0x2013 or 0x2014 ? $"U+{codePoint:X4}" : shown)})";
        }

        var name = JapanesePunctuation.NameOf(codePoint) ?? "caractere";
        return $"{replacement} no lugar do {name} ({shown})";
    }

    private static string SubstituteLabel(string substitute)
    {
        if (substitute == " ")
        {
            return "um espaço comum";
        }

        return SubstituteNames.TryGetValue(substitute, out var word) ? $"{word} ({substitute})" : substitute;
    }

    /// <summary>O caractere, ou <c>U+XXXX</c> quando ele não aparece na tela (controle, espaço, formato, substituto solto).</summary>
    private static string CodePointLabel(int codePoint)
    {
        if (codePoint is >= 0xD800 and <= 0xDFFF or > 0x10FFFF)
        {
            return $"U+{codePoint:X4}";
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        var hidden = category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
        var glyph = hidden ? $"U+{codePoint:X4}" : char.ConvertFromUtf32(codePoint);
        var known = JapanesePunctuation.NameOf(codePoint) ?? (ConfusableNames.TryGetValue(codePoint, out var confusable) ? confusable.Name : null);
        return (known, hidden) switch
        {
            ({ } name, true) => $"{glyph} ({name})",
            ({ } name, false) => $"{glyph} ({name}, U+{codePoint:X4})",
            _ => glyph,
        };
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

    private const string MachineDraftNote = "Rascunho da IA, ainda não revisado";

    /// <summary>
    /// O texto da linha é um rascunho da IA que ninguém revisou. O build grava esse texto mesmo assim, então o problema vale;
    /// a lista e o detalhe dizem de onde ele veio para a pessoa não procurar uma tradução que nunca fez.
    /// </summary>
    public bool IsMachineDraft => HasTranslation && Unit is { Status: TranslationStatus.Machine };

    /// <summary>A nota da lista sob a cena; vazia quando a linha não é um rascunho da IA.</summary>
    public string OriginNote => IsMachineDraft ? MachineDraftNote : string.Empty;

    /// <summary>O título da tradução no detalhe: "Tradução de hoje", ou a origem quando é rascunho da IA.</summary>
    public string TranslationLabel => IsMachineDraft ? MachineDraftNote : "Tradução de hoje";

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
        if (snippet.Length <= MaxExcerpt)
        {
            return $"“{snippet}”";
        }

        // Cortar entre a barra e a letra de um marcador deixaria uma barra solta no fim.
        var cut = snippet[MaxExcerpt - 1] == '\\' ? MaxExcerpt - 1 : MaxExcerpt;
        return $"“{snippet[..cut]}…”";
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
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({ProjectLabels.Number(g.Count())} vezes)")];
}

/// <summary>
/// O card de um ajuste de formatação que a estratégia de acentuação não grava, no topo da lista de erros no lugar das linhas que ele causa:
/// "O ajuste Reticências gera 4.114 erros", a frase do motivo e o botão que troca o ajuste pelo valor seguro.
/// </summary>
public sealed class ConferenceSettingCardViewModel
{
    public ConferenceSettingCardViewModel(FormattingConflict conflict, string strategyId, IReadOnlyList<ConferenceIssueRowViewModel> rows)
    {
        Conflict = conflict;
        Rows = rows;
        var errors = rows.Count == 1 ? "1 erro" : $"{ProjectLabels.Number(rows.Count)} erros";
        Title = $"O ajuste {conflict.Setting} gera {errors}";
        var translations = conflict.Field == FormattingConflicts.EllipsisField ? "As traduções têm três pontos" : "As traduções estão certas";
        Text = $"{SettingConflictTexts.Refusal(strategyId, conflict)} {translations}; o ajuste é que troca.";
        ButtonLabel = SettingConflictTexts.ButtonLabel(conflict);
    }

    public FormattingConflict Conflict { get; }

    /// <summary>As linhas do projeto que o ajuste causa (uma por linha de fala); contam nos totais, mas a lista mostra só o card.</summary>
    public IReadOnlyList<ConferenceIssueRowViewModel> Rows { get; }

    public string Field => Conflict.Field;

    /// <summary>Quantas ocorrências o ajuste causa.</summary>
    public int Count => Rows.Count;

    public string Title { get; }

    public string Text { get; }

    public string ButtonLabel { get; }
}
