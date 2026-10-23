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

/// <summary>
/// O perfil de formatação (<c>formatting.json</c>): quais linhas o pipeline formata e cada regra, com liga/desliga e valores. Um perfil
/// sem um campo usa o valor de <see cref="Default"/>; as regras de E4.2, E4.3 e E4.5 entram aqui como novas propriedades com padrão.
/// </summary>
/// <param name="Kinds">Papéis de linha que o pipeline formata; as demais saem como estão.</param>
/// <param name="Wrap">Quebra de linha por palavra.</param>
public sealed record FormattingProfile(IReadOnlyList<TextUnitKind> Kinds, WrapRule Wrap)
{
    /// <summary>Todas as regras desligadas e só diálogo e narração no alvo: o build não muda nenhum texto.</summary>
    public static FormattingProfile Default { get; } = new([TextUnitKind.Dialogue, TextUnitKind.Narration], new WrapRule());
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
