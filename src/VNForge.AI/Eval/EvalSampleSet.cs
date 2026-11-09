using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Eval;

/// <summary>
/// As linhas sorteadas de uma rodada, em trechos seguidos, e a montagem dos lotes de cada configuração. Lê o projeto uma vez e nunca o grava. As linhas vão ao provedor sem a tradução
/// aprovada (só o original, o falante e o tipo), e o contexto é o que o <c>vnf translate</c> daria à mesma linha: as falas anteriores já traduzidas e as seguintes no original.
/// </summary>
internal sealed class EvalSampleSet
{
    private const string StyleGuideFileName = "style.md";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> _texts;
    private readonly Dictionary<string, (string Key, int Index)> _where = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<GlossaryTerm> _glossary;
    private readonly CharacterBook _characters;
    private readonly string _styleGuide;
    private readonly IReadOnlyList<AddressTerm> _addressTerms;
    private readonly StyleConfig _style;

    private EvalSampleSet(ProjectFolder project, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, IReadOnlyList<IReadOnlyList<GoldLine>> blocks, int goldLines)
    {
        _texts = texts;
        Sample = [.. blocks.SelectMany(b => b)];
        GoldLines = goldLines;
        RemainingLines = GoldSet.RemainingLines(texts);
        foreach (var (key, units) in texts)
        {
            for (var i = 0; i < units.Count; i++)
            {
                _where[units[i].Id] = (key, i);
            }
        }

        _glossary = project.LoadGlossary();
        _characters = new CharacterBook(project.LoadCharacters());
        _styleGuide = TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, StyleGuideFileName));
        _addressTerms = project.LoadAddressTerms();
        _style = project.LoadConfig().Style;
        Blocks = [.. blocks.Select(b => (IReadOnlyList<TextUnit>)[.. b.Select(g => Original(_texts[_where[g.Id].Key][_where[g.Id].Index]))])];
        Lines = [.. Blocks.SelectMany(b => b)];
    }

    public IReadOnlyList<GoldLine> Sample { get; }

    /// <summary>As linhas da amostra como o provedor as recebe: sem tradução.</summary>
    public IReadOnlyList<TextUnit> Lines { get; }

    /// <summary>
    /// Os lotes da amostra: cada um é um trecho de linhas aprovadas seguidas do mesmo script, como o <c>vnf translate</c> monta os seus. Cada linha tem assim a janela
    /// de contexto real: as do próprio lote entre si, e as falas de antes e de depois nas pontas.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<TextUnit>> Blocks { get; }

    public int GoldLines { get; }

    public int RemainingLines { get; }

    /// <exception cref="EvalException">O projeto não tem linha aprovada.</exception>
    /// <param name="blockSize">Linhas por lote; é o tamanho de lote da rodada.</param>
    public static EvalSampleSet Load(ProjectFolder project, int sampleSize, int seed, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (sampleSize < 1)
        {
            throw new EvalException("O tamanho da amostra precisa ser 1 ou maior.");
        }

        var texts = project.LoadAllText();
        var gold = GoldSet.From(texts);
        return gold.Count == 0
            ? throw new EvalException("O projeto ainda não tem linhas aprovadas: o banco de provas compara as traduções com as que você aprovou. Aprove algumas linhas no Editor e rode de novo.")
            : new EvalSampleSet(project, texts, GoldSet.SampleBlocks(gold, sampleSize, seed, blockSize), gold.Count);
    }

    public TranslationBatch BuildBatch(EvalConfig config, IReadOnlyList<TextUnit> lines)
    {
        var builder = new TranslationBatchBuilder(_glossary, _characters, _styleGuide, config.ContextLines, _style, config.ContextLines, _addressTerms);
        var (firstKey, firstIndex) = _where[lines[0].Id];
        var (lastKey, lastIndex) = _where[lines[^1].Id];
        var previous = _texts[firstKey].Take(firstIndex).ToList();
        var next = _texts[lastKey].Skip(lastIndex + 1).Take(config.ContextLines + 20).ToList();
        return builder.Build(lines, previous, next);
    }

    /// <summary>Um lote de lotes do juiz: o original com o texto a avaliar no lugar da tradução, com glossário e estilo do projeto.</summary>
    public TranslationBatch BuildJudgeBatch(IReadOnlyList<TextUnit> lines)
    {
        var builder = new TranslationBatchBuilder(_glossary, _characters, _styleGuide, 0, _style, 0, _addressTerms);
        return builder.Build(lines);
    }

    public IReadOnlyList<EvalSample> ToSamples() =>
        [.. Sample.Select(g => new EvalSample(g.Id, g.ScriptKey, g.Speaker, g.Source, g.Reference, g.Before, g.After))];

    private static TextUnit Original(TextUnit unit) => unit with { Translation = null, Status = TranslationStatus.Untranslated, Doubt = null, AiTranslation = null };
}
