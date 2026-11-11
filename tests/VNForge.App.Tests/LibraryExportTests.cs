using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>O diálogo "Exportar catálogo" (#555) sem janela: as contagens das três escolhas, o arquivo gravado e as recusas de gravar onde não deve.</summary>
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
        var library = new LibraryViewModel(new LibraryFile(_config.Path), new FakeFolderPicker(_out.Path), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, sheets: sheets ?? new LibrarySamples.Sheets());
        library.Export.OpenFile = opened.Add;
        library.Export.Folder = _out.Path;
        await library.LoadAsync();
        return (library, opened);
    }

    [Fact]
    public async Task O_dialogo_conta_os_jogos_de_cada_escolha_e_so_oferece_o_filtro_com_filtro_ligado()
    {
        var (library, _) = await Loaded();
        var export = library.Export;

        export.Open();

        Assert.True(export.IsOpen);
        Assert.Equal("9 jogos; 2 sem casamento no VNDB e 1 adulto ficam de fora", export.Scopes[0].Detail);
        export.IncludeSensitive = true;
        Assert.Equal("9 jogos; 2 sem casamento no VNDB e 1 adulto ficam de fora", export.Scopes[0].Detail);
        export.IncludeSensitive = false;
        Assert.Equal("3 jogos", export.Scopes[1].Detail);
        Assert.False(export.Scopes[2].IsAvailable);

        library.SearchText = "amaoto";
        export.Open();

        Assert.True(export.Scopes[2].IsAvailable);
        Assert.Equal("“amaoto”, 1 jogo", export.Scopes[2].Detail);
    }

    [Fact]
    public async Task Exportar_grava_o_index_html_com_o_credito_ao_VNDB_fecha_o_dialogo_e_abre_a_pagina()
    {
        var (library, opened) = await Loaded();
        var export = library.Export;
        export.Open();

        await export.ExportCommand.ExecuteAsync(null);

        var path = Path.Combine(_out.Path, "index.html");
        Assert.Equal([path], opened);
        Assert.False(export.IsOpen);
        var html = await File.ReadAllTextAsync(path, Ct);
        Assert.Contains("Amaoto no Hakoniwa", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Kōrin no Mori", html, StringComparison.Ordinal);
        Assert.Contains("Open Database License", html, StringComparison.Ordinal);
        Assert.Contains("Numa cidade onde chove", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task As_escolhas_do_dialogo_valem_na_pagina()
    {
        var (library, _) = await Loaded();
        var export = library.Export;
        library.SearchText = "amaoto";
        export.Open();
        export.Select(LibraryExportScope.CurrentFilter);
        export.IncludeSheet = false;

        await export.ExportCommand.ExecuteAsync(null);

        var html = await File.ReadAllTextAsync(Path.Combine(_out.Path, "index.html"), Ct);
        Assert.Contains("Amaoto no Hakoniwa", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nanairo Ressha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Numa cidade onde chove", html, StringComparison.Ordinal);
        Assert.Contains("Open Database License", html, StringComparison.Ordinal);
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

    [Fact]
    public async Task Qualquer_erro_da_gravacao_vira_aviso_no_dialogo_que_continua_aberto()
    {
        var (library, opened) = await Loaded(sheets: new BrokenSheets());
        library.Export.Open();

        await library.Export.ExportCommand.ExecuteAsync(null);

        Assert.Equal("ficha quebrada", library.Export.ErrorMessage);
        Assert.True(library.Export.IsOpen);
        Assert.False(library.Export.IsBusy);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task Esc_durante_a_gravacao_nao_abre_o_navegador()
    {
        var (library, opened) = await Loaded();
        library.Export.Open();
        library.Export.IsOpen = false;

        await library.Export.ExportCommand.ExecuteAsync(null);

        Assert.Empty(opened);
    }

    private sealed class BrokenSheets : ILibrarySheetSource
    {
        public LibrarySheet? SheetOf(LibraryGame game) => throw new InvalidOperationException("ficha quebrada");
    }

    [Fact]
    public async Task Trocar_escolhe_a_pasta_do_seletor()
    {
        var (library, _) = await Loaded();
        library.Export.Folder = string.Empty;

        await library.Export.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(_out.Path, library.Export.Folder);
    }

    [Fact]
    public async Task Dentro_da_pasta_de_um_jogo_o_dialogo_recusa_e_nao_grava_nada()
    {
        var game = LibrarySamples.Games().First() with { Folder = _out.Path };
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
