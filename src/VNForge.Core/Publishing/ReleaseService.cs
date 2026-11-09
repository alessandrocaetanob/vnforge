using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using VNForge.Patching;

namespace VNForge.Core.Publishing;

/// <param name="Version">Número da versão do patch (<c>1.2.0</c>); vira parte do nome dos arquivos.</param>
/// <param name="OutputDirectory">Onde gravar; o padrão é <c>releases/&lt;versão&gt;/</c> na pasta do projeto.</param>
/// <param name="Credits">Linha de créditos (<c>Tradução: Equipe · Revisão: Hana</c>); entra no CREDITOS.txt, no leia-me e no instalador.</param>
/// <param name="InstallerName">Nome do instalador sem a extensão; o padrão é <c>&lt;jogo&gt;-&lt;idioma&gt;-&lt;versão&gt;</c>.</param>
/// <param name="InstallerStub">O executável do instalador (<c>vnf-instalador</c>) que recebe o patch; o padrão é o que está ao lado do <c>vnf</c>.</param>
public sealed record ReleaseOptions(
    string Version,
    string? OutputDirectory = null,
    string? Credits = null,
    string? InstallerName = null,
    string? InstallerStub = null,
    bool AllowWarnings = false);

/// <summary>Um arquivo gerado na pasta de saída.</summary>
public sealed record ReleaseFile(string Name, long Size, string? Sha256);

/// <summary>Um arquivo do jogo que o patch toca, com o tamanho do original e o do delta que o pacote leva no lugar dele.</summary>
public sealed record ReleaseGameFile(string Path, long OriginalSize, long PatchSize, long ResultSize, bool Created);

/// <param name="PreviousVersion">A versão do patch anterior deste projeto; nula na primeira.</param>
/// <param name="Changes">As cenas cuja tradução mudou desde o patch anterior.</param>
public sealed record ReleaseResult(
    string Version,
    string? PreviousVersion,
    string OutputDirectory,
    IReadOnlyList<ReleaseFile> Files,
    IReadOnlyList<ReleaseGameFile> GameFiles,
    PatchVerification Verification,
    ChangesSinceTest Changes);

/// <summary>Os passos de <see cref="ReleaseService.Create"/>, na ordem: o que o jogador recebe só é verificado depois de montado.</summary>
public enum ReleaseStep
{
    /// <summary>Lê os textos e compara com a versão anterior.</summary>
    Check,

    /// <summary>Reconstrói o <c>build/</c>.</summary>
    Build,

    /// <summary>Descobre quais arquivos do jogo mudam.</summary>
    Diff,

    /// <summary>Calcula o delta de cada um e grava o <c>.vnfpatch</c>.</summary>
    Patch,

    /// <summary>Junta o instalador e escreve o leia-me, os créditos, as mudanças e as licenças.</summary>
    Package,

    /// <summary>Aplica o patch numa cópia limpa e compara com o build.</summary>
    Verify,
}

/// <summary>
/// Um relato de <see cref="ReleaseService.Create"/>. O primeiro de cada <paramref name="Step"/> o abre (sem detalhe); os seguintes o acompanham.
/// </summary>
/// <param name="Detail">O que o passo achou, em português ("4 arquivos"); nulo enquanto não sabe.</param>
/// <param name="Fraction">De 0 a 1, quando o passo sabe o quanto já fez (o patch, arquivo a arquivo).</param>
/// <param name="Item">O arquivo do jogo em que o passo está.</param>
public sealed record ReleaseProgress(ReleaseStep Step, string? Detail = null, double? Fraction = null, string? Item = null);

/// <summary>O que o projeto guarda de cada versão lançada: de onde o próximo lançamento tira "o que mudou desde o patch anterior" e a estimativa do patch.</summary>
/// <param name="GameFiles">Os arquivos do jogo que o patch tocou; nulo nos registros anteriores a este campo.</param>
public sealed record ReleaseRecord(string Version, DateTimeOffset CreatedAt, TranslationSnapshot? Snapshot, IReadOnlyList<ReleaseFile> Files, IReadOnlyList<ReleaseGameFile>? GameFiles = null);

/// <summary>
/// <c>vnf release</c>: confere e reconstrói o <c>build/</c>, calcula um delta por arquivo do jogo que muda, aplica o patch numa cópia limpa e compara com o
/// build, e junta o instalador (um executável com o patch dentro), o leia-me, os créditos e a lista do que mudou. Nada é gravado na pasta do jogo e o
/// patch nunca leva arquivo inteiro do jogo (regra 1 do CLAUDE.md); o instalador confere o SHA-256 de cada arquivo antes de tocar no jogo.
/// </summary>
public sealed partial class ReleaseService(IReadOnlyList<IEngine> engines, QaEngine? qa = null, TimeProvider? clock = null)
{
    public const string FolderName = "releases";

    public const string InstallerFileName = "vnf-instalador";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]*$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MultipleHyphens();

    /// <summary>Onde o <c>vnf</c> procura o executável do instalador quando <see cref="ReleaseOptions.InstallerStub"/> não diz.</summary>
    public static string DefaultInstallerStub =>
        Path.Combine(AppContext.BaseDirectory, InstallerFileName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    /// <exception cref="QaBlockedException">A Conferência achou erro (ou aviso, sem <see cref="ReleaseOptions.AllowWarnings"/>).</exception>
    /// <exception cref="VNForgeException">Versão inválida, instalador ausente, nada para lançar, jogo que não é a versão de referência, ou a verificação do patch falhou.</exception>
    public ReleaseResult Create(ProjectFolder project, ReleaseOptions options, IProgress<ReleaseProgress>? steps = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var version = options.Version?.Trim() ?? string.Empty;
        if (!VersionPattern().IsMatch(version))
        {
            throw new VNForgeException($"Versão inválida: '{options.Version}'. Use letras, números, ponto, hífen ou sublinhado (1.2.0).");
        }

        var stub = options.InstallerStub ?? DefaultInstallerStub;
        if (!File.Exists(stub))
        {
            throw new VNForgeException(
                $"Não achei o executável do instalador em '{stub}'. Ele vem no pacote do vnf; num checkout, publique src/VNForge.Installer e passe o caminho em --installer.");
        }

        using var projectLock = ProjectLock.Acquire(project);
        var config = project.LoadConfig();
        var previous = LoadRecords(project).Where(r => !string.Equals(r.Version, version, StringComparison.Ordinal)).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        var output = Path.GetFullPath(options.OutputDirectory ?? Path.Combine(project.Root, FolderName, version));
        if (IsInside(output, Path.GetFullPath(config.Game.Path, project.Root)))
        {
            throw new VNForgeException($"A pasta de saída '{output}' é a pasta do jogo ou está dentro dela. Escolha outra: os arquivos do lançamento não podem ir para o jogo.");
        }

        Directory.CreateDirectory(output);

        steps?.Report(new ReleaseProgress(ReleaseStep.Check));
        var texts = project.LoadAllText();
        var snapshot = TranslationChanges.Snapshot(texts);
        var changes = previous is null
            ? ChangesSinceTest.None
            : TranslationChanges.Since(new GameLastTest(previous.CreatedAt, GameTestKind.Translation, [], previous.Snapshot), texts);

        steps?.Report(new ReleaseProgress(ReleaseStep.Build));
        var built = ProjectBuilder.Build(project, engines, qa: qa, allowWarnings: options.AllowWarnings, cancellationToken: cancellationToken);
        steps?.Report(new ReleaseProgress(ReleaseStep.Build, Detail: BuildDetail(built)));

        steps?.Report(new ReleaseProgress(ReleaseStep.Diff));
        var patches = GameTester.PlanExecutablePatches(project, engines);
        try
        {
            var plan = GameTester.WithPatches(GameInjector.Plan(project), patches);
            if (plan.Files.Count == 0)
            {
                throw new VNForgeException("O build é igual ao jogo: não há arquivo alterado para pôr no patch.");
            }

            foreach (var file in plan.Files)
            {
                if (file.OriginalSha256 is not null && file.OriginalSource is null)
                {
                    throw new VNForgeException($"O original de '{file.RelativePath}' não está acessível; rode vnf extract de novo.");
                }

                if (file.OriginalSha256 is null)
                {
                    NewFilePolicy.EnsureAllowed(plan.GameDirectory, file.RelativePath);
                }
            }

            var sources = plan.Files
                .Select(f => new PatchSource(f.RelativePath, f.OriginalSha256 is null ? null : f.OriginalSource, f.BuildPath))
                .ToList();
            steps?.Report(new ReleaseProgress(ReleaseStep.Diff, Detail: $"{sources.Count} {(sources.Count == 1 ? "arquivo" : "arquivos")}"));

            return Package(project, config, options, version, previous, output, stub, sources, changes, snapshot, texts, steps, cancellationToken);
        }
        catch (PatchException ex)
        {
            throw new VNForgeException(ex.Message, ex);
        }
        finally
        {
            patches.ForEach(GameInjector.DiscardPatch);
        }
    }

    private ReleaseResult Package(
        ProjectFolder project,
        ProjectConfig config,
        ReleaseOptions options,
        string version,
        ReleaseRecord? previous,
        string output,
        string stub,
        List<PatchSource> sources,
        ChangesSinceTest changes,
        TranslationSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts,
        IProgress<ReleaseProgress>? steps,
        CancellationToken cancellationToken)
    {
        var gameName = GameNameOf(config);
        var baseName = options.InstallerName ?? DefaultInstallerName(config, version);
        var credits = string.IsNullOrWhiteSpace(options.Credits) ? null : options.Credits.Trim();
        var date = _clock.GetLocalNow();
        var documents = new ReleaseDocuments(gameName, version, previous?.Version, date, credits, changes);
        var readMe = documents.ReadMe();

        steps?.Report(new ReleaseProgress(ReleaseStep.Patch));
        var patchPath = Path.Combine(output, baseName + ".vnfpatch");
        PatchManifest manifest;
        var perFile = new FileProgress(p =>
        {
            if (p.Total > 0)
            {
                steps?.Report(new ReleaseProgress(ReleaseStep.Patch, Fraction: (double)p.Done / p.Total, Item: p.Path));
            }
        });
        using (var file = new FileStream(patchPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            manifest = PatchBuilder.Build(new PatchInfo(gameName, version, previous?.Version, credits, readMe), sources, file, perFile, cancellationToken);
        }

        steps?.Report(new ReleaseProgress(ReleaseStep.Package));
        var installerPath = Path.Combine(output, baseName + Path.GetExtension(stub));
        using (var package = new FileStream(patchPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            InstallerBundle.Create(stub, package, installerPath);
        }

        WriteText(Path.Combine(output, "LEIA-ME.txt"), readMe);
        WriteText(Path.Combine(output, "CREDITOS.txt"), documents.Credits());
        WriteText(Path.Combine(output, "MUDANCAS.txt"), documents.Changes());
        WriteText(Path.Combine(output, ThirdPartyLicenses.FileName), ThirdPartyLicenses.Text);

        steps?.Report(new ReleaseProgress(ReleaseStep.Verify));
        PatchVerification verification;
        var workDirectory = Path.Combine(project.CacheDirectory, "release-check", Guid.NewGuid().ToString("N"));
        using (var package = PatchPackage.Open(new FileStream(patchPath, FileMode.Open, FileAccess.Read, FileShare.Read)))
        {
            verification = PatchVerifier.Verify(package, sources, workDirectory, cancellationToken: cancellationToken);
        }

        var files = new[] { installerPath, patchPath }
            .Select(p => new ReleaseFile(Path.GetFileName(p), new FileInfo(p).Length, GamePathsSha(p)))
            .Concat(new[] { "LEIA-ME.txt", "CREDITOS.txt", "MUDANCAS.txt", ThirdPartyLicenses.FileName }.Select(n => new ReleaseFile(n, new FileInfo(Path.Combine(output, n)).Length, null)))
            .ToList();
        var gameFiles = manifest.Files
            .Select(f => new ReleaseGameFile(f.Path, f.OriginalSize, f.Length, f.ResultSize, f.Created))
            .ToList();
        SaveRecord(project, new ReleaseRecord(version, _clock.GetUtcNow(), snapshot, files, gameFiles));
        ReleaseTexts.Save(project, version, texts);
        return new ReleaseResult(version, previous?.Version, output, files, gameFiles, verification, changes);
    }

    private static string BuildDetail(BuildResult built) => built.ReusedScripts > 0
        ? $"{built.ReusedScripts} de {built.ScriptCount} scripts reaproveitados"
        : $"{built.RebuiltScripts.Count} de {built.ScriptCount} scripts reconstruídos";

    /// <summary>As versões já lançadas deste projeto, lidas de <c>releases/*.json</c>; um registro ilegível é pulado.</summary>
    public static IReadOnlyList<ReleaseRecord> LoadRecords(ProjectFolder project)
    {
        var folder = Path.Combine(project.Root, FolderName);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var records = new List<ReleaseRecord>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize<ReleaseRecord>(File.ReadAllBytes(path), RecordJson) is { Version.Length: > 0 } record)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // Um registro corrompido não impede o lançamento: ele só some do histórico.
            }
        }

        return records;
    }

    private static readonly JsonSerializerOptions RecordJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    private static void SaveRecord(ProjectFolder project, ReleaseRecord record)
    {
        var folder = Path.Combine(project.Root, FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, record.Version + ".json"), JsonSerializer.Serialize(record, RecordJson) + "\n", new UTF8Encoding(false));
    }

    /// <summary>O nome do instalador sem a extensão quando <see cref="ReleaseOptions.InstallerName"/> não diz: <c>&lt;jogo&gt;-&lt;idioma&gt;-&lt;versão&gt;</c>.</summary>
    public static string DefaultInstallerName(ProjectConfig config, string version)
    {
        ArgumentNullException.ThrowIfNull(config);
        return $"{SafeName(GameNameOf(config))}-{config.Languages.Target.ToUpperInvariant()}-{version}";
    }

    /// <summary>A <paramref name="folder"/> é a <paramref name="parent"/> ou fica dentro dela (os dois já como caminhos completos).</summary>
    public static bool IsInside(string folder, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(child, root, comparison)
            || child.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || child.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string GameNameOf(ProjectConfig config)
    {
        if (config.Game.Title?.Translation is { Length: > 0 } translated)
        {
            return translated;
        }

        return config.Game.Title?.Original is { Length: > 0 } original ? original : config.Name;
    }

    private static void WriteText(string path, string text) =>
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string GamePathsSha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private sealed class FileProgress(Action<PatchProgress> handler) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) => handler(value);
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => char.IsWhiteSpace(c) || invalid.Contains(c) ? '-' : c).ToArray());
        clean = MultipleHyphens().Replace(clean, "-").Trim('-');
        return clean.Length == 0 ? "traducao" : clean;
    }
}

/// <summary>Os três textos que o jogador lê, com as datas no formato do Brasil.</summary>
internal sealed class ReleaseDocuments(string gameName, string version, string? previousVersion, DateTimeOffset date, string? credits, ChangesSinceTest changes)
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    private string Title => $"{gameName} · Tradução para português do Brasil\nVersão {version} · {date.ToString("dd/MM/yyyy", Brazil)}";

    public string ReadMe()
    {
        var text = new StringBuilder();
        text.AppendLine(Title).AppendLine();
        text.AppendLine("COMO INSTALAR");
        text.AppendLine("1. Feche o jogo.");
        text.AppendLine("2. Execute o instalador.");
        text.AppendLine("3. Aponte a pasta do jogo e confirme a instalação.");
        text.AppendLine("O instalador confere se o jogo é a versão certa e guarda uma cópia dos arquivos originais antes de mexer em qualquer coisa.").AppendLine();
        text.AppendLine("COMO DESINSTALAR");
        text.AppendLine("Execute o instalador de novo e escolha Voltar ao jogo original. Os arquivos originais voltam byte a byte e os seus saves não são tocados.").AppendLine();
        text.AppendLine(previousVersion is null ? "O QUE HÁ NESTA VERSÃO" : $"O QUE MUDOU DESDE A {previousVersion}");
        text.AppendLine(Summary()).AppendLine();
        if (credits is not null)
        {
            text.AppendLine("CRÉDITOS").AppendLine(credits);
        }

        text.AppendLine().AppendLine("AVISOS").Append("O instalador usa a biblioteca VCDiff (Apache 2.0) e outras bibliotecas de código aberto. Os textos das licenças estão em LICENCAS.txt e no próprio instalador (instalador --licencas).");
        return text.ToString().TrimEnd() + "\n";
    }

    public string Credits() =>
        $"{Title}\n\n{credits ?? "Sem créditos informados. Use --credits ao gerar a versão."}\n";

    public string Changes()
    {
        var text = new StringBuilder();
        text.AppendLine(Title).AppendLine();
        text.AppendLine(previousVersion is null ? "Primeira versão do patch." : $"Mudanças desde a {previousVersion}:").AppendLine();
        foreach (var scene in changes.Scenes)
        {
            text.AppendLine($"Cena {scene.Number} ({scene.Key}): {scene.Lines.ToString("N0", Brazil)} {(scene.Lines == 1 ? "linha" : "linhas")}");
        }

        if (changes.Scenes.Count == 0 && previousVersion is not null)
        {
            text.AppendLine(Summary());
        }

        return text.ToString().TrimEnd() + "\n";
    }

    private string Summary() => (previousVersion, changes.State) switch
    {
        (null, _) => "Primeira versão do patch.",
        (_, ChangesSinceTestState.Changed) =>
            $"{changes.TotalLines.ToString("N0", Brazil)} {(changes.TotalLines == 1 ? "linha" : "linhas")} em {changes.Scenes.Count.ToString("N0", Brazil)} {(changes.Scenes.Count == 1 ? "cena" : "cenas")}. Veja MUDANCAS.txt.",
        (_, ChangesSinceTestState.Unchanged) => "Nenhuma fala mudou; a versão refaz o patch sobre o mesmo texto.",
        _ => "A lista de falas que mudaram não está disponível para a versão anterior.",
    };
}
