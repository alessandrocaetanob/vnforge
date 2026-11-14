using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>As tags próprias da Biblioteca (#559) sem janela: o filtro por tag que junta as suas e as do VNDB, o filtro salvo e o diálogo de criar, renomear e apagar, sobre o <c>library-tags.json</c> real.</summary>
[Trait("Risco", "R2")]
public sealed class LibraryTagsViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();

    public void Dispose() => _config.Dispose();

    private static string Id(int index) => LibrarySamples.Games()[index].Id;

    private UserTagsFile TagsFile => new(_config.Path);

    private async Task<LibraryViewModel> Open(Action<UserTags>? seed = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        TagsFile.Update(tags => seed?.Invoke(tags));
        var vm = new LibraryViewModel(
            new LibraryFile(_config.Path),
            new FakeFolderPicker(null),
            action => action(),
            new LibraryServices
            {
                Clock = new FixedClock(LibrarySamples.Now),
                ReadTranslation = LibrarySamples.Translation,
                Sheets = new LibrarySamples.Sheets(),
                TagsFile = TagsFile,
            });
        await vm.LoadAsync();
        return vm;
    }

    private static void Favorites(UserTags tags)
    {
        tags.Add(Id(0), "favorito");
        tags.Add(Id(1), "favorito");
        tags.Add(Id(1), "rejogar");
        tags.Add(Id(2), "rejogar");
    }

    [Fact]
    public async Task Falha_de_gravacao_das_tags_vira_aviso_e_o_filtro_continua_coerente()
    {
        var vm = await Open(Favorites);
        File.Delete(TagsFile.Path);
        Directory.CreateDirectory(TagsFile.Path);

        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.OwnOptions.Single(o => o.Text == "favorito"));

        Assert.Contains("library-tags.json", vm.ErrorMessage);
        Assert.Equal(["favorito"], vm.Tags.Active.Select(t => t.Text));
        Assert.Equal(2, vm.Cards.Count);
    }

}
