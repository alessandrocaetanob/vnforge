namespace VNForge.App.ViewModels;

/// <summary>De onde o modo revisão tira a fila: as linhas da cena aberta ou as falas de um personagem em todas as cenas ou as linhas com dúvida da IA da cena aberta.</summary>
public enum ReviewSourceKind
{
    OpenScene,
    Speaker,
    Doubts,
}

/// <summary>
/// A fonte da fila do modo revisão. Uma fonte nova acrescenta um <see cref="ReviewSourceKind"/>, o ramo dela em
/// <see cref="EditorViewModel.StartReviewOf(ReviewSource)"/> e os textos abaixo; a tela e as regras da sequência não mudam.
/// </summary>
public sealed record ReviewSource
{
    public ReviewSource(ReviewSourceKind kind, string? speaker = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"Fonte de revisão desconhecida: {kind}.", nameof(kind));
        }

        if (kind == ReviewSourceKind.Speaker && string.IsNullOrWhiteSpace(speaker))
        {
            throw new ArgumentException("A fonte de um personagem precisa do nome dele.", nameof(speaker));
        }

        Kind = kind;
        Speaker = speaker;
    }

    public ReviewSourceKind Kind { get; }

    /// <summary>O nome do personagem como o texto o guarda; só na fonte <see cref="ReviewSourceKind.Speaker"/>.</summary>
    public string? Speaker { get; }

    /// <summary>As linhas da IA da cena aberta, da primeira à última.</summary>
    public static ReviewSource OpenScene { get; } = new(ReviewSourceKind.OpenScene);

    /// <summary>As linhas da IA de um personagem (o nome como o texto o guarda) em todas as cenas, na ordem da história.</summary>
    public static ReviewSource OfSpeaker(string speaker) => new(ReviewSourceKind.Speaker, speaker);

    /// <summary>As linhas com dúvida da IA da cena aberta, na ordem do script, tenham ou não sido aprovadas.</summary>
    public static ReviewSource Doubts { get; } = new(ReviewSourceKind.Doubts);

    /// <summary>A tela final oferece "a próxima cena": só na fonte da cena aberta, porque a próxima cena seguiria a regra das linhas da IA.</summary>
    public bool OffersNextScene => Kind == ReviewSourceKind.OpenScene;

    /// <summary>A fila passa por mais de uma cena: o modo revisão abre a cena de cada linha e não oferece "a próxima cena".</summary>
    public bool SpansScenes => Kind == ReviewSourceKind.Speaker;

    /// <summary>A legenda no topo da tela, ou nulo quando a fonte não precisa de uma. <paramref name="name"/> é o nome para mostrar do personagem.</summary>
    public string? Caption(string name) => Kind switch
    {
        ReviewSourceKind.OpenScene or ReviewSourceKind.Doubts => null,
        ReviewSourceKind.Speaker => $"Falas de {name} em todas as cenas",
        _ => throw new InvalidOperationException($"Fonte de revisão desconhecida: {Kind}."),
    };

    /// <summary>O título da tela final de uma fila com linhas.</summary>
    public string FinishedTitle(string name) => Kind switch
    {
        ReviewSourceKind.OpenScene or ReviewSourceKind.Doubts => "Revisão da cena concluída",
        ReviewSourceKind.Speaker => $"Revisão das falas de {name} concluída",
        _ => throw new InvalidOperationException($"Fonte de revisão desconhecida: {Kind}."),
    };

    /// <summary>O texto da tela final quando a fila está vazia.</summary>
    public string EmptyText(string name) => Kind switch
    {
        ReviewSourceKind.OpenScene => "Esta cena não tem linhas traduzidas pela IA esperando revisão.",
        ReviewSourceKind.Speaker => $"{name} não tem falas traduzidas pela IA esperando revisão.",
        ReviewSourceKind.Doubts => "Esta cena não tem linhas com dúvida da IA.",
        _ => throw new InvalidOperationException($"Fonte de revisão desconhecida: {Kind}."),
    };
}

/// <summary>Uma linha da fila do modo revisão, pela cena e pela posição nela; a linha da grade é achada ao exibi-la, porque trocar de cena refaz a grade.</summary>
public sealed record ReviewItem(string ScriptKey, int Number, string Id);

/// <summary>Um personagem com linhas da IA esperando revisão: o nome como o texto o guarda, o nome para mostrar e quantas linhas.</summary>
public sealed record ReviewableSpeaker(string Source, string Name, int Count);
