using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;

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
}
