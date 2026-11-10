using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Engines.YuRis;

namespace VNForge.App.Tests;

/// <summary>A Biblioteca como o <c>App</c> a monta: o <c>library.json</c> da pasta de configuração, varredura e casamento ligados, sem a janela.</summary>
public sealed class LibraryCompositionTests
{
    [Fact]
    public async Task A_biblioteca_do_app_le_o_library_json_da_pasta_de_configuracao_e_varre_uma_pasta()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        LibrarySamples.Save(config, LibrarySamples.Library());
        var games = temp.SubFolder("jogos");
        Directory.CreateDirectory(Path.Combine(games, "[Key] CLANNAD v1.02"));
        File.WriteAllText(Path.Combine(games, "[Key] CLANNAD v1.02", "data.xp3"), "x");
        File.WriteAllText(Path.Combine(games, "[Key] CLANNAD v1.02", "CLANNAD.exe"), "x");

        var library = App.CreateLibrary(config, new FakeFolderPicker(games), [new YuRisEngine()], action => action());
        await library.LoadAsync();

        Assert.Equal(LibraryMode.Games, library.Mode);
        Assert.Equal("12 jogos na Biblioteca", library.StatusText);

        library.Scan.MatchVndb = false;
        await library.ChooseFolderCommand.ExecuteAsync(null);

        var saved = new LibraryFile(config).Load();
        Assert.Equal(13, saved.Games.Count);
        Assert.Contains(saved.Roots, r => string.Equals(r.Path, GameLibrary.NormalizeFolder(games), StringComparison.OrdinalIgnoreCase));
        Assert.Null(library.ErrorMessage);
    }
}
