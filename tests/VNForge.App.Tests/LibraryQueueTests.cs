using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A fila de tradução da Biblioteca (#551): a ordem "Fila", as colunas da lista e a conferência do português no VNDB, sobre um <c>library.json</c> real e um VNDB de mentira.</summary>
public sealed class LibraryQueueTests : IDisposable
{
    private readonly TempFolder _config = new();

    public void Dispose() => _config.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VndbRelease PortugueseRelease(string visualNovelId) =>
        new("r1", "Tradução", null, [new VndbReleaseLanguage("pt-br", null, null, false, true)], [], null, null, false, false, false, null, null, [new VndbReleaseVisualNovel(visualNovelId, "complete")]);

    private LibraryViewModel Create(Func<PortugueseTranslationChecker>? checker = null)
    {
        var file = new LibraryFile(_config.Path);
        return new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), null, null, new FixedClock(LibrarySamples.Now), LibrarySamples.Translation, null, new LibrarySamples.Sheets(), portugueseChecker: checker);
    }

    private async Task<LibraryViewModel> Loaded(IEnumerable<LibraryGame>? games = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(games));
        var vm = Create();
        vm.SelectedLayout = vm.Layouts[1];
        await vm.LoadAsync();
        return vm;
    }

    private static LibraryQueueRowViewModel Row(LibraryViewModel vm, string title) => vm.Rows.Single(r => r.Card.Title == title);

    [Fact]
    public async Task A_ordem_Fila_poe_primeiro_o_que_falta_traduzir_e_o_texto_mais_curto_na_frente()
    {
        var vm = await Loaded();

        Assert.Equal(LibrarySort.Queue, vm.Sort);
        Assert.Equal(vm.Cards.Select(c => c.Title), vm.Rows.Select(r => r.Card.Title));

        vm.SelectedSort = vm.Sorts[1];
        Assert.Equal("Akatsuki no Kōro", vm.Rows[0].Card.Title);

        vm.SelectedSort = vm.Sorts[0];

        Assert.Equal(["Tsukiyo no Hanabi", "Nanairo Ressha", "Akatsuki no Kōro"], vm.Rows.Take(3).Select(r => r.Card.Title));
        Assert.Equal(["1", "2", "3", "", ""], vm.Rows.Take(5).Select(r => r.PositionText));
        Assert.Equal(vm.Rows.Select(r => r.Card.Title), vm.Cards.Select(c => c.Title));
        Assert.Equal("Hoshikuzu Diary", vm.Rows[3].Card.Title);
        Assert.Equal("Tōge no Kaji", vm.Rows[^1].Card.Title);
    }

    [Fact]
    public async Task As_linhas_so_sao_montadas_na_lista_e_somem_na_grade()
    {
        var vm = await Loaded();
        Assert.Equal(12, vm.Rows.Count);

        vm.SelectedLayout = vm.Layouts[0];
        Assert.Empty(vm.Rows);

        vm.SearchText = "Nanairo";
        Assert.Empty(vm.Rows);
        Assert.Single(vm.Cards);

        vm.SelectedLayout = vm.Layouts[1];
        Assert.Equal(["Nanairo Ressha"], vm.Rows.Select(r => r.Card.Title));
    }

    [Fact]
    public async Task A_linha_traz_o_titulo_original_e_o_subtitulo_do_jogo()
    {
        var vm = await Loaded();

        var row = Row(vm, "Amaoto no Hakoniwa");

        Assert.Equal("雨音の箱庭", row.Card.OriginalTitle);
        Assert.True(row.Card.HasOriginalTitle);
        Assert.Equal("Média · 18 h · 38% traduzida", row.Card.Subtitle);
    }

    [Fact]
    public async Task Arquivo_travado_na_conferencia_do_portugues_vira_aviso_da_biblioteca()
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        var vm = Create(() => throw new IOException("O arquivo está em uso por outro processo."));

        await vm.OpenAsync(() => Task.CompletedTask);
        await vm.PortugueseChecking;

        Assert.Contains("Não deu para conferir o português no VNDB: O arquivo está em uso por outro processo.", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cada_linha_escreve_o_portugues_a_engine_o_texto_a_duracao_e_a_acao()
    {
        var vm = await Loaded();

        var queued = Row(vm, "Tsukiyo no Hanabi");
        Assert.Equal("Não conferido", queued.PortugueseText);
        Assert.Equal("Yu-RIS · traduz", queued.EngineText);
        Assert.Equal("ok", queued.EngineTone);
        Assert.Equal("≈ 10 mil linhas", queued.TextEstimate);
        Assert.Equal("Curta · 5 h", queued.Duration);
        Assert.Equal("Traduzir este jogo", queued.ActionText);
        Assert.True(queued.CanAct);
        Assert.True(queued.IsTranslate);

        var unsupported = Row(vm, "Shirogane Station 9");
        Assert.Equal("Siglus · sem suporte", unsupported.EngineText);
        Assert.Equal("warn", unsupported.EngineTone);
        Assert.Equal("Sem suporte ainda", unsupported.ActionText);
        Assert.False(unsupported.CanAct);

        var started = Row(vm, "Amaoto no Hakoniwa");
        Assert.Equal("Abrir projeto", started.ActionText);
        Assert.False(started.IsTranslate);

        var unmatched = Row(vm, "Kōrin no Mori");
        Assert.Equal("Sem casamento", unmatched.PortugueseText);
        Assert.Equal("·", unmatched.TextEstimate);
        Assert.Equal("·", unmatched.Duration);
    }

    [Fact]
    public async Task Ordenar_por_duracao_poe_o_mais_curto_na_frente_e_o_que_nao_tem_duracao_no_fim()
    {
        var vm = await Loaded();

        vm.SelectedSort = vm.Sorts[2];

        Assert.Equal("Tsukiyo no Hanabi", vm.Rows[0].Card.Title);
        Assert.Equal("Yūgure Kissaten", vm.Rows[1].Card.Title);
        Assert.Equal(["Kōrin no Mori", "Tōge no Kaji"], vm.Rows.TakeLast(2).Select(r => r.Card.Title));
        Assert.All(vm.Rows, r => Assert.Equal("", r.PositionText));
    }

    [Fact]
    public async Task Traduzir_este_jogo_avisa_o_shell_com_o_jogo_da_linha()
    {
        var vm = await Loaded();
        LibraryGame? asked = null;
        vm.GameOpenRequested += (_, game) => asked = game;

        vm.OpenGameCommand.Execute(Row(vm, "Nanairo Ressha").Card);

        Assert.Equal("Nanairo Ressha", asked?.Title);
        Assert.Null(asked?.ProjectPath);
    }

    [Fact]
    public async Task Abrir_a_biblioteca_confere_o_portugues_no_VNDB_guarda_no_library_json_e_reordena_a_fila()
    {
        var games = LibrarySamples.Games().Select(g => g.Title == "Tsukiyo no Hanabi" ? g with { VndbId = "v2" } : g).ToList();
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(games));
        var vndb = new FakeVndbCatalog(releases: [PortugueseRelease("v31201")]);
        var vm = Create(() => new PortugueseTranslationChecker(vndb, new FixedClock(LibrarySamples.Now)));
        vm.SelectedLayout = vm.Layouts[1];

        await vm.OpenAsync(() => Task.CompletedTask);
        await vm.PortugueseChecking;
        await vm.Loading;
        vm.SelectedSort = vm.Sorts[0];

        Assert.Equal("Não há", Row(vm, "Tsukiyo no Hanabi").PortugueseText);
        Assert.Equal("ok", Row(vm, "Tsukiyo no Hanabi").PortugueseTone);
        Assert.Equal("Completa", Row(vm, "Nanairo Ressha").PortugueseText);
        Assert.Equal("Tsukiyo no Hanabi", vm.Rows[0].Card.Title);
        Assert.Equal(["Nanairo Ressha", "Akatsuki no Kōro"], vm.Rows.Skip(4).Take(2).Select(r => r.Card.Title));
        var saved = new LibraryFile(_config.Path).Load();
        Assert.Equal(PortugueseStatus.None, saved.Games.Single(g => g.Title == "Tsukiyo no Hanabi").CurrentPortuguese?.Status);
        Assert.Equal(PortugueseStatus.Complete, saved.Games.Single(g => g.Title == "Nanairo Ressha").CurrentPortuguese?.Status);
        Assert.Null(saved.Games.Single(g => g.Title == "Shirogane Station 9").CurrentPortuguese);
    }
}
