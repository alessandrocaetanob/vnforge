using System.Globalization;
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
        [UntranslatedQaRule.RuleId] = ("Não traduzida", "A linha ainda não tem tradução. O build copia o original nela, então não bloqueia a reconstrução dos scripts."),
        ["traducao-vazia"] = ("Tradução vazia", "A linha está marcada como traduzida, mas o texto está vazio. O build gravaria uma caixa de texto vazia no jogo."),
        ["marcador-diferente"] = ("Marcadores diferentes do original", @"Os marcadores de controle (\p, \c, {VAR:...}) precisam ser os mesmos do original para o script ser reconstruído. O QA não corrige sozinho porque o lugar do marcador pode mudar na tradução."),
        ["glossario-diferente"] = ("Termo diferente do glossário", "O glossário fixa a tradução deste termo. Se a fala pede outra escolha, marque como ok para registrar a exceção na linha."),
        ["personagem-diferente"] = ("Nome de personagem diferente", "O nome do personagem foi traduzido diferente do que está em characters.json. Marque como ok se a variação for deliberada."),
        ["traducao-inconsistente"] = ("Mesmo original, traduções diferentes", "O mesmo original foi traduzido de jeitos diferentes. Pode ser intencional (personagens diferentes); a lista completa abre na busca."),
        ["espaco-duplo"] = ("Espaço duplo", "Dois espaços seguidos viram uma coluna a mais na caixa e costumam ser sobra de edição."),
        ["espaco-antes-pontuacao"] = ("Espaço antes de pontuação", "Em português não há espaço antes de , ; : ? !. O texto salvo fica errado até ser corrigido."),
        ["aspas-desbalanceadas"] = ("Aspas ou parênteses sem par", "Falta fechar ou abrir um par de aspas ou parênteses. Confira se a fala termina onde o original termina."),
        [UnencodableCharacterRule.RuleId] = ("Caractere fora da codificação", "A estratégia de acentuação do projeto não grava este caractere no jogo. Troque por um que ela aceite."),
        ["japones-na-traducao"] = ("Japonês na tradução", "Há hiragana, katakana ou kanji na tradução. Costuma ser um trecho que ficou sem traduzir."),
        [BoxLineLimitRule.RuleId] = ("Não cabe na caixa", @"A tradução ocupa mais linhas que o máximo do perfil de formatação. Encurte a frase ou divida a fala em duas páginas com \p."),
        [FieldByteLimitRule.RuleId] = ("Passa do limite do campo", "O campo tem um limite de bytes no jogo e a tradução passa dele. Encurte o texto."),
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

    /// <param name="issue">A ocorrência; no resumo de um script, a primeira dele.</param>
    /// <param name="proposal">A correção proposta pelo <see cref="IQaFixer"/> da regra, quando há.</param>
    /// <param name="unit">A linha do projeto, para mostrar original e tradução no detalhe.</param>
    /// <param name="count">Quantas ocorrências a linha representa; maior que 1 no resumo por script e quando a regra aponta a mesma linha mais de uma vez.</param>
    /// <param name="ruleDescription">Descrição da regra no Core, usada quando o título não está na tabela.</param>
    public ConferenceIssueRowViewModel(QaIssue issue, QaFix? proposal = null, TextUnit? unit = null, int count = 1, string? ruleDescription = null)
    {
        Issue = issue;
        Proposal = proposal;
        Unit = unit;
        Count = count;
        this.ruleDescription = ruleDescription;
    }

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

    public SeverityLevel Level => IsError ? SeverityLevel.Error : SeverityLevel.Warning;

    public string SeverityLabel => IsError ? "Erro" : "Aviso";

    /// <summary>Só o erro bloqueia o build, e só o aviso de uma linha pode ser marcado como ok.</summary>
    public bool CanMarkOk => !IsError && !IsScriptSummary;

    public bool HasProposal => Proposal is not null;

    public string? ProposedText => Proposal?.After;

    /// <summary>Coluna "Linha" em mono: <c>yst00012:0042</c>; no resumo, só o script.</summary>
    public string LineLabel => IsScriptSummary ? ScriptKey : FormatLine(UnitId);

    public string Title => ConferenceRuleTexts.Title(RuleId, ruleDescription);

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

            if (Proposal is { } p)
            {
                return Excerpt(p.Before, p.After);
            }

            return Count > 1 ? $"{Message} (+{Count - 1})" : Message;
        }
    }

    /// <summary>"Corrigível" quando a regra propõe uma correção para a linha; vazio nas outras.</summary>
    public string Tag => HasProposal ? "Corrigível" : string.Empty;

    /// <summary>Texto do original no detalhe; vazio quando a linha não veio do projeto.</summary>
    public string Original => Unit?.Source ?? string.Empty;

    public bool HasOriginal => Original.Length > 0;

    /// <summary>Tradução gravada no detalhe; vazia no resumo por script e em linha sem tradução.</summary>
    public string Translation => IsScriptSummary ? string.Empty : Unit?.Translation ?? string.Empty;

    public bool HasTranslation => Translation.Length > 0;

    /// <summary>Título do detalhe: o da regra; no resumo, o script e a contagem.</summary>
    public string DetailTitle => IsScriptSummary ? $"{Title} em {ScriptKey}" : Title;

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

        return $"“{before[start..end]}”";
    }
}

/// <summary>
/// Uma linha da coluna "Regras": "Todas" ou uma categoria de <see cref="QaCategories"/>, com quantos problemas abertos tem e a pior gravidade
/// (ícone de erro, de aviso ou de "tudo certo").
/// </summary>
public sealed partial class ConferenceCategoryViewModel(string? category) : ObservableObject
{
    /// <summary>A categoria de <see cref="QaCategories"/>; nulo em "Todas".</summary>
    public string? Category { get; } = category;

    public string Label => Category ?? "Todas";

    public bool IsAll => Category is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(IsClear), nameof(HasErrors), nameof(HasWarnings))]
    public partial int Count { get; set; }

    /// <summary>Verdadeiro quando o grupo tem ao menos um erro aberto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClear), nameof(HasErrors), nameof(HasWarnings))]
    public partial bool ContainsError { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>"Tradução" e "Outras" só aparecem quando têm problemas; as sete do design sempre.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public string CountLabel => ProjectLabels.Number(Count);

    /// <summary>Categoria sem problema abertos: ícone de "tudo certo".</summary>
    public bool IsClear => !IsAll && Count == 0;

    public bool HasErrors => !IsAll && Count > 0 && ContainsError;

    public bool HasWarnings => !IsAll && Count > 0 && !ContainsError;
}
