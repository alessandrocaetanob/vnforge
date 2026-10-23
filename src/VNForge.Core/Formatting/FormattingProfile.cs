using VNForge.Core.Text;

namespace VNForge.Core.Formatting;

/// <summary>Tabela de largura por caractere que a quebra de linha usa.</summary>
public enum CharWidthMode
{
    /// <summary>Letra latina ocupa 1 coluna e japonês 2 (<see cref="CharWidths.HalfWidthLatin"/>).</summary>
    HalfWidthLatin,

    /// <summary>Todo caractere ocupa 1 coluna (<see cref="CharWidths.Uniform"/>).</summary>
    Uniform,
}

/// <summary>Quebra de linha por palavra (E4.4). Desligada por padrão, porque a largura da caixa depende do jogo.</summary>
/// <param name="Enabled">Liga a regra.</param>
/// <param name="MaxWidth">Largura máxima da linha, em colunas. Deve ser positiva.</param>
/// <param name="MaxLines">Máximo de linhas por caixa, ou <c>null</c> para não limitar. Quando informado, deve ser positivo.</param>
/// <param name="CharWidth">Tabela de largura por caractere.</param>
public sealed record WrapRule(
    bool Enabled = false,
    int MaxWidth = WrapRule.DefaultMaxWidth,
    int? MaxLines = WrapRule.DefaultMaxLines,
    CharWidthMode CharWidth = CharWidthMode.HalfWidthLatin)
{
    public const int DefaultMaxWidth = 54;

    public const int DefaultMaxLines = 3;

    /// <summary>As medidas no formato que o <see cref="WordWrapper"/> recebe.</summary>
    public WrapOptions ToOptions() =>
        new(MaxWidth, MaxLines, CharWidth == CharWidthMode.Uniform ? CharWidths.Uniform : CharWidths.HalfWidthLatin);
}

/// <summary>Normalização do texto (E4.2). Todas desligadas por padrão.</summary>
/// <param name="Nfc">Compõe letra e acento soltos (Unicode NFC).</param>
/// <param name="InvisibleSpaces">Remove espaços de largura zero (U+200B, U+200C, U+200D, U+2060, U+FEFF).</param>
/// <param name="Nbsp">Troca espaço sem quebra (U+00A0, U+202F) por espaço comum.</param>
public sealed record NormalizationRule(bool Nfc = false, bool InvisibleSpaces = false, bool Nbsp = false);

/// <summary>Espaços e pontuação (E4.2). Todas desligadas por padrão.</summary>
/// <param name="CollapseSpaces">Colapsa espaços duplos em um.</param>
/// <param name="SpaceBeforePunctuation">Tira o espaço antes de <c>? ! : ;</c>.</param>
/// <param name="SpaceAfterPunctuation">Insere espaço depois da pontuação colada à palavra seguinte.</param>
public sealed record SpacingRule(bool CollapseSpaces = false, bool SpaceBeforePunctuation = false, bool SpaceAfterPunctuation = false);

/// <summary>O que fazer com as aspas curvas <c>“ ” ‘ ’</c>.</summary>
public enum QuoteStyle
{
    /// <summary>Deixa como estão.</summary>
    None,

    /// <summary>Troca por aspas retas: <c>“ ”</c> por <c>"</c> e <c>‘ ’</c> por <c>'</c>.</summary>
    Ascii,

    /// <summary>Troca <c>“ ”</c> por <c>「 」</c>. As aspas simples ficam, porque <c>’</c> também é apóstrofo.</summary>
    Corner,
}

/// <summary>O que fazer com as reticências.</summary>
public enum EllipsisStyle
{
    /// <summary>Deixa como estão.</summary>
    None,

    /// <summary>Troca <c>...</c> pelo caractere <c>…</c>.</summary>
    Character,

    /// <summary>Troca o caractere <c>…</c> por <c>...</c>.</summary>
    Dots,
}

/// <summary>Direção da troca entre ASCII de meia largura e largura cheia.</summary>
public enum WidthMode
{
    /// <summary>Deixa como está.</summary>
    None,

    /// <summary>ASCII visível vira largura cheia (<c>Hoje</c> vira <c>Ｈｏｊｅ</c>).</summary>
    Full,

    /// <summary>Largura cheia vira ASCII (<c>Ｈｏｊｅ</c> vira <c>Hoje</c>).</summary>
    Half,
}

/// <summary>Aspas, reticências e travessão (E4.3). O padrão não muda nenhum texto.</summary>
/// <param name="Quotes">Estilo das aspas curvas.</param>
/// <param name="Ellipsis">Estilo das reticências.</param>
/// <param name="Dash">Caractere que substitui o travessão <c>—</c> e <c>–</c> em qualquer posição (por exemplo <c>―</c>, que existe em Shift-JIS), ou <c>null</c> para não trocar.</param>
/// <param name="DialogueDash">Caractere com que a fala que abre com travessão (<c>—</c>, <c>–</c>, <c>―</c> ou <c>-</c> seguido de espaço) deve sair, ou <c>null</c> para não trocar. Vence <paramref name="Dash"/> no começo da linha.</param>
public sealed record TypographyRule(
    QuoteStyle Quotes = QuoteStyle.None,
    EllipsisStyle Ellipsis = EllipsisStyle.None,
    char? Dash = null,
    char? DialogueDash = null);

/// <summary>Largura de ASCII (E4.3). <c>\</c>, <c>{</c> e <c>}</c> nunca mudam, para não criar nem desfazer marcador.</summary>
/// <param name="Mode">Direção da troca.</param>
/// <param name="Spaces">Troca também o espaço (<c>U+0020</c> e <c>U+3000</c>). Desligado, o espaço fica como está, para a quebra de linha continuar achando as palavras.</param>
public sealed record WidthRule(WidthMode Mode = WidthMode.None, bool Spaces = false);

/// <summary>
/// O perfil de formatação (<c>formatting.json</c>): quais linhas o pipeline formata e cada regra, com liga/desliga e valores. Um perfil
/// sem um campo usa o valor de <see cref="Default"/>; as regras de E4.3 e E4.5 entram aqui como novas propriedades com padrão.
/// </summary>
/// <param name="Kinds">Papéis de linha que o pipeline formata; as demais saem como estão.</param>
/// <param name="Wrap">Quebra de linha por palavra.</param>
public sealed record FormattingProfile(IReadOnlyList<TextUnitKind> Kinds, WrapRule Wrap)
{
    /// <summary>Aspas, reticências e travessão (E4.3). Tudo desligado por padrão.</summary>
    public TypographyRule Typography { get; init; } = new();

    /// <summary>Meia largura e largura cheia (E4.3). Desligada por padrão.</summary>
    public WidthRule Width { get; init; } = new();

    /// <summary>Todas as regras desligadas e só diálogo e narração no alvo: o build não muda nenhum texto.</summary>
    public static FormattingProfile Default { get; } = new([TextUnitKind.Dialogue, TextUnitKind.Narration], new WrapRule());

    /// <summary>Substituições do usuário, antes e depois das regras do perfil (E4.5). Padrão: nenhuma.</summary>
    public ReplacementRules Replacements { get; init; } = ReplacementRules.None;

    /// <summary>Unicode NFC, espaços invisíveis e NBSP (E4.2).</summary>
    public NormalizationRule Normalization { get; init; } = new();

    /// <summary>Espaços duplos e espaço em volta da pontuação (E4.2).</summary>
    public SpacingRule Spacing { get; init; } = new();

    /// <summary>Limite de bytes (já codificados) da tradução por papel de linha, para campos de tamanho fixo no jogo, como nomes e escolhas (E7.3). Papel ausente não tem limite.</summary>
    public IReadOnlyDictionary<TextUnitKind, int> ByteLimits { get; init; } = new Dictionary<TextUnitKind, int>();
}

/// <summary><c>formatting.json</c> inválido.</summary>
public sealed class FormattingProfileException : VNForgeException
{
    public FormattingProfileException(string field, string reason)
        : base($"formatting.json inválido em {field}: {reason}")
    {
        Field = field;
    }

    /// <summary>Caminho JSON do campo com problema (por exemplo <c>$.wrap.maxWidth</c>).</summary>
    public string Field { get; }
}
