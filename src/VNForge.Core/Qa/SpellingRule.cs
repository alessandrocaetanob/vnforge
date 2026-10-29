using VNForge.Core.Glossary;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Palavra da tradução que o dicionário pt-BR não conhece (erro de digitação, acento esquecido). O dicionário do projeto já traz as traduções do Glossário e os
/// nomes dos personagens, então eles não disparam; o que sobrar e for de propósito, o revisor silencia com "Está certo assim". Só aviso.
/// </summary>
public sealed class SpellingRule : UnitQaRule
{
    /// <summary>Id da regra em <see cref="Text.TextUnit.Flags"/> e no filtro <c>--rules</c>.</summary>
    public const string RuleId = "ortografia";

    private readonly Func<SpellChecker> checker;
    private volatile ProjectWords? cached;

    /// <summary>A regra do <c>vnf</c>, com o dicionário pt-BR embutido (lido só na primeira conferência).</summary>
    public SpellingRule()
        : this(() => SpellChecker.Portuguese)
    {
    }

    /// <summary>A regra sobre outro corretor, para os testes.</summary>
    public SpellingRule(Func<SpellChecker> checker)
    {
        ArgumentNullException.ThrowIfNull(checker);
        this.checker = checker;
    }

    /// <inheritdoc />
    public override string Id => RuleId;

    /// <inheritdoc />
    public override string Description => "Palavra da tradução que o dicionário de português não reconhece.";

    /// <inheritdoc />
    public override QaSeverity Severity => QaSeverity.Warning;

    /// <summary>
    /// Se o dicionário não pôde ser lido, a regra se desliga nesta conferência: devolve um único aviso, na primeira linha, dizendo por quê, em vez de derrubar a
    /// conferência inteira. A próxima conferência tenta ler o dicionário de novo.
    /// </summary>
    public override IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            _ = checker();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Disabled(context, ex);
        }

        return base.Check(context);
    }

    private IEnumerable<QaIssue> Disabled(QaContext context, Exception error)
    {
        foreach (var (key, units) in context.Texts)
        {
            if (units.Count > 0)
            {
                return [new QaIssue(Id, Severity, units[0].Id, key, $"A conferência de ortografia ficou desligada: {error.Message}")];
            }
        }

        return [];
    }

    /// <inheritdoc />
    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (!unit.HasTranslation)
        {
            return [];
        }

        var project = ProjectOf(context);
        return checker().Find(unit.Translation, project).Select(span => $"\"{span.Text}\" não está no dicionário, na posição {span.Start}.");
    }

    // O glossário e os personagens são os mesmos objetos em todas as linhas de uma conferência (e entre elas, no Editor): o dicionário do projeto sai uma vez.
    private ProjectDictionary ProjectOf(QaContext context)
    {
        var current = cached;
        if (current is not null && ReferenceEquals(current.Glossary, context.Glossary) && ReferenceEquals(current.Characters, context.Characters)
            && ReferenceEquals(current.AddressTerms, context.AddressTerms))
        {
            return current.Words;
        }

        var words = ProjectDictionary.Create(context.Glossary, context.Characters, context.AddressTerms);
        cached = new ProjectWords(context.Glossary, context.Characters, context.AddressTerms, words);
        return words;
    }

    private sealed record ProjectWords(IReadOnlyList<GlossaryTerm> Glossary, IReadOnlyList<CharacterEntry> Characters, IReadOnlyList<AddressTerm> AddressTerms, ProjectDictionary Words);
}
