using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

public sealed class LibraryQueueCommandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly LibraryEngine YuRis = new("yuris", "YU-RIS", LibraryEngineSource.Engine);
    private static readonly LibraryEngine KiriKiri = new("kirikiri", "KiriKiri", LibraryEngineSource.Signature);
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_cli.UserConfigDirectory);

    private static LibraryGame Game(string id, string title, string? vndbId, LibraryEngine engine, int? minutes = null, string? folder = null) =>
        new(id, folder ?? "C:\\Jogos\\" + title, "Jogo.exe", engine, title, null, T0)
        {
            VndbId = vndbId,
            Confirmation = vndbId is null ? LibraryConfirmation.None : LibraryConfirmation.Manual,
            Info = vndbId is null ? null : new LibraryVndbInfo(title, null, null, null, null, null, minutes, 10, [], false),
        };

    private static VndbRelease Release(string vn, string language) =>
        new("r" + vn[1..], "Edição", null, [new VndbReleaseLanguage(language, null, null, false, false)], ["win"], "2020-01-01", null, true, true, false, null, null, [new VndbReleaseVisualNovel(vn, "complete")]);

    private static VndbVisualNovel Vn(string id, string title) =>
        new(id, title, null, [], [], "ja", 0, "2008-04-24", ["ja"], ["win"], null, 4, 3000, 10, null, []);

    private FakeVndbCatalog Prepare(params LibraryGame[] games)
    {
        File.Save(new GameLibrary(games));
        var fake = new FakeVndbCatalog(
            [Vn("v1", "Um"), Vn("v2", "Dois"), Vn("v3", "Três"), Vn("v4", "Quatro")],
            releases: [Release("v3", "pt-br"), Release("v4", "en")]);
        _cli.Cli.VndbCatalogFactory = () => fake;
        return fake;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private string NewGameDir()
    {
        var dir = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "Jogo.exe"), "MZ");
        var script = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290).AddWord("こんにちは", 1).AddReturnCode(0, 2).Build().Bytes;
        var ypf = new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes).AddFile("yst00001.ybn", script).AddFile("yst00002.ybn", script);
        System.IO.File.WriteAllBytes(Path.Combine(dir, "ysbin.ypf"), ypf.Build().Bytes);
        return dir;
    }

    [Fact]
    public async Task A_fila_confere_o_vndb_mostra_o_que_falta_traduzir_do_mais_curto_ao_mais_longo_e_deixa_de_fora_o_resto()
    {
        var fake = Prepare(
            Game("g1", "Um", "v1", YuRis, minutes: 3000),
            Game("g2", "Dois", "v2", YuRis, minutes: 90),
            Game("g3", "Três", "v3", YuRis, minutes: 30),
            Game("g4", "Quatro", "v4", KiriKiri, minutes: 10),
            Game("g5", "Cinco", null, YuRis));

        var result = await Run("library", "queue");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["GetReleasesAsync(v1)", "GetReleasesAsync(v2)", "GetReleasesAsync(v3)"], fake.Calls);
        var text = result.Output;
        Assert.True(text.IndexOf("Dois", StringComparison.Ordinal) < text.IndexOf("Um", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Um", StringComparison.Ordinal) < text.IndexOf("Cinco", StringComparison.Ordinal));
        Assert.DoesNotContain("Três", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Quatro", text, StringComparison.Ordinal);
        Assert.Contains("3 jogos na fila de 5 na biblioteca", text, StringComparison.Ordinal);
        Assert.Contains("vnf library translate <jogo>", text, StringComparison.Ordinal);
        Assert.Contains("sem casamento", text, StringComparison.Ordinal);
        Assert.Equal(PortugueseStatus.Complete, File.Load().Find("g3").CurrentPortuguese?.Status);
    }

    [Fact]
    public async Task Rodar_de_novo_nao_confere_outra_vez_e_o_offline_nunca_consulta()
    {
        var fake = Prepare(Game("g1", "Um", "v1", YuRis));
        await Run("library", "queue");
        Assert.Single(fake.Calls);

        await Run("library", "queue");
        Assert.Single(fake.Calls);

        await Run("library", "queue", "--again");
        Assert.Equal(2, fake.Calls.Count);

        File.Update(library => library.Replace(library.Find("g1") with { Portuguese = null }));
        var offline = await Run("library", "queue", "--offline");
        Assert.Equal(2, fake.Calls.Count);
        Assert.Contains("não conferido", offline.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Com_all_a_fila_mostra_a_situacao_de_cada_jogo()
    {
        Prepare(
            Game("g1", "Um", "v1", YuRis),
            Game("g3", "Três", "v3", YuRis),
            Game("g4", "Quatro", "v4", KiriKiri) with { ProjectPath = "C:\\Projetos\\quatro" });

        var result = await Run("library", "queue", "--all");

        Assert.Contains("na fila", result.Output, StringComparison.Ordinal);
        Assert.Contains("já em português", result.Output, StringComparison.Ordinal);
        Assert.Contains("com projeto", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_json_traz_a_ordem_o_estado_e_o_que_o_vndb_tem_em_portugues()
    {
        Prepare(Game("g1", "Um", "v1", YuRis, minutes: 3000), Game("g3", "Três", "v3", YuRis, minutes: 30));

        var result = await Run("library", "queue", "--all", "--json");

        var data = Data(result);
        Assert.Equal(1, data.GetProperty("queued").GetInt32());
        var entries = data.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["g1", "g3"], entries.Select(e => e.GetProperty("game").GetProperty("id").GetString()));
        Assert.Equal("queued", entries[0].GetProperty("state").GetString());
        Assert.Equal("none", entries[0].GetProperty("portuguese").GetProperty("status").GetString());
        Assert.Equal("alreadyTranslated", entries[1].GetProperty("state").GetString());
        Assert.Equal("complete", entries[1].GetProperty("portuguese").GetProperty("status").GetString());
        Assert.Equal(["r3"], entries[1].GetProperty("portuguese").GetProperty("releases").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Offline_e_again_juntos_sao_recusados()
    {
        Prepare(Game("g1", "Um", "v1", YuRis));

        var result = await Run("library", "queue", "--offline", "--again");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--offline", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traduzir_cria_o_projeto_com_a_ficha_do_vndb_e_liga_o_jogo()
    {
        var gameDir = NewGameDir();
        var fake = Prepare(Game("g1", "Um", "v1", YuRis, folder: gameDir));

        var result = await Run("library", "translate", "Um", "meu-projeto");

        Assert.Equal(0, result.ExitCode);
        var projectPath = Path.Combine(_cli.WorkingDirectory, "meu-projeto");
        var project = ProjectFolder.Open(projectPath);
        Assert.Equal("Um", project.LoadConfig().Name);
        Assert.Equal("yuris", project.LoadConfig().Engine.Id);
        Assert.Equal("v1", project.LoadVndbLink()?.VisualNovelId);
        Assert.Equal(GameLibrary.NormalizeFolder(projectPath), File.Load().Find("g1").ProjectPath);
        Assert.Contains("Jogo do VNDB: Um (v1)", result.Output, StringComparison.Ordinal);
        Assert.Contains("Próximo passo: vnf extract --project", result.Output, StringComparison.Ordinal);
        Assert.Contains("GetVisualNovelAsync(v1)", fake.Calls);

        var queue = await Run("library", "queue", "--offline", "--all");
        Assert.Contains("com projeto", queue.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traduzir_sem_pasta_usa_o_titulo_do_jogo_na_pasta_atual()
    {
        var gameDir = NewGameDir();
        Prepare(Game("g1", "Fate/stay night", "v1", YuRis, folder: gameDir));

        var result = await Run("library", "translate", "g1", "--name", "Fate em português");

        Assert.Equal(0, result.ExitCode);
        var config = ProjectFolder.Open(Path.Combine(_cli.WorkingDirectory, "Fate stay night")).LoadConfig();
        Assert.Equal("Fate em português", config.Name);
    }

    [Fact]
    public async Task Traduzir_jogo_sem_casamento_cria_o_projeto_e_avisa_que_ficou_sem_ficha()
    {
        var gameDir = NewGameDir();
        Prepare(Game("g1", "Um", null, YuRis, folder: gameDir));

        var result = await Run("library", "translate", "Um", "sem-ficha");

        Assert.Equal(0, result.ExitCode);
        Assert.Null(ProjectFolder.Open(Path.Combine(_cli.WorkingDirectory, "sem-ficha")).LoadVndbLink());
        Assert.Contains("não tem o jogo do VNDB confirmado", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traduzir_recusa_engine_sem_suporte_e_jogo_que_ja_tem_projeto()
    {
        Prepare(Game("g1", "Um", "v1", KiriKiri), Game("g2", "Dois", "v2", YuRis) with { ProjectPath = "C:\\Projetos\\dois" });

        var unsupported = await Run("library", "translate", "Um", "um");
        var started = await Run("library", "translate", "Dois", "dois");

        Assert.NotEqual(0, unsupported.ExitCode);
        Assert.Contains("KiriKiri", unsupported.Error, StringComparison.Ordinal);
        Assert.NotEqual(0, started.ExitCode);
        Assert.Contains("já tem o projeto", started.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_cli.WorkingDirectory, "um")));
    }

    [Fact]
    public async Task Traduzir_com_o_jogo_fora_da_biblioteca_nao_diz_que_ligou_e_aponta_onde_o_projeto_ficou()
    {
        var gameDir = NewGameDir();
        var fake = Prepare(Game("g1", "Um", "v1", YuRis, folder: gameDir));
        _cli.Cli.VndbCatalogFactory = () => new RemovesGameOnLookup(fake, () => File.Save(new GameLibrary([])));

        var result = await Run("library", "translate", "Um", "meu-projeto");

        var projectPath = Path.Combine(_cli.WorkingDirectory, "meu-projeto");
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(System.IO.File.Exists(Path.Combine(projectPath, ProjectFolder.ConfigFileName)));
        Assert.DoesNotContain("ficou ligado ao projeto", result.Output, StringComparison.Ordinal);
        Assert.Contains(projectPath, result.Error, StringComparison.Ordinal);
        Assert.Contains("o jogo saiu da biblioteca", result.Error, StringComparison.Ordinal);
        Assert.Contains($"vnf library link g1 --project \"{projectPath}\"", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.Load().Games);
    }

    private sealed class RemovesGameOnLookup(IVndbCatalog inner, Action onLookup) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) =>
            inner.SearchVisualNovelsAsync(title, limit, ct);

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default)
        {
            onLookup();
            return inner.GetVisualNovelAsync(id, ct);
        }

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) =>
            inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) =>
            inner.SearchCharactersByTitleAsync(title, ct);

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) =>
            inner.GetReleasesAsync(visualNovelId, ct);
    }
}
