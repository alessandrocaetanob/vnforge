using VNForge.Core.Projects;

namespace VNForge.Core.Bible;

/// <summary>Os campos de uma cena da bíblia. Cada um pode ser da IA ou da pessoa (<see cref="BibleScene.Edited"/>).</summary>
public enum BibleField
{
    Name,
    Summary,
    Characters,
    Tone,
    Jokes,
    Relations,
}

/// <summary>Como dois personagens se relacionam na cena ("Kenta e Ren": "Amigos de infância, se provocam o tempo todo").</summary>
public sealed record BibleRelation(string Between, string Note);

/// <summary>
/// O que a IA entendeu de uma cena (um script do jogo): nome, resumo, quem aparece, tom, piadas que voltam e relações. O resumo, o tom, as
/// piadas e as relações entram em cada pedido de tradução das linhas da cena. Um campo que a pessoa editou fica em <see cref="Edited"/> e a
/// geração seguinte não o sobrescreve.
/// </summary>
public sealed record BibleScene(string Key)
{
    /// <summary>Nome da cena ("Atraso no portão"); vazio quando a bíblia ainda não deu um.</summary>
    public string Name { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    /// <summary>Quem aparece na cena, com o nome que o roteiro usa para falar do personagem.</summary>
    public IReadOnlyList<string> Characters { get; init; } = [];

    /// <summary>O tom da cena, em poucas palavras cada ("cômico leve", "tenso no fim").</summary>
    public IReadOnlyList<string> Tone { get; init; } = [];

    /// <summary>As piadas e os motivos que voltam em outras cenas.</summary>
    public IReadOnlyList<string> Jokes { get; init; } = [];

    public IReadOnlyList<BibleRelation> Relations { get; init; } = [];

    /// <summary>Os campos que a pessoa escreveu; a IA não os troca.</summary>
    public IReadOnlyList<BibleField> Edited { get; init; } = [];

    /// <summary>A cena ainda não tem nenhum conteúdo escrito (nem pela IA, nem pela pessoa).</summary>
    public bool IsBlank =>
        Name.Length == 0 && Summary.Length == 0 && Characters.Count == 0 && Tone.Count == 0 && Jokes.Count == 0 && Relations.Count == 0;

    public bool IsEdited(BibleField field) => Edited.Contains(field);

    /// <summary>Marca (ou desmarca) <paramref name="field"/> como escrito pela pessoa.</summary>
    public BibleScene WithEdited(BibleField field, bool edited)
    {
        var rest = Edited.Where(f => f != field);
        return this with { Edited = [.. (edited ? rest.Append(field) : rest).Distinct().Order()] };
    }

    public BibleScene EditName(string name) => (this with { Name = Clean(name) }).WithEdited(BibleField.Name, true);

    public BibleScene EditSummary(string summary) => (this with { Summary = Clean(summary) }).WithEdited(BibleField.Summary, true);

    public BibleScene EditCharacters(IEnumerable<string> characters) => (this with { Characters = CleanList(characters) }).WithEdited(BibleField.Characters, true);

    public BibleScene EditTone(IEnumerable<string> tone) => (this with { Tone = CleanList(tone) }).WithEdited(BibleField.Tone, true);

    public BibleScene EditJokes(IEnumerable<string> jokes) => (this with { Jokes = CleanList(jokes) }).WithEdited(BibleField.Jokes, true);

    public BibleScene EditRelations(IEnumerable<BibleRelation> relations) =>
        (this with
        {
            Relations =
            [
                .. relations
                    .Select(r => new BibleRelation(Clean(r.Between), Clean(r.Note)))
                    .Where(r => r.Between.Length > 0 && r.Note.Length > 0),
            ],
        }).WithEdited(BibleField.Relations, true);

    /// <summary>
    /// Pega de <paramref name="generated"/> os campos que a pessoa não escreveu; os que ela escreveu ficam como estão. A chave e a lista dos
    /// campos editados não mudam.
    /// </summary>
    public BibleScene WithGenerated(BibleScene generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        return this with
        {
            Name = IsEdited(BibleField.Name) ? Name : generated.Name,
            Summary = IsEdited(BibleField.Summary) ? Summary : generated.Summary,
            Characters = IsEdited(BibleField.Characters) ? Characters : generated.Characters,
            Tone = IsEdited(BibleField.Tone) ? Tone : generated.Tone,
            Jokes = IsEdited(BibleField.Jokes) ? Jokes : generated.Jokes,
            Relations = IsEdited(BibleField.Relations) ? Relations : generated.Relations,
        };
    }

    /// <summary>Pega de <paramref name="generated"/> só <paramref name="field"/>, como "Reescrever com a IA" num campo; um campo da pessoa também é trocado, já que ela pediu.</summary>
    public BibleScene WithGeneratedField(BibleScene generated, BibleField field)
    {
        ArgumentNullException.ThrowIfNull(generated);
        var changed = field switch
        {
            BibleField.Name => this with { Name = generated.Name },
            BibleField.Summary => this with { Summary = generated.Summary },
            BibleField.Characters => this with { Characters = generated.Characters },
            BibleField.Tone => this with { Tone = generated.Tone },
            BibleField.Jokes => this with { Jokes = generated.Jokes },
            BibleField.Relations => this with { Relations = generated.Relations },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        return changed.WithEdited(field, false);
    }

    internal static string Clean(string? text) => (text ?? string.Empty).Trim();

    internal static IReadOnlyList<string> CleanList(IEnumerable<string> items) =>
        [.. items.Select(Clean).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
}

/// <summary>Um marco da linha do tempo do jogo (um capítulo ou uma virada): o título e uma frase.</summary>
public sealed record BibleTimelineEntry(string Title, string Note);

/// <summary>Como e quando a bíblia foi gerada, para o painel de custo.</summary>
/// <param name="ScenesRead">Cenas que a IA leu na última geração.</param>
/// <param name="Chapters">Capítulos lidos.</param>
/// <param name="Dollars">O que a geração custou, em dólar; vazio quando o custo não se sabe.</param>
public sealed record BibleGeneration(DateTimeOffset At, string Model, int ScenesRead, int Chapters, decimal Dollars);

/// <summary>
/// A bíblia do jogo (<c>bible.json</c>): a sinopse, a linha do tempo e, por cena, o que a IA entendeu do roteiro. Imutável: cada alteração
/// devolve uma cópia. O que a pessoa escreveu nunca é trocado por uma geração nova.
/// </summary>
public sealed record GameBible
{
    public static GameBible Empty { get; } = new();

    public string Synopsis { get; init; } = string.Empty;

    /// <summary>A pessoa escreveu a sinopse; a geração seguinte a mantém.</summary>
    public bool SynopsisEdited { get; init; }

    public IReadOnlyList<BibleTimelineEntry> Timeline { get; init; } = [];

    public bool TimelineEdited { get; init; }

    /// <summary>As cenas na ordem em que a história corre (<see cref="ScriptGrouping.InStoryOrder"/>).</summary>
    public IReadOnlyList<BibleScene> Scenes { get; init; } = [];

    public BibleGeneration? Generation { get; init; }

    public bool IsEmpty => Scenes.Count == 0 && Synopsis.Length == 0 && Timeline.Count == 0;

    public BibleScene? Scene(string key) => Scenes.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    /// <summary>O nome que a bíblia deu à cena, ou nulo quando ela não tem cena com esse nome.</summary>
    public string? SceneName(string key) => Scene(key) is { } scene && !string.IsNullOrWhiteSpace(scene.Name) ? scene.Name.Trim() : null;

    /// <summary>Os nomes de cena da bíblia, por chave de script, só das cenas que têm nome.</summary>
    public IReadOnlyDictionary<string, string> SceneNames() =>
        Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToDictionary(s => s.Key, s => s.Name.Trim(), StringComparer.Ordinal);

    /// <summary>Monta a bíblia com as cenas dadas, na ordem da história.</summary>
    public static GameBible From(IEnumerable<BibleScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        return Empty with { Scenes = InStoryOrder(scenes) };
    }

    /// <summary>Troca a cena de mesma chave, ou a acrescenta na ordem da história.</summary>
    public GameBible WithScene(BibleScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return this with { Scenes = InStoryOrder(Scenes.Where(s => !string.Equals(s.Key, scene.Key, StringComparison.Ordinal)).Append(scene)) };
    }

    /// <summary>Aplica o que a IA escreveu de uma cena: os campos da pessoa ficam, os outros são trocados; cena nova entra como veio.</summary>
    public GameBible WithGeneratedScene(BibleScene generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        return WithScene(Scene(generated.Key) is { } existing ? existing.WithGenerated(generated) : generated with { Edited = [] });
    }

    /// <summary>Aplica a sinopse e a linha do tempo da IA, sem trocar a que a pessoa escreveu.</summary>
    public GameBible WithGeneratedOverview(string synopsis, IEnumerable<BibleTimelineEntry> timeline)
    {
        ArgumentNullException.ThrowIfNull(synopsis);
        ArgumentNullException.ThrowIfNull(timeline);
        return this with
        {
            Synopsis = SynopsisEdited ? Synopsis : BibleScene.Clean(synopsis),
            Timeline = TimelineEdited ? Timeline : [.. timeline.Select(Clean).Where(t => t.Title.Length > 0)],
        };
    }

    public GameBible EditSynopsis(string synopsis) => this with { Synopsis = BibleScene.Clean(synopsis), SynopsisEdited = true };

    public GameBible EditTimeline(IEnumerable<BibleTimelineEntry> timeline) =>
        this with { Timeline = [.. timeline.Select(Clean).Where(t => t.Title.Length > 0)], TimelineEdited = true };

    private static BibleTimelineEntry Clean(BibleTimelineEntry entry) => new(BibleScene.Clean(entry.Title), BibleScene.Clean(entry.Note));

    private static IReadOnlyList<BibleScene> InStoryOrder(IEnumerable<BibleScene> scenes)
    {
        var byKey = scenes.ToDictionary(s => s.Key, StringComparer.Ordinal);
        return [.. ScriptGrouping.InStoryOrder(byKey.Keys).Select(k => byKey[k])];
    }
}
