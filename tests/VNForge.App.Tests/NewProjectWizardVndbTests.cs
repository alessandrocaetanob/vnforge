using VNForge.App.ViewModels;
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

    private static VndbVisualNovel Vn(string id, string title, string original) =>
        new(id, title, original, [], [], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 10, "Numa [b]cidade[/b] onde chove.", []);

    private static VndbCharacter Character(string id, string name, string original, string gender) =>
        new(id, name, original, ["Apelido"], null, null, new VndbGender(gender, gender), [new VndbCharacterAppearance("v1", "main", 0, null)], []);

    private static FakeVndbCatalog Catalog(params VndbVisualNovel[] games) =>
        new(games, [Character("c1", "Kenta Aizawa", "相沢 健太", "m"), Character("c2", "Aoi Sakura", "佐倉 葵", "f"), Character("c3", "Sem gênero", "誰某", "x")]);

    private NewProjectWizardViewModel Create(IVndbCatalog? catalog, string? title = Title) =>
        new(new FakeFolderPicker(null), [new TitledEngine(title)], vndb: catalog);

    private async Task<NewProjectWizardViewModel> AtVndbStepAsync(IVndbCatalog? catalog, string? title = Title)
    {
        var vm = Create(catalog, title);
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
        Assert.Equal("2019 · Média · 18 h", vm.Candidates[0].Details);
    }

    [Fact]
    public async Task Sem_resultado_que_combine_com_a_pasta_nada_vem_marcado_e_avancar_espera_a_escolha()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Outro titulo", "雨音の箱庭 外伝")));

        Assert.Equal("Só o título", vm.Candidates[0].EvidenceLabel);
        Assert.DoesNotContain(vm.Candidates, c => c.IsSelected);
        Assert.False(vm.NextCommand.CanExecute(null));

        vm.Candidates[0].IsSelected = true;

        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Marcar_um_resultado_desmarca_o_outro()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Um", Title), Vn("v2", "Dois", Title)));

        vm.Candidates[1].IsSelected = true;

        Assert.Equal([false, true], vm.Candidates.Select(c => c.IsSelected));
    }

    [Fact]
    public async Task Confirmar_mostra_os_personagens_e_a_sinopse_sem_a_marcacao_do_VNDB_e_continua_no_passo()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)));

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Vndb, vm.Step);
        Assert.Equal(VndbStage.Preview, vm.Stage);
        var preview = vm.Preview!;
        Assert.Equal("Amaoto no Hakoniwa, v1", preview.Label);
        Assert.Equal("Numa cidade onde chove.", preview.Synopsis);
        Assert.Equal(["相沢 健太", "佐倉 葵", "誰某"], preview.Characters.Select(c => c.Original));
        Assert.Equal(["Masculino", "Feminino", "Sem gênero"], preview.Characters.Select(c => c.Gender));
        Assert.Equal("Protagonista", preview.Characters[0].Role);
        Assert.Contains("2 dos 3 personagens", preview.GenderNote, StringComparison.Ordinal);
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
        Assert.Equal("Amaoto no Hakoniwa", vm.Steps[1].Subtitle);
    }

    [Fact]
    public async Task O_jogo_confirmado_vai_nas_opcoes_do_projeto_e_o_projeto_nasce_com_o_vndb_json()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)));
        await vm.NextCommand.ExecuteAsync(null);

        var link = vm.BuildOptions().Vndb;

        Assert.Equal("v1", link?.VisualNovelId);
        Assert.Equal(3, link?.Characters.Count);
        Assert.Equal(CharacterGender.Male, link?.Characters[0].Gender);
    }

    [Fact]
    public async Task Nenhum_deles_segue_sem_vndb_e_o_projeto_nasce_sem_a_ligacao()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)));

        vm.SkipVndbCommand.Execute(null);

        Assert.Equal(WizardStep.Names, vm.Step);
        Assert.Null(vm.BuildOptions().Vndb);
        Assert.Equal("Sem VNDB", vm.Steps[1].Subtitle);

        vm.BackCommand.Execute(null);

        Assert.Equal(VndbStage.Choosing, vm.Stage);
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
    public async Task Jogo_sem_titulo_lido_busca_pelo_nome_da_pasta()
    {
        var catalog = Catalog(Vn("v1", "Hakoniwa Story", Title));

        var vm = await AtVndbStepAsync(catalog, title: null);

        Assert.Equal(["Search(Hakoniwa)"], catalog.Calls.Select(c => c.Replace("SearchVisualNovelsAsync", "Search")));
        Assert.Contains("Nome da pasta do jogo: Hakoniwa.", vm.VndbDetail, StringComparison.Ordinal);
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
    public async Task Sem_catalogo_o_passo_diz_que_nao_ha_busca_e_deixa_seguir()
    {
        var vm = await AtVndbStepAsync(null);

        Assert.Equal(VndbStage.Unavailable, vm.Stage);
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.Null(vm.BuildOptions().Vndb);
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
    public async Task Os_textos_do_passo_nao_tem_travessao()
    {
        var vm = await AtVndbStepAsync(Catalog(Vn("v1", "Amaoto no Hakoniwa", Title)));
        await vm.NextCommand.ExecuteAsync(null);
        var preview = vm.Preview!;

        Assert.All(
            new[] { vm.VndbHeadline, vm.VndbDetail, preview.CountText, preview.SynopsisNote, preview.GenderNote },
            text => Assert.DoesNotMatch("[—–]", text));
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
