using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>O diálogo "Exportar catálogo" (#555) sem janela: as contagens das três escolhas, o arquivo gravado e as recusas de gravar onde não deve.</summary>
[Trait("Risco", "R1")]
[Trait("Risco", "R7")]
[Trait("Risco", "R9")]
public sealed class LibraryExportTests : IDisposable
{
    private readonly TempFolder _config = new();
    private readonly TempFolder _out = new();

    public void Dispose()
    {
        _config.Dispose();
        _out.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(LibraryViewModel Library, List<string> Opened)> Loaded(IEnumerable<LibraryGame>? games = null, ILibrarySheetSource? sheets = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(games));
        var opened = new List<string>();
        var library = new LibraryViewModel(new LibraryFile(_config.Path), new FakeFolderPicker(_out.Path), action => action(), new LibraryServices { Clock = new FixedClock(LibrarySamples.Now), ReadTranslation = LibrarySamples.Translation, Sheets = sheets ?? new LibrarySamples.Sheets() });
        library.Export.OpenFile = opened.Add;
        library.Export.Folder = _out.Path;
        await library.LoadAsync();
        return (library, opened);
    }

    [Theory]
    [InlineData(AdultMode.Show, true)]
    [InlineData(AdultMode.Blur, false)]
    [InlineData(AdultMode.Hide, false)]
    public async Task So_o_modo_Mostrar_poe_o_jogo_adulto_no_catalogo(AdultMode mode, bool included)
    {
        var (library, _) = await Loaded();
        library.ChooseAdultMode(mode);
        library.Export.Open();

        await library.Export.ExportCommand.ExecuteAsync(null);

        var html = await File.ReadAllTextAsync(Path.Combine(_out.Path, "index.html"), Ct);
        Assert.Equal(included, html.Contains("Kuroneko Archive", StringComparison.Ordinal));
        Assert.Contains("Amaoto no Hakoniwa", html, StringComparison.Ordinal);
    }

    private sealed class BrokenSheets : ILibrarySheetSource
    {
        public LibrarySheet? SheetOf(LibraryGame game) => throw new InvalidOperationException("ficha quebrada");
    }

    [Fact]
    public async Task Dentro_da_pasta_de_um_jogo_o_dialogo_recusa_e_nao_grava_nada()
    {
        var game = LibrarySamples.Games()[0] with { Folder = _out.Path };
        var (library, opened) = await Loaded([game]);
        library.Export.Open();

        await library.Export.ExportCommand.ExecuteAsync(null);

        Assert.Contains("fica dentro da pasta do jogo", library.Export.ErrorMessage, StringComparison.Ordinal);
        Assert.True(library.Export.IsOpen);
        Assert.Empty(opened);
        Assert.False(File.Exists(Path.Combine(_out.Path, "index.html")));
    }

    [Fact]
    public async Task Um_index_html_que_nao_e_catalogo_nao_e_trocado_mas_o_catalogo_antigo_e()
    {
        var (library, opened) = await Loaded();
        var path = Path.Combine(_out.Path, "index.html");
        await File.WriteAllTextAsync(path, "<html><title>Meu site</title></html>", Ct);
        library.Export.Open();

        await library.Export.ExportCommand.ExecuteAsync(null);

        Assert.Contains("não é um catálogo do VNForge", library.Export.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("<html><title>Meu site</title></html>", await File.ReadAllTextAsync(path, Ct));

        File.Delete(path);
        await library.Export.ExportCommand.ExecuteAsync(null);
        library.Export.Open();
        await library.Export.ExportCommand.ExecuteAsync(null);

        Assert.Null(library.Export.ErrorMessage);
        Assert.Equal([path, path], opened);
    }
}
