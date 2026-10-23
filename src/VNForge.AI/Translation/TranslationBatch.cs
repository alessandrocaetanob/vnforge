using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>Um termo fixo do glossário: como o original deve ser traduzido.</summary>
public sealed record GlossaryEntry(string Source, string Target);

/// <summary>Um personagem presente no lote: nome original e traduzido, com o tom de fala quando definido.</summary>
public sealed record CharacterInfo(string Source, string Target, string? Tone = null);

/// <summary>Um lote de linhas a traduzir, com o contexto que o provedor usa para manter o estilo e os termos.</summary>
public sealed record TranslationBatch
{
    public TranslationBatch(IReadOnlyList<TextUnit> lines)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    /// <summary>Linhas a traduzir; só <see cref="TextUnit.Id"/>, <see cref="TextUnit.Kind"/>, <see cref="TextUnit.Speaker"/> e <see cref="TextUnit.Source"/> contam.</summary>
    public IReadOnlyList<TextUnit> Lines { get; }

    /// <summary>Linhas anteriores já traduzidas, só como contexto; não entram na resposta.</summary>
    public IReadOnlyList<TextUnit> PreviousLines { get; init; } = [];

    /// <summary>Termos do glossário filtrados para este lote.</summary>
    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = [];

    /// <summary>Personagens presentes no lote.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; init; } = [];

    /// <summary>Guia de estilo livre do projeto (<c>style.md</c>); vazio quando não há.</summary>
    public string StyleGuide { get; init; } = string.Empty;
}

/// <summary>A tradução de uma linha do lote.</summary>
/// <param name="Id">Id da <see cref="TextUnit"/> traduzida.</param>
/// <param name="Translation">Texto traduzido, com os mesmos marcadores de controle do original.</param>
public sealed record TranslationResult(string Id, string Translation);
