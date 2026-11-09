using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>
/// Monta o <see cref="TranslationBatch"/> de um trecho do jogo: só entram no prompt os termos do glossário que aparecem nas linhas
/// do lote e os personagens que falam nele, para não gastar tokens (nem diluir a atenção do modelo) com o resto. Cada lote leva
/// também as falas antes e depois como contexto (duas de cada lado por padrão; o <c>vnforge.json</c> muda), o guia de estilo e as escolhas de estilo do projeto.
/// </summary>
public sealed class TranslationBatchBuilder(
    IReadOnlyList<GlossaryTerm> glossary,
    CharacterBook characters,
    string? styleGuide = null,
    int maxPreviousLines = TranslationBatchBuilder.DefaultContextLines,
    StyleConfig? style = null,
    int maxNextLines = TranslationBatchBuilder.DefaultContextLines,
    IReadOnlyList<AddressTerm>? addressTerms = null)
{
    /// <summary>Quantas falas antes e depois do lote entram como contexto.</summary>
    public const int DefaultContextLines = AiConfig.DefaultContextLines;

    /// <summary>Quantas falas anteriores já traduzidas entram como contexto, no máximo.</summary>
    public int MaxPreviousLines { get; } = Math.Max(0, maxPreviousLines);

    /// <summary>Quantas falas seguintes entram como contexto, no máximo.</summary>
    public int MaxNextLines { get; } = Math.Max(0, maxNextLines);

    private readonly IReadOnlyList<GlossaryTerm> _glossary = glossary ?? throw new ArgumentNullException(nameof(glossary));
    private readonly CharacterBook _characters = characters ?? throw new ArgumentNullException(nameof(characters));
    private readonly string _styleGuide = styleGuide?.Trim() ?? string.Empty;
    private readonly StyleConfig _style = style ?? StyleConfig.Default;
    private readonly AddressBook _addressing = new(addressTerms ?? []);
    /// <summary>A bíblia do jogo (#530): o resumo da cena de cada linha e a sinopse entram no pedido. Sem ela (ou nula) o lote é o de antes.</summary>
    public GameBible Bible { get; init => field = value ?? GameBible.Empty; } = GameBible.Empty;

    /// <summary>
    /// Monta o lote de <paramref name="lines"/>. <paramref name="previous"/> são as linhas anteriores do script; entram como contexto
    /// só as que já têm tradução, no máximo as últimas <c>maxPreviousLines</c>. <paramref name="next"/> são as linhas que vêm depois do lote;
    /// entram as primeiras <c>maxNextLines</c> com texto, no original, porque ainda não foram traduzidas.
    /// </summary>
    public TranslationBatch Build(IReadOnlyList<TextUnit> lines, IReadOnlyList<TextUnit>? previous = null, IReadOnlyList<TextUnit>? next = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var matchedGlossary = _glossary
            .Where(t => t.Source.Trim().Length > 0 && lines.Any(l => l.Source.Contains(t.Source.Trim(), StringComparison.Ordinal)))
            .Select(t => new GlossaryEntry(t.Source.Trim(), t.Target, t.Notes))
            .ToList();

        // "？？？" é um rótulo que serve a várias pessoas: nenhuma entrada de personagem descreve todas, e uma existente (de uma sugestão
        // antiga) daria gênero e jeito de falar errados a quem fala sob esse nome.
        var matchedCharacters = lines
            .Where(l => !SpeakerNames.IsSignsOnly(l.Speaker))
            .Select(l => _characters.Find(l.Speaker))
            .OfType<CharacterEntry>()
            .DistinctBy(c => c.Source, StringComparer.Ordinal)
            .Select(c => new CharacterInfo(c.Source, c.Target, c.Tone, c.Gender == CharacterGender.Unknown ? null : c.Gender.ToString().ToLowerInvariant()))
            .ToList();

        // Os pares incompletos (a pessoa ainda preenche a tabela) não dizem nada à IA.
        var addressing = lines
            .Where(l => !SpeakerNames.IsSignsOnly(l.EffectiveSpeaker))
            .Select(l => _characters.Find(l.EffectiveSpeaker))
            .OfType<CharacterEntry>()
            .DistinctBy(c => c.Source, StringComparer.Ordinal)
            .SelectMany(c => _addressing.From(c.Source))
            .Where(t => !string.IsNullOrWhiteSpace(t.Source) && !string.IsNullOrWhiteSpace(t.Target))
            .Select(t => new AddressInfo(NameInTranslation(t.From), NameInTranslation(t.To), t.Source.Trim(), t.Target.Trim()))
            .ToList();

        var context = (previous ?? [])
            .Where(u => u.HasTranslation)
            .TakeLast(MaxPreviousLines)
            .ToList();

        var following = (next ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u.Source))
            .Take(MaxNextLines)
            .ToList();

        // A chave do script é a parte do id antes dos dois-pontos ("yst00012:000478"). Cena sem nada escrito na bíblia não leva seção.
        var scenes = lines
            .Select(l => ScriptKeyOf(l.Id))
            .Distinct(StringComparer.Ordinal)
            .Select(Bible.Scene)
            .OfType<BibleScene>()
            .Where(s => s.Summary.Length > 0 || s.Tone.Count > 0 || s.Jokes.Count > 0 || s.Relations.Count > 0)
            .ToList();

        return new TranslationBatch(lines)
        {
            Scenes = scenes,
            Synopsis = Bible.Synopsis,
            Glossary = matchedGlossary,
            Characters = matchedCharacters,
            Addressing = addressing,
            PreviousLines = context,
            NextLines = following,
            Window = new ContextWindow(MaxPreviousLines, MaxNextLines),
            StyleGuide = _styleGuide,
            Style = _style,
        };
    }

    private static string ScriptKeyOf(string unitId) => unitId.Contains(':', StringComparison.Ordinal) ? unitId[..unitId.IndexOf(':', StringComparison.Ordinal)] : unitId;

    private string NameInTranslation(string name) =>
        _characters.Find(name) is { Target: { } target } && !string.IsNullOrWhiteSpace(target) ? target.Trim() : name.Trim();

    /// <summary>Lê o <c>style.md</c> do projeto; devolve vazio quando o arquivo não existe.</summary>
    public static string ReadStyleGuide(string path) =>
        File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
}
