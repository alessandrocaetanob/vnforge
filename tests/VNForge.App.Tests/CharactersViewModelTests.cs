using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Factsheet;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// A área Personagens (d11, #558) com o catálogo falso: três jogos inventados do VNDB, dois na Biblioteca e um fora. Hana está nos três, Mika tem papel e spoiler diferentes por edição
/// no jogo 1, Kai só aparece no jogo de fora e Fantasma só em jogo com spoiler grande.
/// </summary>
[Trait("Risco", "R7")]
public sealed class CharactersViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _config.Dispose();

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

        Assert.Equal(new CharacterTranslation("Feminino", "花", "2"), vm.Page!.Translation);
        Assert.Equal(0.5, vm.Page.Games.Single(g => g.GameId == "g2").Progress);
        var notice = Assert.Single(notices);
        Assert.Equal(ToastKind.Warning, notice.Kind);
        Assert.Contains("Moon Flower", notice.Text, StringComparison.Ordinal);
    }

    private static readonly VndbTrait BlackHair = new("i4", "Black", "i10", "Hair", 0, false, Sexual: false);
    private static readonly VndbTrait BlackEyes = new("i109", "Black", "i11", "Eyes", 0, false, Sexual: false);

    /// <summary>Quatro personagens no jogo Moon: dois traços de nome "Black", um de cabelo (i4, em dois personagens) e um de olhos (i109, em três), e um grupo que o app não conhece.</summary>
    private async Task SeedHomonymsAsync()
    {
        var mystery = new VndbTrait("i77", "Glow", "i70", "Mystery", 0, false, Sexual: false);
        var catalog = new FakeVndbCatalog(
            [CharactersSamples.Game(CharactersSamples.Moon, "Moon Flower")],
            [
                CharactersSamples.Character("c201", "Ayu One", null, "f", [new(CharactersSamples.Moon, "main", 0, null)], [CharactersSamples.LongHair, BlackHair, BlackEyes, mystery]),
                CharactersSamples.Character("c202", "Bea Two", null, "f", [new(CharactersSamples.Moon, "side", 0, null)], [BlackHair, BlackEyes]),
                CharactersSamples.Character("c203", "Cai Three", null, "m", [new(CharactersSamples.Moon, "side", 0, null)], [BlackEyes]),
                CharactersSamples.Character("c204", "Dan Four", null, "m", [new(CharactersSamples.Moon, "side", 0, null)], []),
            ]);
        var store = new SheetStore(_config.Path);
        await SheetFetcher.FetchAsync(catalog, store, CharactersSamples.Moon, new FixedClock(CharactersSamples.Now), Ct);
        new LibraryFile(_config.Path).Save(new GameLibrary([CharactersSamples.Owned("g1", "Moon Flower", CharactersSamples.Moon, "Moon Flower")]));
    }

    [Fact]
    [Trait("Risco", "R6")]
    public async Task Dois_tracos_de_mesmo_nome_em_grupos_diferentes_ficam_separados_no_filtro_no_cartao_e_no_chip()
    {
        await SeedHomonymsAsync();
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };
        await vm.LoadAsync();

        Assert.Equal(["Cabelo", "Olhos", "Mystery"], vm.TraitSections.Select(s => s.Title));
        Assert.Equal([("Black", 2), ("Long Hair", 1)], vm.TraitSections[0].Facets.Select(f => (f.Label, f.Count)));
        var eyes = Assert.Single(vm.TraitSections[1].Facets);
        Assert.Equal(("Black", 3), (eyes.Label, eyes.Count));

        eyes.IsSelected = true;

        var chip = Assert.Single(vm.ActiveTraits);
        Assert.Equal("Olhos: Black", chip.ChipLabel);
        Assert.Equal(["c201", "c202", "c203"], vm.Items.Select(i => i.Id).Order(StringComparer.Ordinal));

        vm.Open("c201");
        await vm.LoadAsync();

        Assert.Equal(["Cabelo", "Olhos", "Mystery"], vm.Page!.Groups.Select(g => g.Name));
        Assert.Equal(["Long Hair", "Black"], vm.Page.Groups[0].Chips.Select(c => c.Text));
        Assert.Equal(["Black"], vm.Page.Groups[1].Chips.Select(c => c.Text));
    }

    [Fact]
    public async Task O_nome_do_grupo_vem_da_traducao_da_ficha_depois_do_dicionario_e_por_ultimo_do_ingles_do_VNDB()
    {
        await SeedHomonymsAsync();
        SheetEditor.Edit(new SheetStore(_config.Path), CharactersSamples.Moon, SheetField.OfGroup("i10"), "Cabelos");
        var vm = new CharactersViewModel { ConfigDirectory = _config.Path };

        await vm.LoadAsync();

        Assert.Equal(["Cabelos", "Olhos", "Mystery"], vm.TraitSections.Select(s => s.Title));
    }
}
