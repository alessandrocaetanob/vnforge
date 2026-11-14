using VNForge.App.ViewModels;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests;

/// <summary>#536: o passo "Ficha do VNDB" do assistente, com o catálogo falso (nunca a API).</summary>
[Trait("Risco", "R7")]
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
    public async Task VNDB_fora_do_ar_avisa_em_portugues_e_deixa_seguir_sem_ele()
    {
        var vm = await AtVndbStepAsync(new FailingCatalog());

        Assert.Equal(VndbStage.Failed, vm.Stage);
        Assert.Equal("O VNDB não respondeu", vm.VndbHeadline);
        Assert.Contains("sem rede", vm.VndbDetail, StringComparison.Ordinal);
        Assert.True(vm.NextCommand.CanExecute(null));
        Assert.True(vm.SkipVndbCommand.CanExecute(null));
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
