using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Um script que o build reconstruiu porque tinha tradução a gravar.</summary>
/// <param name="Key">Chave do script (<c>yst00012</c>).</param>
/// <param name="Name">Nome do arquivo (<c>yst00012.ybn</c>).</param>
/// <param name="Lines">Unidades de texto trocadas.</param>
public sealed record BuiltScript(string Key, string Name, int Lines);

/// <summary>Opções do build que não são injeção de dependência (<see cref="ProjectBuilder.Build"/>).</summary>
/// <param name="OnlyChanged">Build incremental (<c>--only-changed</c>): reaproveita o resultado do build anterior dos scripts cujas entradas não mudaram (<see cref="BuildState"/>) e, se nenhum mudou e <c>build/</c> está como o anterior o deixou, nem regrava os pacotes. <c>false</c> reconstrói tudo.</param>
/// <param name="EncodingStrategy">Estratégia de codificação só deste build (<c>--encoding</c>); <c>null</c> usa a do <c>vnforge.json</c>, que não é alterado.</param>
/// <param name="SelectTexts">Troca os textos de <c>text/*.json</c> que o build e o QA veem (o teste no jogo usa para reconstruir só uma cena ou só a frase dos acentos); os arquivos em disco não mudam. <c>null</c> usa todos.</param>
public sealed record BuildOptions(
    bool OnlyChanged = false,
    string? EncodingStrategy = null,
    Func<IReadOnlyDictionary<string, IReadOnlyList<TextUnit>>, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>>>? SelectTexts = null);

/// <summary>Resultado de <see cref="ProjectBuilder.Build"/>.</summary>
/// <param name="GameDirectory">Pasta do jogo cujos pacotes serviram de base.</param>
/// <param name="BuildDirectory">Pasta <c>build/</c> que recebeu os pacotes.</param>
/// <param name="Archives">Pacotes gravados em <c>build/</c>.</param>
/// <param name="ScriptCount">Scripts lidos de <c>.cache/game</c>, com ou sem tradução.</param>
/// <param name="RebuiltScripts">Scripts reconstruídos, na ordem da chave. Os outros saem byte a byte iguais.</param>
/// <param name="Warnings">Avisos da engine e dos arquivos de texto.</param>
/// <param name="Encoding">Caracteres que a estratégia de codificação trocou por um substituto (nenhum bloqueante: esses abortam o build).</param>
/// <param name="Qa">O QA que rodou antes do build: sem erro, e só com avisos quando o build rodou com <c>allowWarnings</c>; mais os avisos silenciados.</param>
public sealed record BuildResult(
    string GameDirectory,
    string BuildDirectory,
    IReadOnlyList<RepackedArchive> Archives,
    int ScriptCount,
    IReadOnlyList<BuiltScript> RebuiltScripts,
    IReadOnlyList<string> Warnings,
    EncodingReport Encoding,
    QaReport Qa)
{
    /// <summary>Scripts com tradução cujo resultado do build anterior foi reaproveitado, sem chamar <see cref="IScriptFormat.Rebuild"/> (só no build incremental).</summary>
    public int ReusedScripts { get; init; }

    /// <summary>Total de unidades de texto trocadas.</summary>
    public int TotalLines => RebuiltScripts.Sum(s => s.Lines);
}

/// <summary>
/// O <c>vnf build</c>: reabre o jogo descompactado em <c>.cache/game</c>, reconstrói com <see cref="IScriptFormat.Rebuild"/> os scripts que têm
/// tradução em <c>text/*.json</c> e pede à engine (<see cref="IGameRepacker"/>) os pacotes novos em <c>build/</c>, no mesmo caminho relativo
/// do jogo, que é o que o <c>vnf inject</c> espera. Só lê a pasta do jogo e o <c>.cache/</c>.
/// </summary>
/// <remarks>
/// O <see cref="ITextEncoder"/> vem do <c>encoding.strategy</c> do <c>vnforge.json</c> (<see cref="TextEncoderRegistry"/>). Antes de reconstruir, o
/// build roda o QA (<see cref="QaEngine"/>) e recusa com <see cref="QaBlockedException"/> se achar erro, ou aviso (exceto linha sem tradução) sem <c>allowWarnings</c>; os avisos que a linha marcou como "ok" não contam. Depois confere todas as traduções e, se a estratégia não representa algum caractere, recusa com o relatório completo
/// (<see cref="EncodingReportException"/>). Antes disso o texto passa pelo pipeline do <c>formatting.json</c> (<see cref="FormattingPipeline"/>), então
/// a codificação confere o texto já formatado; os avisos do pipeline vão para <see cref="BuildResult.Warnings"/>, menos os que o QA já aponta (caixa acima do limite de linhas), que ficam só em <see cref="BuildResult.Qa"/>. Scripts sem tradução não são reconstruídos. Cada pacote descompactado
/// sai em <c>build/</c>: o que não teve script alterado é copiado idêntico, e o <c>vnf inject</c> ignora o arquivo igual ao do jogo.
/// </remarks>
public static class ProjectBuilder
{
    /// <summary>Reconstrói o jogo do <paramref name="project"/> em <c>build/</c>.</summary>
    /// <param name="project">Projeto aberto, já extraído (<c>vnf extract</c>).</param>
    /// <param name="engines">Engines conhecidas; vale a de <c>engine.id</c> do <c>vnforge.json</c>.</param>
    /// <param name="cancellationToken">Cancela entre um script e outro; um cancelamento não deixa <c>build/</c> pela metade.</param>
    /// <param name="encoders">Estratégias disponíveis; o padrão é <see cref="TextEncoderRegistry.Default"/>.</param>
    /// <param name="qa">Regras de QA que rodam antes de reconstruir; o padrão é <see cref="QaEngine.Default"/>.</param>
    /// <param name="allowWarnings">Libera o build com avisos de QA (<c>--allow-warnings</c>); erro de QA bloqueia sempre, e aviso de linha sem tradução (<see cref="UntranslatedQaRule"/>) nunca bloqueia.</param>
    /// <param name="options">Build incremental e estratégia de codificação só deste build; <c>null</c> reconstrói tudo com a estratégia do <c>vnforge.json</c>.</param>
    /// <param name="progress">Recebe um relato a cada script do jogo examinado e um final.</param>
    /// <exception cref="QaBlockedException">O QA achou erro, ou aviso sem <paramref name="allowWarnings"/>; nada é gravado.</exception>
    /// <exception cref="VNForgeException">
    /// Engine desconhecida ou sem suporte ao build, estratégia de codificação desconhecida ou sem encoder, jogo ou <c>.cache/game</c> inexistente,
    /// script ilegível, <c>text/*.json</c> inválido, tradução que não grava (caractere que a estratégia não representa, que lança
    /// <see cref="EncodingReportException"/> com todos os casos; marcador inválido, original divergente) ou falha ao gravar. A mensagem, em
    /// português, cita o script e, quando a falha é de uma linha, o id dela. Em qualquer erro o <c>build/</c> anterior continua como estava.
    /// </exception>
    public static BuildResult Build(ProjectFolder project, IReadOnlyList<IEngine> engines, TextEncoderRegistry? encoders = null, QaEngine? qa = null, bool allowWarnings = false, BuildOptions? options = null, IProgress<ScriptWorkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engines);

        var config = project.LoadConfig();
        var engine = engines.FirstOrDefault(e => string.Equals(e.Id, config.Engine.Id, StringComparison.Ordinal))
            ?? throw new VNForgeException($"A engine '{config.Engine.Id}' do {ProjectFolder.ConfigFileName} não é conhecida por esta versão do vnf.");
        if (engine is not IGameUnpacker unpacker || engine is not IGameRepacker repacker)
        {
            throw new VNForgeException($"A engine {engine.DisplayName} ainda não suporta o build.");
        }

        var encoder = (encoders ?? TextEncoderRegistry.Default).Create(options?.EncodingStrategy ?? config.Encoding.Strategy);

        var gameDirectory = Path.GetFullPath(config.Game.Path, project.Root);
        if (!Directory.Exists(gameDirectory))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {gameDirectory}");
        }

        if (ProjectExtractor.IsInside(ProjectExtractor.ResolveLinks(gameDirectory), ProjectExtractor.ResolveLinks(project.BuildDirectory)))
        {
            throw new VNForgeException(
                $"O build/ do projeto ('{project.BuildDirectory}') fica dentro da pasta do jogo ('{gameDirectory}'): o VNForge não grava nada no jogo fora da injeção com backup.");
        }

        if (!Directory.Exists(project.GameCacheDirectory))
        {
            throw new VNForgeException($"A pasta '{project.GameCacheDirectory}' não existe. Rode vnf extract antes do build.");
        }

        var game = unpacker.Open(project.GameCacheDirectory);
        if (game.Scripts.Count == 0)
        {
            throw new VNForgeException($"Nenhum script foi encontrado em '{project.GameCacheDirectory}'. Rode vnf extract.");
        }

        var profile = project.LoadFormatting();
        var loaded = project.LoadAllText();
        if (options?.SelectTexts is { } select)
        {
            loaded = select(loaded);
        }

        var fingerprint = BuildState.FingerprintTexts(loaded, encoder.Id, project.FormattingPath);
        var gameKeys = game.Scripts.Select(sc => new ScriptFile(sc.Name, Array.Empty<byte>()).Key).ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>(game.Warnings);
        var texts = ApplyFormatting(profile, loaded, gameKeys, warnings);
        var report = EncodingReporter.Create(texts.Where(t => gameKeys.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value), encoder);
        if (report.HasBlocking)
        {
            throw new EncodingReportException(report);
        }

        // Depois do relatório de codificação: o erro específico (com a posição) vem antes do QA genérico. O QA vê só os scripts do jogo, como o relatório; texto de script que não existe mais não é gravado.
        var characters = project.LoadCharacters();
        var qaReport = (qa ?? QaEngine.Default).Run(new QaContext(loaded.Where(t => gameKeys.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value), project.LoadGlossary(), characters, profile, encoder) { Style = config.Style });
        if (qaReport.Blocks(allowWarnings))
        {
            throw new QaBlockedException(qaReport, allowWarnings);
        }

        DropWarningsReportedByQa(warnings, qaReport);

        var previous = options?.OnlyChanged == true ? BuildState.Load(project) : null;
        var (rebuilt, built, state, reused) = RebuildScripts(project, game, ResolveSpeakers(texts, new CharacterBook(characters), encoder, gameKeys), encoder, previous, warnings, progress, cancellationToken);

        if (previous is not null && built.Count == 0 && SameScripts(previous, state) && previous.ArchivesIntact(project))
        {
            warnings.AddRange(previous.Warnings);
            if (previous.TextFingerprint != fingerprint)
            {
                (previous with { TextFingerprint = fingerprint }).TrySave(project);
            }

            return new BuildResult(gameDirectory, project.BuildDirectory, [.. previous.Archives.Select(a => new RepackedArchive(a.GamePath, a.ReplacedFiles))], game.Scripts.Count, built, warnings, report, qaReport) { ReusedScripts = reused };
        }

        var staging = project.BuildDirectory + ".tmp";
        DeleteIfExists(staging);
        try
        {
            Directory.CreateDirectory(staging);
            var packed = repacker.Repack(gameDirectory, project.GameCacheDirectory, rebuilt, staging, cancellationToken);
            warnings.AddRange(packed.Warnings);
            cancellationToken.ThrowIfCancellationRequested();
            Swap(staging, project.BuildDirectory);
            new BuildState(BuildState.CurrentVersion, state, [.. packed.Archives.Select(a => new BuildStateArchive(a.GamePath, a.ReplacedFiles, ArchiveLength(project, a.GamePath)))], [.. packed.Warnings], fingerprint).TrySave(project);
            return new BuildResult(gameDirectory, project.BuildDirectory, packed.Archives, game.Scripts.Count, built, warnings, report, qaReport) { ReusedScripts = reused };
        }
        catch
        {
            DeleteIfExists(staging);
            throw;
        }
    }

    /// <summary>
    /// Só o relatório de codificação (<c>vnf build --report</c>): confere as traduções do <paramref name="project"/> com a estratégia do
    /// <c>vnforge.json</c> sem tocar em <c>.cache/</c>, <c>build/</c> nem no jogo.
    /// </summary>
    /// <param name="encodingStrategy">Estratégia só deste relatório (<c>--encoding</c>); <c>null</c> usa a do <c>vnforge.json</c>.</param>
    /// <exception cref="VNForgeException">Estratégia desconhecida ou sem encoder, ou <c>text/*.json</c> inválido.</exception>
    public static EncodingReport ReportEncoding(ProjectFolder project, TextEncoderRegistry? encoders = null, string? encodingStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var encoder = (encoders ?? TextEncoderRegistry.Default).Create(encodingStrategy ?? project.LoadConfig().Encoding.Strategy);
        return EncodingReporter.Create(ApplyFormatting(project.LoadFormatting(), project.LoadAllText(), null, []), encoder);
    }

    /// <summary>
    /// Passa pelo pipeline do perfil a tradução de cada unidade que o build gravaria (<see cref="TextUnit.NeedsRewrite"/>) e cujo papel está em
    /// <see cref="FormattingProfile.Kinds"/>; as outras unidades seguem iguais. O <c>text/*.json</c> em disco não muda. Os avisos das etapas vão para
    /// <paramref name="warnings"/> com o id da linha.
    /// </summary>
    /// <param name="keys">Só os scripts com essas chaves são formatados; <c>null</c> formata todos.</param>
    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> ApplyFormatting(
        FormattingProfile profile,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts,
        HashSet<string>? keys,
        List<string> warnings)
    {
        var pipeline = FormattingPipeline.Create(profile);
        if (pipeline.Stages.Count == 0)
        {
            return texts;
        }

        var formatted = new SortedDictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        foreach (var (key, units) in texts)
        {
            formatted[key] = keys is not null && !keys.Contains(key)
                ? units
                : [.. units.Select(unit => FormatUnit(pipeline, profile, unit, warnings))];
        }

        return formatted;
    }

    /// <summary>
    /// A caixa acima do limite de linhas sai do pipeline como aviso do build e da regra <see cref="BoxLineLimitRule"/> como ocorrência de QA, com o mesmo
    /// texto. Fica só a do QA, que o revisor pode marcar como "ok"; o aviso do build sobra apenas quando nenhuma regra de QA o cobre (QA sem a regra).
    /// </summary>
    private static void DropWarningsReportedByQa(List<string> warnings, QaReport qa)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        var covered = qa.OtherIssues.Concat(qa.Silenced).Select(i => $"{i.UnitId}: {i.Message}").ToHashSet(StringComparer.Ordinal);
        warnings.RemoveAll(covered.Contains);
    }

    /// <summary>
    /// Troca o <see cref="TextUnit.Speaker"/> de cada unidade que o build grava pelo nome que vai para o jogo (<see cref="SpeakerText.Resolve"/>): a tradução de
    /// <c>characters.json</c> ou, sem cadastro, o nome original (largura normalizada só quando a estratégia não o grava). O <c>text/*.json</c> em disco não muda.
    /// A Conferência já recusa o nome que não dá para gravar (<see cref="SpeakerEncodingRule"/>); aqui a recusa só vale para quem roda o build sem essa regra.
    /// </summary>
    /// <exception cref="VNForgeException">O nome de uma linha não pode ser gravado na estratégia.</exception>
    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> ResolveSpeakers(
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts,
        CharacterBook characters,
        ITextEncoder encoder,
        HashSet<string> keys)
    {
        var resolved = new SortedDictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        foreach (var (key, units) in texts)
        {
            resolved[key] = keys.Contains(key) ? [.. units.Select(unit => ResolveSpeaker(unit, characters, encoder))] : units;
        }

        return resolved;
    }

    private static TextUnit ResolveSpeaker(TextUnit unit, CharacterBook characters, ITextEncoder encoder)
    {
        if (!unit.NeedsRewrite || string.IsNullOrWhiteSpace(unit.Speaker))
        {
            return unit;
        }

        var name = SpeakerText.Resolve(unit.Speaker, characters, encoder);
        return name.Error is { } error ? throw new VNForgeException($"A linha {unit.Id} não pode ser gravada: {error}") : unit with { Speaker = name.Name };
    }

    private static TextUnit FormatUnit(FormattingPipeline pipeline, FormattingProfile profile, TextUnit unit, List<string> warnings)
    {
        if (!unit.NeedsRewrite || !profile.Kinds.Contains(unit.Kind))
        {
            return unit;
        }

        try
        {
            var result = pipeline.Format(unit.Translation ?? string.Empty);
            warnings.AddRange(result.Warnings.Select(w => $"{unit.Id}: {w}"));
            return unit with { Translation = result.Text };
        }
        catch (Exception ex) when (ex is ArgumentException or FormattingException)
        {
            throw new VNForgeException($"Não foi possível formatar a linha {unit.Id} com o {ProjectFolder.FormattingFileName}: {ex.Message}", ex);
        }
    }

    private static (List<RebuiltScript> Rebuilt, List<BuiltScript> Built, Dictionary<string, BuildStateScript> State, int Reused) RebuildScripts(
        ProjectFolder project,
        UnpackedGame game,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts,
        ITextEncoder encoder,
        BuildState? previous,
        List<string> warnings,
        IProgress<ScriptWorkProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rebuilt = new List<RebuiltScript>();
        var built = new List<BuiltScript>();
        var state = new Dictionary<string, BuildStateScript>(StringComparer.Ordinal);
        var reused = 0;
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var done = 0;
        foreach (var script in game.Scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScriptWorkProgress(done++, game.Scripts.Count, script.Name));
            var original = File.ReadAllBytes(script.Path);
            var file = new ScriptFile(script.Name, original);
            if (!keys.TryAdd(file.Key, script.Path))
            {
                throw new VNForgeException(
                    $"Os scripts '{keys[file.Key]}' e '{script.Path}' têm a mesma chave '{file.Key}'; dois scripts não podem usar o mesmo text/{file.Key}.json.");
            }

            if (!texts.TryGetValue(file.Key, out var units) || !units.Any(u => u.NeedsRewrite))
            {
                continue;
            }

            var hash = BuildState.HashInputs(original, encoder.Id, units);
            if (TryReuse(project, previous, file.Key, hash) is { } reuse)
            {
                state[file.Key] = reuse.Entry;
                if (reuse.Content is not null)
                {
                    rebuilt.Add(new RebuiltScript(script.Path, original, reuse.Content));
                }

                reused++;
                continue;
            }

            var content = RebuildScript(script, file, units, encoder, game.Context);

            if (content.AsSpan().SequenceEqual(original))
            {
                state[file.Key] = new BuildStateScript(hash, Identical: true, 0);
                continue;
            }

            BuildState.WriteScript(project, file.Key, hash, content);
            state[file.Key] = new BuildStateScript(hash, Identical: false, content.Length);
            rebuilt.Add(new RebuiltScript(script.Path, original, content));
            built.Add(new BuiltScript(file.Key, script.Name, units.Count(u => u.NeedsRewrite)));
        }

        progress?.Report(new ScriptWorkProgress(game.Scripts.Count, game.Scripts.Count, string.Empty));
        var known = keys.Keys.ToHashSet(StringComparer.Ordinal);
        warnings.AddRange(texts.Keys.Where(k => !known.Contains(k)).Select(k => $"text/{k}.json não corresponde a nenhum script do jogo descompactado; ignorado."));
        built.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return (rebuilt, built, state, reused);
    }

    private static byte[] RebuildScript(UnpackedScript script, ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext context)
    {
        try
        {
            return script.Format.Rebuild(file, units, encoder, context);
        }
        catch (Exception ex) when (ex is InvalidDataException or EncoderFallbackException or FormatException or ArgumentException or VNForgeException)
        {
            throw new VNForgeException($"Não foi possível reconstruir o script '{script.Name}' com o text/{file.Key}.json: {ex.Message}", ex);
        }
    }

    /// <summary>O resultado do build anterior para o script, se as entradas (<paramref name="hash"/>) são as mesmas e os bytes guardados ainda conferem; <c>Content</c> nulo quando o script saiu idêntico ao original.</summary>
    private static (BuildStateScript Entry, byte[]? Content)? TryReuse(ProjectFolder project, BuildState? previous, string key, string hash)
    {
        if (previous is null || !previous.Scripts.TryGetValue(key, out var saved) || saved.Hash != hash)
        {
            return null;
        }

        if (saved.Identical)
        {
            return (saved, null);
        }

        return BuildState.ReadScript(project, key, saved) is { } cached ? (saved, cached) : null;
    }

    private static bool SameScripts(BuildState previous, Dictionary<string, BuildStateScript> current) =>
        previous.Scripts.Count == current.Count && previous.Scripts.All(p => current.TryGetValue(p.Key, out var c) && c == p.Value);

    private static long ArchiveLength(ProjectFolder project, string gamePath) =>
        new FileInfo(Path.Combine(project.BuildDirectory, gamePath.Replace('/', Path.DirectorySeparatorChar))).Length;

    /// <summary>Põe <paramref name="staging"/> no lugar de <paramref name="target"/>; se a troca falhar, devolve o anterior.</summary>
    private static void Swap(string staging, string target)
    {
        var previous = target + ".old";
        DeleteIfExists(previous);
        var hadPrevious = Directory.Exists(target);
        if (hadPrevious)
        {
            Directory.Move(target, previous);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (hadPrevious && !Directory.Exists(target))
            {
                Directory.Move(previous, target);
            }

            throw;
        }

        DeleteIfExists(previous);
    }

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
