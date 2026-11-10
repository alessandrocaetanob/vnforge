using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// A área Personagens (d11, #558) com o catálogo falso: três jogos inventados do VNDB, dois na Biblioteca e um fora. Hana está nos três, Mika tem papel e spoiler diferentes por edição
/// no jogo 1, Kai só aparece no jogo de fora e Fantasma só em jogo com spoiler grande.
/// </summary>
public sealed class CharactersViewModelTests : IDisposable
{
    private const string Moon = "v900101";
    private const string Star = "v900102";
    private const string Outside = "v900103";

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _config = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _config.Dispose();

    private static VndbTrait LongHair => new("i1", "Long Hair", "i10", "Hair", 0, false, Sexual: false);

    private static VndbTrait Ghost => new("i2", "Ghost", "i20", "Personality", 2, false, Sexual: false);

    private static VndbVisualNovel Game(string id, string title) =>
        new(id, title, null, [], [], "ja", 2, "2020-01-01", ["ja"], ["win"], null, 3, 1200, 10, "Texto.", []);

    private static VndbCharacter Character(string id, string name, string? original, string? gender, VndbCharacterAppearance[] appearances, VndbTrait[] traits) =>
        new(id, name, original, name == "Hana Tsukino" ? ["Hanako"] : [], $"{name}.", null, gender is null ? null : new VndbGender(gender, gender), appearances, traits);

    private static FakeVndbCatalog Catalog() => new(
        [Game(Moon, "Moon Flower"), Game(Star, "Star Garden"), Game(Outside, "Outside Game")],
        [
            Character("c101", "Hana Tsukino", "月野 花", "f", [new(Moon, "main", 0, null), new(Star, "side", 0, null), new(Outside, "primary", 0, null)], [LongHair, Ghost]),
            Character("c102", "Mika Aoba", null, "f", [new(Moon, "main", 2, "r2"), new(Moon, "side", 0, "r1")], [LongHair]),
            Character("c103", "Kai Ono", null, "m", [new(Outside, "main", 0, null)], []),
            Character("c104", "Sora Mori", null, "m", [new(Star, "side", 0, null)], []),
            Character("c105", "Fantasma", null, null, [new(Star, "side", 2, null)], []),
        ]);

    private static LibraryGame Owned(string id, string folder, string vndbId, string title) =>
        new(id, $"C:\\Jogos\\{folder}", null, LibraryEngine.Unknown, title, null, Now) { VndbId = vndbId, Confirmation = LibraryConfirmation.Manual };

    /// <summary>Grava as fichas dos três jogos e uma Biblioteca com os dois primeiros; <paramref name="matched"/> casa a edição r1 com a pasta do jogo 1.</summary>
    private async Task<CharactersViewModel> OpenAsync(bool matched = false, bool showSpoilers = false)
    {
        var store = new SheetStore(_config.Path);
        foreach (var id in new[] { Moon, Star, Outside })
        {
            await SheetFetcher.FetchAsync(Catalog(), store, id, new FixedClock(Now), Ct);
        }

        new LibraryFile(_config.Path).Save(new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g2", "Star Garden", Star, "Star Garden")])
        {
            Privacy = new LibraryPrivacy(ShowSpoilers: showSpoilers),
        });
        if (matched)
        {
            new GameEditionsFile(_config.Path).Update(e => e.Set("g1", Moon, ["r1"], Now));
        }

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
        Assert.Equal("Papel mais importante entre as edições · spoiler nível 2", game.Detail);
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
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(temp.SubFolder("vn")), new FixedClock(Now)));
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
}
