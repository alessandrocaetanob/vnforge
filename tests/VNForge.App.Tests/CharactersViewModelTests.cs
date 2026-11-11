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

    [Fact]
    public async Task Projeto_com_a_pasta_ausente_avisa_e_nao_impede_o_andamento_e_o_cartao_dos_outros()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        var root = SampleProjects.Create(Path.Combine(_config.Path, "projeto"), "Star Garden", TranslationStatus.Approved, TranslationStatus.Untranslated);
        var project = ProjectFolder.Open(root);
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select(u => u with { Speaker = "花" })]);
        project.SaveCharacters([new CharacterEntry("花", "Hana", CharacterGender.Female, VndbId: "c101")]);
        new LibraryFile(_config.Path).Update(l =>
        {
            l.Replace(l.Find("g1") with { ProjectPath = Path.Combine(_config.Path, "sumiu") });
            l.Replace(l.Find("g2") with { ProjectPath = root });
        });
        var notices = new List<(string Text, ToastKind Kind)>();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, Notify = (text, kind) => notices.Add((text, kind)) };

        vm.Open("c101");
        await vm.LoadAsync();

        Assert.Equal(new CharacterTranslation("Feminino", "花", "2 em Star Garden"), vm.Page!.Translation);
        Assert.Equal(0.5, vm.Page.Games.Single(g => g.GameId == "g2").Progress);
        var notice = Assert.Single(notices);
        Assert.Equal(ToastKind.Warning, notice.Kind);
        Assert.Contains("Moon Flower", notice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personagem_escondido_por_spoiler_nao_dispara_a_leitura_dos_projetos()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        new LibraryFile(_config.Path).Update(l => l.Replace(l.Find("g2") with { ProjectPath = Path.Combine(_config.Path, "sumiu") }));
        var notices = new List<(string Text, ToastKind Kind)>();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, Notify = (text, kind) => notices.Add((text, kind)) };
        await vm.LoadAsync();
        vm.ShowSpoilers = true;
        var ghost = vm.Items.Single(i => i.Name == "Fantasma");
        vm.ShowSpoilers = false;

        var before = vm.ExtrasLoading;

        vm.OpenCharacterCommand.Execute(ghost);
        await vm.ExtrasLoading;

        Assert.Same(before, vm.ExtrasLoading);

        Assert.Null(vm.Page);
        Assert.Contains("spoiler escondido", vm.Notice, StringComparison.Ordinal);
        Assert.Empty(notices);
    }

    [Fact]
    public async Task Caminho_de_projeto_invalido_vira_aviso_uma_vez_so_e_nao_deixa_a_pagina_muda()
    {
        await CharactersSamples.SeedAsync(_config.Path, ct: Ct);
        new LibraryFile(_config.Path).Update(l => l.Replace(l.Find("g1") with { ProjectPath = "projeto\0ruim" }));
        var notices = new List<(string Text, ToastKind Kind)>();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, Notify = (text, kind) => notices.Add((text, kind)) };

        vm.Open("c101");
        await vm.LoadAsync();

        Assert.NotNull(vm.Page);
        Assert.Contains("Moon Flower", Assert.Single(notices).Text, StringComparison.Ordinal);

        vm.Open("c101");
        await vm.LoadAsync();
        vm.BackCommand.Execute(null);
        vm.OpenCharacterCommand.Execute(vm.Items.Single(i => i.Name == "Hana Tsukino"));
        await vm.ExtrasLoading;

        Assert.Single(notices);
    }

    [Fact]
    public async Task O_retrato_adulto_tem_uma_chave_so_na_lista_e_na_pagina_e_so_o_jogo_dela_o_revela()
    {
        await CharactersSamples.SeedAsync(_config.Path, matched: true, showSpoilers: true, Ct, adultPortrait: true);
        var reveals = new AdultReveal();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path, Reveals = reveals };
        await vm.LoadAsync();
        vm.RoleFacets.Single(f => f.Key == "side").IsSelected = true;
        var hana = vm.Items.Single(i => i.Name == "Hana Tsukino");

        // Com o filtro "Secundária" a lista mostra o papel do jogo Star, mas o retrato continua com a chave do papel mais importante.
        Assert.Equal(CharactersSamples.Moon, hana.Portrait.RevealKey);
        Assert.True(hana.Portrait.IsBlurred);
        vm.OpenCharacterCommand.Execute(hana);
        Assert.Equal(CharactersSamples.Moon, vm.Page!.Portrait.RevealKey);

        reveals.Reveal(CharactersSamples.Star);

        Assert.False(vm.Page!.Portrait.Revealed);
        Assert.True(vm.Page.Portrait.IsBlurred);
        Assert.True(vm.Page.Games.Single(g => g.Title == "Star Garden").Revealed);
        Assert.Contains(vm.Page.Tags, c => c.IsHidden);
        Assert.DoesNotContain(vm.TraitFacets, f => f.Label == "Nude");

        vm.RevealPortraitCommand.Execute(vm.Page.Portrait.RevealKey);

        Assert.True(reveals.IsRevealed(CharactersSamples.Moon));
        Assert.True(vm.Page!.Portrait.Revealed);
        Assert.False(vm.Page.Portrait.IsBlurred);
        Assert.DoesNotContain(vm.Page.Tags, c => c.IsHidden);
        Assert.DoesNotContain(vm.Page.Groups, g => g.Chips.Any(c => c.IsHidden));
        Assert.Contains(vm.Page.Groups, g => g.Chips.Any(c => c.Text == "Nude"));
        Assert.Contains(vm.TraitFacets, f => f.Label == "Nude");

        // Borrar o jogo de novo esconde o traço outra vez, e o filtro por ele cai junto em vez de deixar a lista filtrada por um traço invisível.
        vm.TraitFacets.Single(f => f.Label == "Nude").IsSelected = true;
        Assert.Equal(["Hana Tsukino"], vm.Items.Select(i => i.Name));
        reveals.Conceal(CharactersSamples.Moon);

        Assert.Empty(vm.ActiveTraits);
        Assert.DoesNotContain(vm.TraitFacets, f => f.Label == "Nude");
        Assert.Contains(vm.Items, i => i.Name == "Hana Tsukino");
        vm.RevealPortraitCommand.Execute(string.Empty);
        vm.RevealPortraitCommand.Execute(null);
        Assert.False(reveals.IsRevealed(string.Empty));
    }

    [Fact]
    public async Task Ordenar_por_mais_jogos_e_por_papel_reordena_a_mesma_lista()
    {
        var vm = await OpenAsync(showSpoilers: true);
        string[] porNome = [.. vm.Items.Select(i => i.Name)];

        vm.SelectedSort = vm.SortOptions.Single(o => o.Sort == CharacterSort.MostGames);
        string[] porJogos = [.. vm.Items.Select(i => i.Name)];
        vm.SelectedSort = vm.SortOptions.Single(o => o.Sort == CharacterSort.Role);
        string[] porPapel = [.. vm.Items.Select(i => i.Name)];

        Assert.Equal("Hana Tsukino", porJogos[0]);
        Assert.Equal(porNome.Order(), porJogos.Order());
        Assert.Equal(porNome.Order(), porPapel.Order());
        Assert.Equal("Hana Tsukino", porPapel[0]);
    }

    [Fact]
    public async Task Buscar_um_traco_e_ver_todos_os_tracos_refazem_a_lista_de_tracos()
    {
        var vm = await OpenAsync(showSpoilers: true);

        vm.ShowAllTraits = true;
        Assert.Equal("Ver menos", vm.ShowAllTraitsText);
        var todos = vm.TraitFacets.Count;
        vm.TraitSearch = "zzzz-nenhum-traco";

        Assert.True(vm.TraitFacets.Count <= todos);
        Assert.Empty(vm.TraitFacets);
    }
}
