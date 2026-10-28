using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>Um termo fixo do glossário: como o original deve ser traduzido.</summary>
public sealed record GlossaryEntry(string Source, string Target, string? Notes = null);

/// <summary>Um personagem presente no lote: nome original e traduzido, com o tom de fala quando definido.</summary>
public sealed record CharacterInfo(string Source, string Target, string? Tone = null, string? Gender = null);

/// <summary>Pedido de uma versão mais curta de uma tradução que não cabe na caixa do jogo.</summary>
/// <param name="Current">A tradução de hoje, que passa do limite.</param>
/// <param name="MaxLines">Máximo de linhas por caixa do perfil de formatação.</param>
/// <param name="MaxWidth">Largura máxima de cada linha, em colunas.</param>
public sealed record ShortenRequest(string Current, int MaxLines, int MaxWidth);

/// <summary>Um lote de linhas a traduzir, com o contexto que o provedor usa para manter o estilo e os termos.</summary>
public sealed record TranslationBatch
{
    public TranslationBatch(IReadOnlyList<TextUnit> lines)
    {
        Lines = [.. lines ?? throw new ArgumentNullException(nameof(lines))];
    }

    /// <summary>Linhas a traduzir; só <see cref="TextUnit.Id"/>, <see cref="TextUnit.Kind"/>, <see cref="TextUnit.Speaker"/> e <see cref="TextUnit.Source"/> contam.</summary>
    public IReadOnlyList<TextUnit> Lines { get; }

    /// <summary>Linhas anteriores já traduzidas, só como contexto; não entram na resposta.</summary>
    public IReadOnlyList<TextUnit> PreviousLines { get; init; } = [];

    /// <summary>Falas seguintes às do lote, só como contexto (o original, ainda sem tradução); não entram na resposta.</summary>
    public IReadOnlyList<TextUnit> NextLines { get; init; } = [];

    /// <summary>Termos do glossário filtrados para este lote.</summary>
    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = [];

    /// <summary>Personagens presentes no lote.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; init; } = [];

    /// <summary>Guia de estilo livre do projeto (<c>style.md</c>); vazio quando não há.</summary>
    public string StyleGuide { get; init; } = string.Empty;

    /// <summary>Linhas (pelo id) para as quais se pede uma versão mais curta da tradução de hoje em vez de uma tradução nova; vazio num lote comum.</summary>
    public IReadOnlyDictionary<string, ShortenRequest> Shorten { get; init; } = new Dictionary<string, ShortenRequest>();

    /// <summary>Escolhas de estilo do projeto (honoríficos, tratamento, narrador, onomatopeias, ordem dos nomes).</summary>
    public StyleConfig Style { get; init; } = StyleConfig.Default;

    /// <summary>Um lote de <paramref name="lines"/> com o mesmo contexto deste.</summary>
    public TranslationBatch ForLines(IReadOnlyList<TextUnit> lines) => new(lines)
    {
        PreviousLines = PreviousLines,
        NextLines = NextLines,
        Glossary = Glossary,
        Characters = Characters,
        StyleGuide = StyleGuide,
        Style = Style,
        Shorten = Shorten,
    };
}

/// <summary>A tradução de uma linha do lote.</summary>
/// <param name="Id">Id da <see cref="TextUnit"/> traduzida.</param>
/// <param name="Translation">Texto traduzido, com os mesmos marcadores de controle do original.</param>
/// <param name="Refused">O modelo recusou esta linha mesmo após o fallback; <paramref name="Translation"/> fica vazia e a linha precisa de revisão.</param>
/// <param name="Doubt">A dúvida que o modelo registrou sobre a linha; nula quando ele não teve dúvida ou o provedor não a pede.</param>
public sealed record TranslationResult(string Id, string Translation, bool Refused = false, TranslationDoubt? Doubt = null);
