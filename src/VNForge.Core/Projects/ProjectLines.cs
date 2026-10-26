using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Progresso de um script (<c>text/&lt;Key&gt;.json</c>) para a lista de scripts do Editor.</summary>
public sealed record ScriptProgress(string Key, StatusCounts Counts)
{
    /// <summary>Percentual concluído (Revisadas e Aprovadas sobre o total), de 0 a 100, truncado; 0 quando o script não tem linhas.</summary>
    public int CompletedPercent => Percent(Counts.Reviewed + Counts.Approved);

    /// <summary>Percentual de linhas só com tradução da IA, de 0 a 100, truncado; 0 quando o script não tem linhas.</summary>
    public int MachinePercent => Percent(Counts.Machine);

    private int Percent(int lines) => Counts.Total == 0 ? 0 : (int)(100L * lines / Counts.Total);
}

/// <summary>Uma linha da grade do Editor: a unidade, sua posição no script e os problemas do QA que a atingem.</summary>
/// <param name="ScriptKey">Chave do script a que a linha pertence.</param>
/// <param name="Number">Posição da linha no script, começando em 1.</param>
/// <param name="Unit">A unidade de <c>text/*.json</c>.</param>
/// <param name="Issues">Ocorrências ativas do QA nesta linha; vazio quando não há.</param>
public sealed record LineEntry(string ScriptKey, int Number, TextUnit Unit, IReadOnlyList<QaIssue> Issues)
{
    /// <summary>Maior gravidade entre <see cref="Issues"/> (erro acima de aviso); nulo quando não há ocorrência.</summary>
    public QaSeverity? Severity => Issues.Count == 0 ? null : Issues.Max(i => i.Severity);

    public bool HasIssues => Issues.Count > 0;

    /// <summary>
    /// A mesma linha com <paramref name="unit"/> no lugar da unidade, depois de uma edição. Refaz as ocorrências das regras de completude
    /// (<see cref="CompletenessQaRules"/>), que olham a linha isolada; as demais, que dependem do glossário e do resto do projeto, ficam como estavam
    /// e voltam a valer na próxima leitura. Com <paramref name="context"/> (o encoder e o perfil do projeto, <see cref="ProjectLines.LineContext"/>)
    /// refaz também as regras que só olham a linha e dependem deles: caractere não codificável, caixa acima do limite e tamanho em bytes.
    /// </summary>
    public LineEntry WithUnit(TextUnit unit, QaContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var engine = context is null ? CompletenessEngine : LineEngine;
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>> { [ScriptKey] = [unit] };
        var lineContext = (context ?? QaContext.ForTexts(texts)) with { Texts = texts };
        var fresh = engine.Run(lineContext).Issues;
        var kept = Issues.Where(i => !engine.Rules.Any(r => string.Equals(r.Id, i.RuleId, StringComparison.Ordinal)));
        return this with { Unit = unit, Issues = [.. kept, .. fresh] };
    }

    private static QaEngine CompletenessEngine { get; } = new(CompletenessQaRules.All);

    /// <summary>As regras que olham só a linha: a completude e as que dependem do encoder e do perfil do projeto.</summary>
    private static QaEngine LineEngine { get; } = new([.. CompletenessQaRules.All, new UnencodableCharacterRule(), new BoxLineLimitRule(), new FieldByteLimitRule()]);
}

/// <summary>Filtro de status da grade.</summary>
public enum LineStatusFilter
{
    /// <summary>Todas as linhas.</summary>
    All,

    /// <summary>Linhas ainda sem tradução (<see cref="TranslationStatus.Untranslated"/>).</summary>
    Pending,

    /// <summary>Linhas traduzidas só pela IA (<see cref="TranslationStatus.Machine"/>).</summary>
    Machine,

    /// <summary>Linhas com ocorrência do QA.</summary>
    Warnings,
}

/// <summary>Filtro da grade: um status e, opcionalmente, um personagem. Os dois valem juntos.</summary>
/// <param name="Status">Filtro de status.</param>
/// <param name="Speaker">Nulo aceita qualquer personagem; <see cref="Narration"/> aceita só linhas sem falante; outro valor é comparado em ordem ordinal.</param>
public sealed record LineFilter(LineStatusFilter Status = LineStatusFilter.All, string? Speaker = null)
{
    /// <summary>Valor de <see cref="Speaker"/> para as linhas sem falante (narração).</summary>
    public const string Narration = "";

    /// <summary>Filtro que aceita tudo.</summary>
    public static LineFilter None { get; } = new();

    private bool IsNone => Status == LineStatusFilter.All && Speaker is null;

    public bool Matches(LineEntry line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return MatchesStatus(line) && MatchesSpeaker(line);
    }

    /// <summary>Linhas que passam no filtro, na ordem original; devolve <paramref name="lines"/> mesmo quando o filtro aceita tudo.</summary>
    public IReadOnlyList<LineEntry> Apply(IReadOnlyList<LineEntry> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (IsNone)
        {
            return lines;
        }

        var result = new List<LineEntry>();
        foreach (var line in lines)
        {
            if (Matches(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    public int Count(IReadOnlyList<LineEntry> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (IsNone)
        {
            return lines.Count;
        }

        var count = 0;
        foreach (var line in lines)
        {
            if (Matches(line))
            {
                count++;
            }
        }

        return count;
    }

    private bool MatchesStatus(LineEntry line) => Status switch
    {
        LineStatusFilter.Pending => line.Unit.Status == TranslationStatus.Untranslated,
        LineStatusFilter.Machine => line.Unit.Status == TranslationStatus.Machine,
        LineStatusFilter.Warnings => line.HasIssues,
        _ => true,
    };

    private bool MatchesSpeaker(LineEntry line) => Speaker switch
    {
        null => true,
        Narration => string.IsNullOrEmpty(line.Unit.Speaker),
        _ => string.Equals(line.Unit.Speaker, Speaker, StringComparison.Ordinal),
    };
}

/// <summary>
/// As linhas do projeto prontas para a grade do Editor: por script, com a posição e os problemas do QA de cada uma.
/// Só leitura; a edição e o filtro ficam em quem apresenta.
/// </summary>
public sealed class ProjectLines
{
    private static readonly LineEntry[] NoLines = [];

    private readonly Dictionary<string, LineEntry[]> byScript;
    private Dictionary<string, List<(string Key, int Index)>>? bySource;

    private ProjectLines(List<ScriptProgress> scripts, Dictionary<string, LineEntry[]> byScript, QaContext? lineContext)
    {
        Scripts = scripts;
        this.byScript = byScript;
        LineContext = lineContext;
    }

    /// <summary>
    /// O encoder e o perfil de formatação do projeto, sem os textos: o que <see cref="LineEntry.WithUnit"/> precisa para refazer as regras de uma linha
    /// depois da edição. Nulo quando as linhas não vieram de um projeto (<see cref="Create"/>).
    /// </summary>
    public QaContext? LineContext { get; }

    /// <summary>A <paramref name="entry"/> com a <paramref name="unit"/> editada e as ocorrências da linha refeitas com o <see cref="LineContext"/>.</summary>
    public LineEntry WithUnit(LineEntry entry, TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.WithUnit(unit, LineContext);
    }

    /// <summary>Scripts em ordem ordinal da chave.</summary>
    public IReadOnlyList<ScriptProgress> Scripts { get; }

    /// <summary>Linhas do script na ordem do arquivo; vazio para uma chave desconhecida.</summary>
    public IReadOnlyList<LineEntry> LinesOf(string key) => byScript.TryGetValue(key, out var lines) ? lines : NoLines;

    /// <summary>
    /// As outras linhas do projeto com o mesmo original (comparação exata, entre todos os scripts), na ordem dos scripts e das linhas; a própria
    /// <paramref name="entry"/> não entra. Original vazio não agrupa. O índice é por <see cref="SourceHash"/> e é montado na primeira chamada.
    /// </summary>
    public IReadOnlyList<LineEntry> SameSourceAs(LineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Unit.Source.Length == 0 || !SourceIndex().TryGetValue(SourceHash.Of(entry.Unit.Source), out var positions))
        {
            return NoLines;
        }

        return [.. positions
            .Select(p => byScript[p.Key][p.Index])
            .Where(l => !(l.ScriptKey == entry.ScriptKey && l.Number == entry.Number) && string.Equals(l.Unit.Source, entry.Unit.Source, StringComparison.Ordinal))];
    }

    private Dictionary<string, List<(string Key, int Index)>> SourceIndex()
    {
        if (bySource is not null)
        {
            return bySource;
        }

        var index = new Dictionary<string, List<(string Key, int Index)>>(StringComparer.Ordinal);
        foreach (var key in Scripts.Select(script => script.Key))
        {
            var withSource = byScript[key].Select((line, i) => (line, i)).Where(x => x.line.Unit.Source.Length > 0);
            foreach (var (line, i) in withSource)
            {
                var hash = SourceHash.Of(line.Unit.Source);
                if (!index.TryGetValue(hash, out var list))
                {
                    index[hash] = list = [];
                }

                list.Add((key, i));
            }
        }

        return bySource = index;
    }

    /// <summary>
    /// Troca a linha <see cref="LineEntry.Number"/> do script pela versão editada, para que <see cref="LinesOf"/> devolva a edição
    /// quando o Editor reconstruir a grade. A entrada tem de ser da mesma posição e do mesmo script.
    /// </summary>
    public void Replace(LineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (byScript.TryGetValue(entry.ScriptKey, out var lines) && entry.Number >= 1 && entry.Number <= lines.Length)
        {
            lines[entry.Number - 1] = entry;
        }
    }

    /// <summary>Lê os textos do projeto e roda o QA (<paramref name="qa"/>, ou <see cref="QaEngine.Default"/>) para marcar as linhas.</summary>
    /// <exception cref="VNForgeException">Arquivo do projeto inválido ou estratégia de codificação desconhecida.</exception>
    public static ProjectLines Read(ProjectFolder project, QaEngine? qa = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var context = QaContext.FromProject(project);
        var lineContext = new QaContext(new Dictionary<string, IReadOnlyList<TextUnit>>(), [], [], context.Formatting, context.Encoder);
        return Create(context.Texts, (qa ?? QaEngine.Default).Run(context), lineContext);
    }

    /// <summary>Monta as linhas de <paramref name="texts"/>; as ocorrências de <paramref name="report"/> casam por chave do script e id da linha.</summary>
    /// <param name="lineContext">O encoder e o perfil do projeto, para refazer as regras da linha depois de uma edição (<see cref="LineContext"/>).</param>
    public static ProjectLines Create(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, QaReport? report = null, QaContext? lineContext = null)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var issues = new Dictionary<(string Script, string Unit), List<QaIssue>>();
        foreach (var issue in report?.Issues ?? [])
        {
            if (!issues.TryGetValue((issue.ScriptKey, issue.UnitId), out var list))
            {
                issues[(issue.ScriptKey, issue.UnitId)] = list = [];
            }

            list.Add(issue);
        }

        var scripts = new List<ScriptProgress>(texts.Count);
        var byScript = new Dictionary<string, LineEntry[]>(texts.Count, StringComparer.Ordinal);
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var lines = new LineEntry[units.Count];
            for (var i = 0; i < lines.Length; i++)
            {
                var unit = units[i];
                lines[i] = new LineEntry(key, i + 1, unit, issues.TryGetValue((key, unit.Id), out var found) ? found : []);
            }

            byScript[key] = lines;
            scripts.Add(new ScriptProgress(key, StatusCounts.Of(units)));
        }

        return new ProjectLines(scripts, byScript, lineContext);
    }

    /// <summary>Falantes distintos e não vazios, o que mais fala primeiro; empates em ordem ordinal.</summary>
    public static IReadOnlyList<string> SpeakersOf(IEnumerable<LineEntry> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .Select(l => l.Unit.Speaker)
            .OfType<string>()
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
    }
}
