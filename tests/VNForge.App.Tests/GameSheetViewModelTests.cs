using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Factsheet;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A aba Ficha do jogo (d9, #554): estados, spoiler por nível, tradução e a revisão por blocos, com o catálogo e o tradutor falsos.</summary>
[Trait("Risco", "R2")]
public sealed class GameSheetViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();
    private readonly List<(string Text, ToastKind Kind)> _notices = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _config.Dispose();

    private GameSheetViewModel Create(bool translator = true, bool catalog = true, string? directory = null) =>
        new()
        {
            ConfigDirectory = directory ?? _config.Path,
            CatalogFactory = catalog ? GameSheetSamples.Catalog : null,
            TranslatorFactory = translator ? GameSheetSamples.Translator : null,
            Clock = new FixedClock(GameSheetSamples.Now),
            Notify = (text, kind) => _notices.Add((text, kind)),
        };

    private async Task<GameSheetViewModel> ReadyAsync(bool translate = true, string? directory = null)
    {
        await GameSheetSamples.SeedAsync(directory ?? _config.Path, translate, ct: Ct);
        var vm = Create(directory: directory);
        vm.Open(GameSheetSamples.Id, "Amaoto no Hakoniwa");
        await vm.Loading;
        return vm;
    }

    private SheetStore Store => new(_config.Path);

    // --- Revisão ---

    private static async Task<SheetReviewViewModel> ReviewAsync(GameSheetViewModel vm)
    {
        vm.StartReviewCommand.Execute(null);
        await Task.CompletedTask;
        return vm.Review;
    }

    [Fact]
    public async Task Navegar_ou_reler_com_texto_que_nao_pode_ser_gravado_nao_perde_o_rascunho_e_o_que_pode_e_gravado_antes()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.SelectedFilter = review.Filters[1];
        while (review.Current!.Title != "Haruto Mine")
        {
            review.NextCommand.Execute(null);
        }

        review.Pairs[0].Draft = "Veterano do clube.";
        vm.CycleSpoilerCommand.Execute(null);

        Assert.Equal("Veterano do clube.", Store.LoadCharacter("c6")!.Description!.Target);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];

        while (review.Current!.Title != "Misaki Tōdō")
        {
            review.NextCommand.Execute(null);
        }

        review.Pairs[1].Draft = "Só um nome";
        review.NextCommand.Execute(null);
        review.PreviousCommand.Execute(null);
        review.SelectedFilter = review.Filters[0];
        vm.ExitReviewCommand.Execute(null);

        Assert.Equal("Misaki Tōdō", review.Current!.Title);
        Assert.Equal("Só um nome", review.Pairs[1].Draft);
        Assert.True(review.Pairs[1].HasError);
        Assert.Same(review.Filters[1], review.SelectedFilter);
        Assert.True(vm.IsReviewing);
    }

}
