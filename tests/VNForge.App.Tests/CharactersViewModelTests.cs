using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Factsheet;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// A área Personagens (d11, #558) com o catálogo falso: três jogos inventados do VNDB, dois na Biblioteca e um fora. Hana está nos três, Mika tem papel e spoiler diferentes por edição
/// no jogo 1, Kai só aparece no jogo de fora e Fantasma só em jogo com spoiler grande.
/// </summary>
public sealed class CharactersViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _config.Dispose();

    private async Task<CharactersViewModel> OpenAsync(bool matched = false, bool showSpoilers = false)
    {
        await CharactersSamples.SeedAsync(_config.Path, matched, showSpoilers, Ct);
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task A_lista_traz_so_os_personagens_dos_jogos_da_Biblioteca_e_esconde_o_spoiler_por_padrao()
    {
        var vm = await OpenAsync();

        Assert.Equal(["Hana Tsukino", "Sora Mori"], vm.Items.Select(i => i.Name));
        Assert.Contains("2 escondidos por spoiler", vm.Subtitle, StringComparison.Ordinal);
        Assert.Equal("2 personagens de 2 jogos", vm.StatusText);

        vm.ShowSpoilers = true;

        Assert.Equal(["Fantasma", "Hana Tsukino", "Mika Aoba", "Sora Mori"], vm.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task A_pagina_do_personagem_mostra_os_jogos_da_Biblioteca_com_o_papel_em_cada_um_e_deixa_de_fora_o_terceiro()
    {
        var vm = await OpenAsync();
        vm.OpenCharacterCommand.Execute(vm.Items.Single(i => i.Name == "Hana Tsukino"));

        var page = vm.Page!;

        Assert.Equal("月野 花 · Hana Tsukino", page.NamesLine);
        Assert.Equal("Também chamado de Hanako", page.AliasesLine);
        Assert.Equal(["Moon Flower", "Star Garden"], page.Games.Select(g => g.Title));
        Assert.Equal(["Protagonista", "Secundária"], page.Games.Select(g => g.Role.Text));
        Assert.Equal("2 jogos da sua Biblioteca", page.GamesCount);
        Assert.Contains("1 jogo do VNDB que não está na sua Biblioteca", page.Note, StringComparison.Ordinal);
        Assert.Contains(page.Tags, c => c is { IsHidden: true } && c.Text.StartsWith("1 traço escondido", StringComparison.Ordinal));
        Assert.Equal("Hana Tsukino", vm.OpenName);
        Assert.Contains(page.Tags, c => c.Text == "17 anos");
    }

    [Fact]
    public async Task Com_a_edicao_casada_com_a_pasta_vale_o_papel_dela_e_o_maior_spoiler()
    {
        var vm = await OpenAsync(matched: true, showSpoilers: true);
        vm.OpenCharacterCommand.Execute(vm.Items.Single(i => i.Name == "Mika Aoba"));

        var game = Assert.Single(vm.Page!.Games);

        Assert.Equal("Secundária", game.Role.Text);
        Assert.Equal("Edição casada com a sua pasta · spoiler nível 2", game.Detail);
    }

    [Fact]
    public async Task Sem_edicao_casada_vale_o_papel_mais_importante_e_o_maior_spoiler()
    {
        var vm = await OpenAsync(showSpoilers: true);
        vm.OpenCharacterCommand.Execute(vm.Items.Single(i => i.Name == "Mika Aoba"));

        var game = Assert.Single(vm.Page!.Games);

        Assert.Equal("Protagonista", game.Role.Text);
        Assert.Equal("Papel mais importante · spoiler nível 2", game.Detail);
    }

    [Fact]
    public async Task A_busca_e_o_filtro_por_traco_e_por_papel_estreitam_a_grade_e_Limpar_volta_a_tudo()
    {
        var vm = await OpenAsync(showSpoilers: true);

        vm.TraitFacets.Single(f => f.Label == "Long Hair").IsSelected = true;
        Assert.Equal(["Hana Tsukino", "Mika Aoba"], vm.Items.Select(i => i.Name));
        Assert.Equal(["Long Hair"], vm.ActiveTraits.Select(f => f.Label));

        vm.RoleFacets.Single(f => f.Key == "main").IsSelected = true;
        Assert.Equal(["Hana Tsukino", "Mika Aoba"], vm.Items.Select(i => i.Name));

        vm.SearchText = "hanako";
        Assert.Equal(["Hana Tsukino"], vm.Items.Select(i => i.Name));

        vm.ClearFilterCommand.Execute(null);
        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.HasFilter);
    }

    [Fact]
    public async Task Com_o_filtro_de_papel_o_cartao_mostra_o_papel_que_casou_com_o_filtro_e_nao_o_primeiro()
    {
        var vm = await OpenAsync();
        Assert.Equal("Protagonista", vm.Items.Single(i => i.Name == "Hana Tsukino").Role.Text);

        vm.RoleFacets.Single(f => f.Key == "side").IsSelected = true;

        Assert.Equal(["Hana Tsukino", "Sora Mori"], vm.Items.Select(i => i.Name));
        Assert.Equal(["Secundária", "Secundário"], vm.Items.Select(i => i.Role.Text));
    }

    [Fact]
    public async Task Uma_ficha_ilegivel_aparece_como_erro_na_tela_em_vez_de_deixar_a_lista_em_branco()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        await File.WriteAllTextAsync(new SheetStore(_config.Path).TermsPath, "{ não é json", Ct);
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };

        await vm.LoadAsync();

        Assert.Empty(vm.Items);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Equal(vm.ErrorMessage, vm.EmptyMessage);
    }

    [Fact]
    public async Task Abrir_um_jogo_da_pagina_pede_ao_shell_o_jogo_da_Biblioteca()
    {
        var vm = await OpenAsync();
        vm.OpenCharacterCommand.Execute(vm.Items.Single(i => i.Name == "Hana Tsukino"));
        LibraryGame? requested = null;
        vm.GameOpenRequested += (_, game) => requested = game;

        vm.OpenGameCommand.Execute(vm.Page!.Games[1]);

        Assert.Equal("g2", requested?.Id);
    }

    [Fact]
    public async Task Do_cartao_da_ficha_o_shell_leva_a_pagina_do_personagem_e_o_trilho_marca_Personagens()
    {
        await OpenAsync();
        using var temp = new TempFolder();
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(temp.SubFolder("vn")), new FixedClock(CharactersSamples.Now)));
        main.Characters.ConfigDirectory = _config.Path;

        main.Sheet.OpenCharacterCommand.Execute(new CharacterTile("c101", TileKind.Portrait, "Hana Tsukino", null, null, null, null, string.Empty, string.Empty, 0));
        await main.Characters.Loading;

        Assert.Same(main.Characters, main.CurrentPage);
        Assert.Equal("Personagens", main.SelectedTopNav?.Label);
        Assert.Equal("Personagens › Hana Tsukino", main.Breadcrumb);
        Assert.Equal(2, main.Characters.Page?.Games.Count);
    }

    [Fact]
    public async Task Um_personagem_so_com_spoiler_escondido_avisa_em_vez_de_abrir_uma_pagina_vazia()
    {
        await OpenAsync();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };

        vm.Open("c105");
        await vm.LoadAsync();

        Assert.Null(vm.Page);
        Assert.Contains("spoiler escondido", vm.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Buscar_do_VNDB_traz_os_personagens_dos_jogos_da_Biblioteca_e_avisa_quando_falha()
    {
        new LibraryFile(_config.Path).Save(new GameLibrary([
            CharactersSamples.Owned("g1", "Moon Flower", CharactersSamples.Moon, "Moon Flower"),
        ]));
        var notices = new List<(string Text, ToastKind Kind)>();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, CatalogFactory = CharactersSamples.Catalog, Clock = new FixedClock(CharactersSamples.Now), Notify = (text, kind) => notices.Add((text, kind)) };
        await vm.LoadAsync();
        Assert.Empty(vm.Items);

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Equal(["Hana Tsukino"], vm.Items.Select(i => i.Name));
        Assert.Equal(ToastKind.Success, Assert.Single(notices).Kind);

        notices.Clear();
        vm.CatalogFactory = () => throw new InvalidOperationException("sem rede");
        await vm.SyncCommand.ExecuteAsync(null);
        Assert.Equal(ToastKind.Error, Assert.Single(notices).Kind);
    }

    [Fact]
    public async Task O_cartao_Na_traducao_e_a_barra_de_andamento_aparecem_so_quando_o_jogo_tem_projeto()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };
        vm.Open("c101");
        await vm.LoadAsync();
        Assert.False(vm.Page!.HasTranslation);
        Assert.All(vm.Page.Games, g => Assert.False(g.HasProgress));

        var root = SampleProjects.Create(Path.Combine(_config.Path, "projeto"), "Moon Flower", TranslationStatus.Approved, TranslationStatus.Untranslated);
        var project = ProjectFolder.Open(root);
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select(u => u with { Speaker = "花" })]);
        project.SaveCharacters([new CharacterEntry("花", "Hana", CharacterGender.Female, VndbId: "c101")]);
        new LibraryFile(_config.Path).Update(l => l.Replace(l.Find("g1") with { ProjectPath = root }));
        vm.Open("c101");
        await vm.LoadAsync();

        var page = vm.Page!;
        Assert.Equal(new CharacterTranslation("Feminino", "花", "2 em Moon Flower"), page.Translation);
        Assert.Equal(0.5, page.Games[0].Progress);
        Assert.False(page.Games[1].HasProgress);

        // O cartão e o andamento são lidos uma vez ao abrir a página: mexer no filtro não volta ao disco.
        File.Delete(project.CharactersPath);
        vm.ShowSpoilers = true;
        Assert.Equal(new CharacterTranslation("Feminino", "花", "2 em Moon Flower"), vm.Page!.Translation);
    }

    [Fact]
    public async Task Buscar_do_VNDB_refaz_as_fichas_de_antes_da_idade_e_mantem_a_pagina_aberta_os_filtros_e_o_spoiler()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        var store = new SheetStore(_config.Path);
        foreach (var character in store.LoadAllCharacters())
        {
            store.SaveCharacter(character with { Age = null, AgeFetched = false });
        }

        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, CatalogFactory = CharactersSamples.Catalog, Clock = new FixedClock(CharactersSamples.Now) };
        await vm.LoadAsync();
        vm.ShowSpoilers = true;
        vm.SearchText = "hana";
        vm.OpenCharacterCommand.Execute(vm.Items.Single());
        await vm.ExtrasLoading;
        Assert.DoesNotContain(vm.Page!.Tags, c => c.Text == "17 anos");

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Contains(vm.Page!.Tags, c => c.Text == "17 anos");
        Assert.Equal("hana", vm.SearchText);
        Assert.True(vm.ShowSpoilers);
    }
}
