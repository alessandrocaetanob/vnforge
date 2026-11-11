using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>#536: o passo "Ficha do VNDB" do assistente, com o catálogo falso (nunca a API).</summary>
public sealed class NewProjectWizardVndbTests : IDisposable
{
    private const string Title = "雨音の箱庭";

    private readonly TempFolder temp = new();
    private readonly string game;

    public NewProjectWizardVndbTests()
    {
        game = temp.SubFolder("Hakoniwa");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
    }

    public void Dispose() => temp.Dispose();

    /// <summary>Um VNDB que não responde: os personagens ficam esperando até o pedido ser cancelado.</summary>
    private sealed class HangingCatalog : IVndbCatalog
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Searched { get; private set; }

        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
        {
            Searched = true;
            return Task.FromResult<IReadOnlyList<VndbVisualNovel>>([]);
        }

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => Task.FromResult<VndbVisualNovel?>(Vn(id, "Amaoto no Hakoniwa", Title));

        public async Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
        {
            Asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new VndbResults<VndbCharacter>([], false);
        }

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VndbVisualNovel Vn(string id, string title, string original) =>
        new(id, title, original, [], [], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 10, "Numa [b]cidade[/b] onde chove.", []);

    private static VndbCharacter Character(string id, string name, string original, string gender) =>
        new(id, name, original, ["Apelido"], null, null, new VndbGender(gender, gender), [new VndbCharacterAppearance("v1", "main", 0, null)], []);

    private static FakeVndbCatalog Catalog(params VndbVisualNovel[] games) =>
        new(games, [Character("c1", "Kenta Aizawa", "相沢 健太", "m"), Character("c2", "Aoi Sakura", "佐倉 葵", "f"), Character("c3", "Sem gênero", "誰某", "x")]);

    private static NewProjectWizardViewModel Create(IVndbCatalog? catalog, string? title = Title, Func<GameInspection, string, CancellationToken, IReadOnlyList<string>?>? gameNames = null) =>
        new(new FakeFolderPicker(null), [new TitledEngine(title)], vndb: catalog, gameCharacterNames: gameNames ?? ((_, _, _) => null));

    private async Task<NewProjectWizardViewModel> AtVndbStepAsync(IVndbCatalog? catalog, string? title = Title, Func<GameInspection, string, CancellationToken, IReadOnlyList<string>?>? gameNames = null)
    {
        var vm = Create(catalog, title, gameNames);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(WizardStep.Vndb, vm.Step);
        return vm;
    }

    [Fact]
    public async Task Entrar_no_passo_busca_pelo_titulo_lido_do_jogo_e_pre_marca_o_que_combina_com_a_pasta()
    {
        var catalog = Catalog(Vn("v1", "Amaoto no Hakoniwa", Title), Vn("v2", "Amaoto no Hakoniwa: Fan Disc", "雨音の箱庭 ファンディスク"));

        var vm = await AtVndbStepAsync(catalog);

        Assert.Equal(VndbStage.Choosing, vm.Stage);
        Assert.Equal("Achamos 2 jogos com este título no VNDB", vm.VndbHeadline);
        Assert.Contains($"Título lido do jogo (yscfg.ybn): {Title}.", vm.VndbDetail, StringComparison.Ordinal);
        Assert.Equal(["Search(雨音の箱庭)"], catalog.Calls.Select(c => c.Replace("SearchVisualNovelsAsync", "Search")));
        Assert.Equal(["Amaoto no Hakoniwa", "Amaoto no Hakoniwa: Fan Disc"], vm.Candidates.Select(c => c.Title));
        Assert.Equal(["Combina com a pasta e o exe", "Combina com a pasta e o exe"], vm.Candidates.Select(c => c.EvidenceLabel));
        Assert.True(vm.Candidates[0].IsSelected);
        Assert.Equal("2019 · Média · 18 h · Yu-RIS", vm.Candidates[0].Details);
    }

    [Fact]
    public async Task Jogo_da_Biblioteca_com_casamento_confirmado_traz_a_ficha_direto_sem_buscar_e_Voltar_oferece_a_busca()
    {
        var catalog = Catalog(Vn("v1", "Amaoto no Hakoniwa", Title), Vn("v2", "Outro", "別の"));
        var vm = Create(catalog);
        await vm.SetGameFolderAsync(game, "v1");
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Vndb, vm.Step);
        Assert.Equal(VndbStage.Preview, vm.Stage);
        Assert.Equal("v1", vm.ConfirmedVndb?.VisualNovelId);
        Assert.Equal("Amaoto no Hakoniwa, v1", vm.Preview!.Label);
        Assert.DoesNotContain(catalog.Calls, c => c.Contains("Search", StringComparison.Ordinal));

        vm.BackCommand.Execute(null);

        Assert.Equal(VndbStage.Empty, vm.Stage);
        Assert.Null(vm.ConfirmedVndb);
        Assert.Equal(WizardStep.Vndb, vm.Step);
    }

    [Fact]
    public async Task Com_os_nomes_do_jogo_a_ficha_mostra_onde_cada_personagem_aparece_e_fecha_com_os_que_o_VNDB_nao_tem()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)), gameNames: (_, _, _) => ["相沢 健太", "名無し 08", "佐倉 葵"]);

        await vm.NextCommand.ExecuteAsync(null);

        var preview = vm.Preview!;
        Assert.Equal("3 do VNDB, 2 casados com os nomes do jogo (ES.CHAR.NAME)", preview.CountText);
        Assert.Equal(["相沢 健太", "佐倉 葵", "名無し 08"], preview.Characters.Select(c => c.Original));
        Assert.Equal(["ES.CHAR.NAME 1", "ES.CHAR.NAME 3", "sem par no VNDB"], preview.Characters.Select(c => c.InGame));
        Assert.Equal(["Masculino", "Feminino", "Sem gênero"], preview.Characters.Select(c => c.Gender));
        Assert.Equal("O aviso “3 personagens sem gênero” cai para 1, o da personagem que o VNDB não tem.", preview.GenderNote);
    }

    [Fact]
    public async Task O_nome_em_destaque_vem_na_ordem_ocidental_e_o_original_fica_abaixo_so_quando_existe()
    {
        var catalog = new FakeVndbCatalog(
            [Vn("v1", "Amaoto no Hakoniwa", Title)],
            [Character("c1", "Aizawa Kenta", "相沢 健太", "m"), Character("c2", "John Smith", "ジョン・スミス", "m"), Character("c3", "Sakura", "桜", "f")]);
        var vm = await AtVndbStepAsync(catalog);

        await vm.NextCommand.ExecuteAsync(null);

        var rows = vm.Preview!.Characters;
        Assert.Equal(["Kenta Aizawa", "John Smith", "Sakura"], rows.Select(c => c.Display));
        Assert.Equal(["Aizawa Kenta", "John Smith", "Sakura"], rows.Select(c => c.Romaji));
        Assert.All(rows, c => Assert.True(c.HasOriginal));
    }

    [Fact]
    public async Task O_aviso_de_genero_conta_tambem_os_que_o_VNDB_traz_sem_genero()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)), gameNames: (_, _, _) => ["相沢 健太", "誰某", "名無し 08", "名無し 09"]);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal("O aviso “4 personagens sem gênero” cai para 3: os que o VNDB não tem e os que ele traz sem gênero.", vm.Preview!.GenderNote);
    }

    [Fact]
    public async Task Sem_como_ler_os_nomes_do_jogo_a_ficha_lista_os_do_VNDB_e_deixa_o_casamento_para_a_extracao()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)), gameNames: (_, _, _) => throw new VNForgeException("script ilegível"));

        await vm.NextCommand.ExecuteAsync(null);

        var preview = vm.Preview!;
        Assert.Equal(VndbStage.Preview, vm.Stage);
        Assert.StartsWith("3 do VNDB. Os que casam com os nomes do jogo", preview.CountText, StringComparison.Ordinal);
        Assert.Equal(["相沢 健太", "佐倉 葵", "誰某"], preview.Characters.Select(c => c.Original));
        Assert.All(preview.Characters, c => Assert.Equal(string.Empty, c.InGame));
    }

    [Fact]
    public async Task Voltar_da_confirmacao_volta_para_a_lista_e_um_segundo_Avancar_segue_para_o_nome()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)));
        await vm.NextCommand.ExecuteAsync(null);

        vm.BackCommand.Execute(null);

        Assert.Equal((WizardStep.Vndb, VndbStage.Choosing), (vm.Step, vm.Stage));
        Assert.Null(vm.Preview);

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.Equal("Jogo e personagens", vm.Steps[1].Subtitle);
    }

    [Fact]
    public async Task Sem_resultado_avisa_e_deixa_buscar_por_outro_titulo_ou_seguir()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)), title: "Titulo que ninguem tem");

        Assert.Equal(VndbStage.Empty, vm.Stage);
        Assert.Equal("Não achamos este título no VNDB", vm.VndbHeadline);
        Assert.True(vm.NextCommand.CanExecute(null));

        vm.SearchTitle = "Amaoto";
        await vm.SearchVndbCommand.ExecuteAsync(null);

        Assert.Equal(VndbStage.Choosing, vm.Stage);
        Assert.Contains("Título digitado: Amaoto.", vm.VndbDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VNDB_fora_do_ar_avisa_em_portugues_e_deixa_seguir_sem_ele()
    {
        var vm = await AtVndbStepAsync(new FailingCatalog());

        Assert.Equal(VndbStage.Failed, vm.Stage);
        Assert.Equal("O VNDB não respondeu", vm.VndbHeadline);
        Assert.Contains("sem rede", vm.VndbDetail, StringComparison.Ordinal);
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.True(vm.SkipVndbCommand.CanExecute(null));
    }

    [Fact]
    public async Task Trocar_a_pasta_do_jogo_refaz_a_busca()
    {
        var catalog = Catalog(Vn("v1", "Amaoto no Hakoniwa", Title));
        var vm = await AtVndbStepAsync(catalog);
        vm.BackCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Single(catalog.Calls);

        vm.BackCommand.Execute(null);
        var other = temp.SubFolder("Outro");
        File.WriteAllText(Path.Combine(other, "Jogo.exe"), "MZ");
        await vm.SetGameFolderAsync(other);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(2, catalog.Calls.Count);
    }

    [Fact]
    public async Task Com_a_sinopse_traduzida_pela_IA_a_ficha_mostra_a_traducao_e_o_selo()
    {
        var vm = new NewProjectWizardViewModel(
            new FakeFolderPicker(null),
            [new TitledEngine(Title)],
            vndb: Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)),
            gameCharacterNames: (_, _, _) => null,
            translatedSynopsis: link => link.VisualNovelId == "v1" ? "  Numa cidade onde chove quase sempre.  " : null);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        await vm.NextCommand.ExecuteAsync(null);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.True(vm.Preview!.IsSynopsisTranslated);
        Assert.True(vm.Preview.HasSynopsis);
        Assert.Equal("Numa cidade onde chove quase sempre.", vm.Preview.Synopsis);
    }

    [Fact]
    public async Task Seguir_sem_VNDB_com_a_busca_em_andamento_cancela_o_pedido_e_libera_o_assistente()
    {
        var catalog = new BlockingCatalog();
        var vm = Create(catalog);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        var next = vm.NextCommand.ExecuteAsync(null);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(VndbStage.Searching, vm.Stage);
        Assert.True(vm.SkipVndbCommand.CanExecute(null));
        Assert.True(vm.SearchVndbCommand.CanExecute(null));

        vm.SkipVndbCommand.Execute(null);
        await next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(catalog.Token.IsCancellationRequested);
        Assert.Equal((WizardStep.Names, VndbStage.Skipped, false), (vm.Step, vm.Stage, vm.IsBusy));
        Assert.Null(vm.BuildOptions().Vndb);

        vm.BackCommand.Execute(null);

        Assert.Equal((VndbStage.Empty, "A busca foi interrompida"), (vm.Stage, vm.VndbHeadline));
    }

    [Fact]
    public async Task Seguir_sem_VNDB_enquanto_traz_os_personagens_cancela_esse_pedido_tambem()
    {
        var catalog = new BlockingCatalog(blockCharacters: true, Vn("v1", "Amaoto no Hakoniwa", Title));
        var vm = Create(catalog);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        await vm.NextCommand.ExecuteAsync(null);
        var confirm = vm.NextCommand.ExecuteAsync(null);
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(VndbStage.Confirming, vm.Stage);

        vm.SkipVndbCommand.Execute(null);
        await confirm.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(catalog.Token.IsCancellationRequested);
        Assert.Equal((WizardStep.Names, false), (vm.Step, vm.IsBusy));
        Assert.Null(vm.BuildOptions().Vndb);

        vm.BackCommand.Execute(null);

        Assert.Equal(VndbStage.Choosing, vm.Stage);
        Assert.Equal("Achamos 1 jogo com este título no VNDB", vm.VndbHeadline);
    }

    private sealed class BlockingCatalog(bool blockCharacters = false, params VndbVisualNovel[] games) : IVndbCatalog
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public async Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default)
        {
            if (blockCharacters)
            {
                return games;
            }

            Token = ct;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return games;
        }

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default)
        {
            Token = ct;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new VndbResults<VndbCharacter>([], false);
        }

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FailingCatalog : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) =>
            throw new VndbException("O VNDB está sem rede.");

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TitledEngine(string? title) : IEngine, IGameTitleSource
    {
        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece titled");

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameTitleFile? ReadTitle(string unpackedDirectory) => null;

        public string? ReadTitleFromGame(string gameDir) => title;

        public byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder) => [];
    }
}
