namespace VNForge.App.ViewModels;

/// <summary>De onde o modo revisão tira a fila: as linhas da cena aberta ou as falas de um personagem em todas as cenas.</summary>
public enum ReviewSourceKind
{
    OpenScene,
    Speaker,
}

/// <summary>
/// A fonte da fila do modo revisão. Uma fonte nova acrescenta um <see cref="ReviewSourceKind"/> e o ramo dela em
/// <see cref="EditorViewModel.StartReviewOf(ReviewSource)"/>; a tela e as regras da sequência não mudam.
/// </summary>
public sealed record ReviewSource(ReviewSourceKind Kind, string? Speaker = null)
{
    /// <summary>As linhas da IA da cena aberta, da primeira à última.</summary>
    public static ReviewSource OpenScene { get; } = new(ReviewSourceKind.OpenScene);

    /// <summary>As linhas da IA de um personagem (o nome como o texto o guarda) em todas as cenas, na ordem da história.</summary>
    public static ReviewSource OfSpeaker(string speaker) => new(ReviewSourceKind.Speaker, speaker ?? throw new ArgumentNullException(nameof(speaker)));

    /// <summary>A fila passa por mais de uma cena: o modo revisão abre a cena de cada linha e não oferece "a próxima cena".</summary>
    public bool SpansScenes => Kind == ReviewSourceKind.Speaker;
}

/// <summary>Uma linha da fila do modo revisão, pela cena e pela posição nela; a linha da grade é achada ao exibi-la, porque trocar de cena refaz a grade.</summary>
public sealed record ReviewItem(string ScriptKey, int Number, string Id);

/// <summary>Um personagem com linhas da IA esperando revisão: o nome como o texto o guarda, o nome para mostrar e quantas linhas.</summary>
public sealed record ReviewableSpeaker(string Source, string Name, int Count);
