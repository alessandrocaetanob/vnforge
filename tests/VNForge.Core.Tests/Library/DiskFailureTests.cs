using System.Text.RegularExpressions;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Tests.Projects;
using VNForge.Patching;

namespace VNForge.Core.Tests.Library;

/// <summary>
/// R1, R2 e R7: cada caminho que grava projeto, manifesto, backup, biblioteca e pasta do jogo roda contra os quatro jeitos de o disco falhar.
/// O esperado é erro em português e o arquivo anterior com os mesmos bytes, nunca sucesso com dado perdido. Um caso por tipo de falha, e cada caso percorre todos os
/// caminhos da sua área e junta o que deu errado, para um teste só dizer quais caminhos falharam.
/// </summary>
public sealed class DiskFailureTests : IDisposable
{
    private const string Key = "yst00001";
    private const string FirstFile = "ysbin.ypf";
    private const string SecondFile = "zz.ypf";
    private const string Translated = "traduzido";
    private const string Original = "original";

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // O rename que zera só acontece num disco que não é local (é lá que a troca com .bak guarda o anterior). Num disco local um rename que mente já é erro, mas não devolve o anterior.
    public static TheoryData<DiskFailure, bool> Failures => new()
    {
        { DiskFailure.ZeroedRename, false },
        { DiskFailure.Locked, true },
        { DiskFailure.Locked, false },
        { DiskFailure.DiskFull, true },
        { DiskFailure.ReadOnly, true },
    };

    private static ProjectConfig Config(string name, string game) => new(
        name,
        new GameConfig(game, "jogo.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default);

    private static List<TextUnit> Units(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"今日は{i}\\p", null, TranslationStatus.Untranslated))];

    private static Func<string, bool> Is(string path) => candidate => Same(candidate, path);

    private static Func<string, bool> Under(string folder, params string[] except) =>
        candidate => Path.GetFullPath(candidate).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !except.Contains(Path.GetFileName(candidate), StringComparer.OrdinalIgnoreCase);

    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static byte[] Snapshot(string path) => File.ReadAllBytes(path);

    /// <summary>Roda <paramref name="write"/> com o disco falhando e confere o erro e os arquivos que tinham que ficar como estavam.</summary>
    private static void Check(List<string> problems, string path, Func<string, bool> fails, DiskFailure failure, bool local, Action write, params string[] intact)
    {
        var before = intact.ToDictionary(file => file, Snapshot);
        Exception? error;
        using (ProjectDisk.Use(new FailingDisk(failure, local, fails)))
        {
            error = Record.Exception(write);
        }

        if (error is null)
        {
            problems.Add($"{path}: terminou com sucesso apesar da falha ({failure}, local={local})");
            return;
        }

        if (!Regex.IsMatch(error.Message, "Não foi possível gravar|depois de gravado"))
        {
            problems.Add($"{path}: a mensagem não está em português: {error.Message}");
        }

        foreach (var (file, bytes) in before)
        {
            if (!File.Exists(file))
            {
                problems.Add($"{path}: {Path.GetFileName(file)} sumiu");
            }
            else if (!Snapshot(file).AsSpan().SequenceEqual(bytes))
            {
                problems.Add($"{path}: {Path.GetFileName(file)} ficou com {new FileInfo(file).Length} bytes e tinha {bytes.Length}");
            }
        }
    }

    private static void NoTemporaries(List<string> problems, string path, string folder)
    {
        foreach (var leftover in Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories))
        {
            problems.Add($"{path}: sobrou o temporário {Path.GetFileName(leftover)}");
        }
    }

    // R2: vnforge.json, o texto traduzido, a biblioteca e as tags são o trabalho da pessoa.
    [Theory]
    [MemberData(nameof(Failures))]
    public void R2_projeto_e_biblioteca_recusam_a_gravacao_que_falha_e_guardam_o_arquivo_anterior(DiskFailure failure, bool local)
    {
        var problems = new List<string>();
        var game = Path.Combine(_temp.Path, "jogo");
        var project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), Config("Minha VN", game));
        project.SaveText(Key, Units(2));

        Check(problems, "projeto/vnforge.json", Is(project.ConfigPath), failure, local, () => project.SaveConfig(Config("Outro nome", game)), project.ConfigPath);
        Check(problems, "projeto/texto", Is(project.TextPath(Key)), failure, local, () => project.SaveText(Key, Units(3)), project.TextPath(Key));
        NoTemporaries(problems, "projeto", project.Root);
        if (project.LoadConfig().Name != "Minha VN" || project.LoadText(Key).Count != 2)
        {
            problems.Add("projeto: o projeto não abre como estava");
        }

        var library = new LibraryFile(Path.Combine(_temp.Path, "config"));
        library.Save(new GameLibrary(localeEmulatorPath: "antes"));
        Check(problems, "biblioteca/library.json", Is(library.Path), failure, local, () => library.Save(new GameLibrary(localeEmulatorPath: "depois")), library.Path);
        var tags = new UserTagsFile(Path.Combine(_temp.Path, "config"));
        var saved = new UserTags();
        saved.Add("g1", "favorito");
        tags.Save(saved);
        saved.Add("g1", "terminado");
        Check(problems, "biblioteca/tags", Is(tags.Path), failure, local, () => tags.Save(saved), tags.Path);
        NoTemporaries(problems, "biblioteca", Path.Combine(_temp.Path, "config"));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // R1: manifesto e backup nascem antes de o jogo ser tocado; a pasta do jogo nunca pode ficar com arquivo vazio ou pela metade, na injeção nem na restauração.
    [Theory]
    [MemberData(nameof(Failures))]
    public void R1_manifesto_backup_e_pasta_do_jogo_recusam_a_gravacao_que_falha_e_o_jogo_fica_como_estava(DiskFailure failure, bool local)
    {
        var problems = new List<string>();
        var game = Path.Combine(_temp.Path, "jogo");
        var project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), Config("Teste", game));
        Seed(project, game, FirstFile, Original, Translated);
        Seed(project, game, SecondFile, Original, Translated);
        var gameFiles = new[] { Path.Combine(game, FirstFile), Path.Combine(game, SecondFile) };
        var injector = new GameInjector();

        Check(problems, "manifesto do backup", candidate => Path.GetFileName(candidate) == BackupManifest.FileName, failure, local, () => injector.Inject(GameInjector.Plan(project)), gameFiles);
        Assert.Empty(Directory.EnumerateFiles(project.BackupDirectory, BackupManifest.FileName, SearchOption.AllDirectories));

        Check(problems, "cópia do original no backup", Under(project.BackupDirectory, BackupManifest.FileName), failure, local, () => injector.Inject(GameInjector.Plan(project)), gameFiles);
        Assert.Empty(Directory.EnumerateFiles(project.BackupDirectory, BackupManifest.FileName, SearchOption.AllDirectories));

        // O segundo arquivo do jogo falha: o primeiro já foi gravado e a injeção o devolve ao original.
        Check(problems, "pasta do jogo na injeção", Is(Path.Combine(game, SecondFile)), failure, local, () => injector.Inject(GameInjector.Plan(project)), gameFiles);
        Assert.Equal(Original, File.ReadAllText(gameFiles[0]));

        // Injeta de verdade e tenta restaurar: o jogo continua traduzido, inteiro, e o backup continua valendo.
        foreach (var leftover in Directory.GetDirectories(project.BackupDirectory))
        {
            Directory.Delete(leftover, recursive: true);
        }

        injector.Inject(GameInjector.Plan(project));
        var manifest = Directory.EnumerateFiles(project.BackupDirectory, BackupManifest.FileName, SearchOption.AllDirectories).Single();
        var manifestBefore = Snapshot(manifest);
        var restore = GameInjector.PlanRestore(project);
        Check(problems, "pasta do jogo na restauração", Under(game), failure, local, () => injector.Restore(restore), [.. gameFiles, manifest]);
        if (!Snapshot(manifest).AsSpan().SequenceEqual(manifestBefore))
        {
            problems.Add("restauração: o backup foi marcado como restaurado sem ter restaurado");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void Seed(ProjectFolder project, string game, string name, string original, string translated)
    {
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, name), original);
        Directory.CreateDirectory(project.BuildDirectory);
        File.WriteAllText(Path.Combine(project.BuildDirectory, name), translated);
    }

    // R2: a conferência só vale se nenhum caminho do Core grava sem passar pela costura. Quem entra aqui sem passar por AtomicFile precisa dizer por que está certo.
    [Fact]
    public void R2_nenhum_caminho_do_core_grava_arquivo_sem_passar_pela_gravacao_conferida()
    {
        var core = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VNForge.Core"));
        Assert.True(Directory.Exists(core), $"Pasta do Core não encontrada: {core}");
        var write = new Regex(
            @"(?<![A-Za-z])File\.(WriteAll\w*|Create|Move|Copy|AppendAll\w*|OpenWrite)\(|Directory\.Move\(|new StreamWriter\(|new FileStream\([^)]*(FileMode\.(Create|CreateNew|OpenOrCreate|Append|Truncate)|FileAccess\.Write)",
            RegexOptions.CultureInvariant);

        var found = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(file => (File: Path.GetRelativePath(core, file).Replace('\\', '/'), Count: File.ReadLines(file).Count(line => write.IsMatch(line) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal))))
            .Where(entry => entry.Count > 0 && entry.File != "Library/AtomicFile.cs")
            .ToDictionary(entry => entry.File, entry => entry.Count);

        var unexpected = found.Where(entry => !Allowed.TryGetValue(entry.Key, out var allowed) || allowed.Count != entry.Value)
            .Select(entry => $"{entry.Key}: {entry.Value} gravação(ões) direta(s); use AtomicFile.WriteAllText/WriteAllBytes/Replace/Copy ou liste aqui com o motivo.");
        var stale = Allowed.Keys.Where(file => !found.ContainsKey(file)).Select(file => $"{file}: saiu da lista, tire a entrada.");

        Assert.True(!unexpected.Any() && !stale.Any(), string.Join(Environment.NewLine, unexpected.Concat(stale)));
    }

    // Cada entrada é uma gravação que não pode (ou não precisa) passar por AtomicFile, com o motivo.
    private static readonly Dictionary<string, (int Count, string Reason)> Allowed = new()
    {
        ["Demo/SampleProject.cs"] = (2, "projeto de exemplo montado numa pasta de preparo e movido inteiro; marca de reuso dentro da pasta nova"),
        ["Images/ImageBuild.cs"] = (1, "o fluxo vai para um temporário que AtomicFile.Replace troca e confere logo depois"),
        ["Jmdict/JmdictImporter.cs"] = (1, "índice reconstruível do dicionário, gerado num temporário e trocado por JmdictInstaller"),
        ["Jmdict/JmdictInstaller.cs"] = (2, "índice do dicionário (centenas de MB, reconstruível): a releitura SHA-256 dobraria a instalação"),
        ["Library/FileLock.cs"] = (1, "arquivo de trava, sem conteúdo"),
        ["Library/UserTagsFile.cs"] = (1, "arquivo de trava, sem conteúdo"),
        ["Logging/FileLoggerProvider.cs"] = (1, "log em modo append; perder uma linha não perde trabalho"),
        ["Projects/ProjectBuilder.cs"] = (3, "build/ é montado numa pasta de preparo e trocado por inteiro, com a anterior devolvida se falhar; é regenerado por vnf build"),
        ["Projects/RestorePointStore.cs"] = (4, "arquivos de uma pasta de ponto de restauração nova; o manifesto, gravado por AtomicFile por último, é o que faz o ponto existir"),
        ["Vndb/VndbImageCache.cs"] = (1, "o temporário é escrito sem bloquear a tarefa e AtomicFile.Replace o troca e confere em seguida; é cache, baixado de novo se faltar"),
        ["Publishing/ReleaseService.cs"] = (2, "pacote do patch e textos do release numa pasta de saída que o vnf release regenera; o pacote é reaberto e conferido em seguida"),
    };
}
