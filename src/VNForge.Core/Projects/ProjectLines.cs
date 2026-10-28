using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Progresso de um script (<c>text/&lt;Key&gt;.json</c>) para a lista de scripts do Editor.</summary>
public sealed record ScriptProgress(string Key, StatusCounts Counts)
{
    /// <summary>O script não tem linha nenhuma: não há o que traduzir nem revisar, então conta como pronto.</summary>
    public bool IsEmpty => Counts.Total == 0;

    /// <summary>Percentual concluído (Revisadas, Aprovadas e "Não traduzir" sobre o total), de 0 a 100, truncado; 100 quando o script não tem linhas (<see cref="IsEmpty"/>).</summary>
    public int CompletedPercent => IsEmpty ? 100 : Percent(Counts.Ready);

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
    /// (<see cref="CompletenessQaRules"/>), que olham a linha isolada; as que olham o projeto inteiro (<c>traducao-inconsistente</c>,
    /// <c>personagem-sem-genero</c>) ficam como estavam e voltam a valer em <see cref="ProjectLines.BeginRecheck"/>. Com <paramref name="context"/>
    /// (glossário, personagens, estilo, perfil e encoder do projeto, <see cref="ProjectLines.LineContext"/>) refaz todas as regras que olham uma
    /// linha de cada vez (<see cref="UnitQaRule"/>): nome de personagem e termo do glossário, honorífico, caractere não codificável, caixa, bytes e texto.
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

    /// <summary>Todas as regras que olham uma linha de cada vez (<see cref="UnitQaRule"/>): a completude e as que dependem do contexto do projeto.</summary>
    internal static QaEngine LineEngine { get; } = new(QaEngine.Default.Rules.OfType<UnitQaRule>());
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

    /// <summary>Linhas "Não traduzir" (<see cref="TranslationStatus.Keep"/>).</summary>
    Keep,

    /// <summary>Linhas em que a IA registrou uma dúvida (<see cref="TextUnit.Doubt"/>).</summary>
    Doubt,

    /// <summary>Linhas cuja tradução mudou desde o último teste no jogo; vale junto com <see cref="LineFilter.ChangedLines"/>, que diz quais são.</summary>
    ChangedSinceTest,
}

/// <summary>Filtro da grade: um status, opcionalmente um personagem e opcionalmente um texto de busca. Os três valem juntos.</summary>
/// <param name="Status">Filtro de status.</param>
/// <param name="Speaker">Nulo aceita qualquer personagem; <see cref="Narration"/> aceita só linhas sem falante; outro valor é comparado em ordem ordinal.</param>
/// <param name="Query">
/// Nulo ou em branco aceita tudo. Um id completo (<c>yst00112:033792</c>, <see cref="LineIds.TryParse"/>) aceita só essa linha; a chave de um script
/// (<c>yst00112</c>) aceita a cena inteira; qualquer outro texto procura no original e na tradução, sem diferenciar maiúsculas nem acentos.
/// </param>
public sealed record LineFilter(LineStatusFilter Status = LineStatusFilter.All, string? Speaker = null, string? Query = null)
{
    /// <summary>Valor de <see cref="Speaker"/> para as linhas sem falante (narração).</summary>
    public const string Narration = "";

    /// <summary>Filtro que aceita tudo.</summary>
    public static LineFilter None { get; } = new();

    /// <summary>As linhas (chave do script e id) que <see cref="LineStatusFilter.ChangedSinceTest"/> aceita; sem ela esse filtro não aceita nada.</summary>
    public IReadOnlySet<(string ScriptKey, string UnitId)>? ChangedLines { get; init; }

    private bool IsNone => Status == LineStatusFilter.All && Speaker is null && string.IsNullOrWhiteSpace(Query);

    // Fora do record para não entrar na igualdade nem numa corrida entre threads: a chave é a instância, e um "with" ganha a sua.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LineFilter, ParsedQuery> ParsedQueries = [];

    public bool Matches(LineEntry line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return MatchesStatus(line) && MatchesSpeaker(line) && MatchesQuery(line);
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
        LineStatusFilter.Keep => line.Unit.Status == TranslationStatus.Keep,
        LineStatusFilter.Doubt => line.Unit.Doubt is not null,
        LineStatusFilter.ChangedSinceTest => ChangedLines?.Contains((line.ScriptKey, line.Unit.Id)) == true,
        _ => true,
    };

    private bool MatchesQuery(LineEntry line)
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return true;
        }

        return ParsedQueries.GetValue(this, static filter => ParsedQuery.Parse(filter.Query!)).Matches(line);
    }

    private sealed class ParsedQuery
    {
        private readonly string text;
        private readonly string folded;
        private readonly string trimmed;
        private readonly string? unitId;

        private ParsedQuery(string text, string trimmed, string? unitId)
        {
            this.text = text;
            this.trimmed = trimmed;
            this.unitId = unitId;
            folded = SearchText.Fold(text);
        }

        public static ParsedQuery Parse(string query)
        {
            var trimmed = query.Trim();
            return new ParsedQuery(query, trimmed, LineIds.TryParse(trimmed, out var id) ? id.UnitId : null);
        }

        public bool Matches(LineEntry line)
        {
            // O id vale como id e também como texto: "Cap:1" pode ser um trecho da fala, e um id que ninguém tem não deve calar a busca.
            if ((unitId is not null && string.Equals(line.Unit.Id, unitId, StringComparison.OrdinalIgnoreCase))
                || string.Equals(line.ScriptKey, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Só a tradução em português perde o acento na busca; no original em japonês a dobra juntaria kana com e sem dakuten.
            return line.Unit.Source.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (line.Unit.Translation is { } translation && SearchText.Fold(translation).Contains(folded, StringComparison.Ordinal));
        }
    }

    private bool MatchesSpeaker(LineEntry line) => Speaker switch
    {
        null => true,
        Narration => string.IsNullOrEmpty(line.Unit.EffectiveSpeaker),
        _ => string.Equals(line.Unit.EffectiveSpeaker, Speaker, StringComparison.Ordinal),
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

    private readonly QaContext? fixedContext;
    private readonly ProjectLineContext? contextSource;
    private readonly QaEngine? projectEngine;

    private ProjectLines(List<ScriptProgress> scripts, Dictionary<string, LineEntry[]> byScript, QaContext? lineContext, ProjectLineContext? contextSource, QaEngine? projectEngine)
    {
        Scripts = scripts;
        this.byScript = byScript;
        fixedContext = lineContext;
        this.contextSource = contextSource;
        this.projectEngine = projectEngine;
    }

    /// <summary>
    /// O glossário, os personagens, o estilo, o perfil de formatação e o encoder do projeto, sem os textos: o que <see cref="LineEntry.WithUnit"/>
    /// precisa para refazer as regras de uma linha depois da edição. Num projeto lido por <see cref="Read"/> acompanha os arquivos (cada um só é
    /// lido de novo quando muda); nulo quando as linhas não vieram de um projeto (<see cref="Create"/> sem contexto).
    /// </summary>
    public QaContext? LineContext => contextSource?.Current() ?? fixedContext;

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
        var stamps = ProjectLineContext.StampsOf(project);
        var context = QaContext.FromProject(project);
        var engine = qa ?? QaEngine.Default;
        return Build(context.Texts, engine.Run(context), null, new ProjectLineContext(project, context, stamps), engine);
    }

    /// <summary>
    /// Pede um instantâneo das linhas para refazer o QA fora da thread da tela com o glossário, os personagens e o estilo de agora:
    /// <see cref="LineRecheck.Run"/> roda longe da tela e <see cref="Apply"/> leva o resultado de volta. Sem as regras do projeto só as regras
    /// que olham uma linha de cada vez rodam; com elas rodam também as que olham o projeto inteiro (<c>traducao-inconsistente</c>,
    /// <c>personagem-sem-genero</c>). Chame na thread que grava as linhas; linhas que não vieram de um projeto não têm contexto e não mudam.
    /// </summary>
    public LineRecheck BeginRecheck(bool includeProjectRules)
    {
        if (LineContext is not { } context)
        {
            return new LineRecheck(null, null, []);
        }

        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>(byScript.Count, StringComparer.Ordinal);
        var entries = new List<LineEntry>();
        foreach (var key in Scripts.Select(script => script.Key))
        {
            var lines = byScript[key];
            texts[key] = [.. lines.Select(l => l.Unit)];
            entries.AddRange(lines);
        }

        var engine = includeProjectRules ? projectEngine ?? QaEngine.Default : LineEntry.LineEngine;
        return new LineRecheck(engine, context with { Texts = texts }, entries);
    }

    /// <summary>
    /// Leva o resultado de <see cref="LineRecheck.Run"/> para as linhas e devolve as que mudaram. A linha gravada depois do instantâneo
    /// fica de fora: as ocorrências dela já foram refeitas na gravação, e a próxima releitura confere o resto.
    /// </summary>
    public IReadOnlyList<LineEntry> Apply(RecheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var applied = new List<LineEntry>(result.Changes.Count);
        foreach (var (original, issues) in result.Changes)
        {
            if (!byScript.TryGetValue(original.ScriptKey, out var lines) || original.Number < 1 || original.Number > lines.Length)
            {
                continue;
            }

            var current = lines[original.Number - 1];
            if (!ReferenceEquals(current.Unit, original.Unit) && current.Unit != original.Unit)
            {
                continue;
            }

            var updated = current with { Issues = issues };
            lines[original.Number - 1] = updated;
            applied.Add(updated);
        }

        return applied;
    }

    /// <summary>Monta as linhas de <paramref name="texts"/>; as ocorrências de <paramref name="report"/> casam por chave do script e id da linha.</summary>
    /// <param name="lineContext">O contexto do projeto, para refazer as regras da linha depois de uma edição (<see cref="LineContext"/>).</param>
    public static ProjectLines Create(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, QaReport? report = null, QaContext? lineContext = null) =>
        Build(texts, report, lineContext, null, null);

    private static ProjectLines Build(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, QaReport? report, QaContext? lineContext, ProjectLineContext? contextSource, QaEngine? engine)
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

        return new ProjectLines(scripts, byScript, lineContext, contextSource, engine);
    }

    /// <summary>Falantes distintos e não vazios, o que mais fala primeiro; empates em ordem ordinal.</summary>
    public static IReadOnlyList<string> SpeakersOf(IEnumerable<LineEntry> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines
            .Select(l => l.Unit.EffectiveSpeaker)
            .OfType<string>()
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
    }
}
