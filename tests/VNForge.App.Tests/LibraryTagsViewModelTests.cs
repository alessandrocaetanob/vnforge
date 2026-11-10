using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>As tags próprias da Biblioteca (#559) sem janela: o filtro por tag que junta as suas e as do VNDB, o filtro salvo e o diálogo de criar, renomear e apagar, sobre o <c>library-tags.json</c> real.</summary>
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
            clock: new FixedClock(LibrarySamples.Now),
            readTranslation: LibrarySamples.Translation,
            sheets: new LibrarySamples.Sheets(),
            tagsFile: TagsFile);
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
    public async Task O_filtro_junta_tag_sua_e_do_VNDB_e_o_proximo_inicio_abre_com_ele()
    {
        var vm = await Open(Favorites);

        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.OwnOptions.Single(o => o.Text == "favorito"));
        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.VndbOptions.Single(o => o.Text == "Escola"));

        Assert.Equal(["favorito", "Escola"], vm.Tags.Active.Select(t => t.Text));
        Assert.Equal(2, vm.Cards.Count);
        Assert.Equal("2 de 12 jogos · favorito e Escola", vm.Subtitle);
        Assert.True(vm.HasFilter);
        Assert.Equal(["favorito", "Escola", "+8"], vm.Cards[0].Tags.Select(c => c.Text));

        var reopened = await Open();

        Assert.Equal(["favorito", "Escola"], reopened.Tags.Active.Select(t => t.Text));
        Assert.Equal(2, reopened.Cards.Count);
    }

    [Fact]
    public async Task Tirar_um_termo_ou_trocar_para_qualquer_uma_refaz_a_grade()
    {
        var vm = await Open(Favorites);
        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.OwnOptions.Single(o => o.Text == "favorito"));
        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.OwnOptions.Single(o => o.Text == "rejogar"));

        Assert.Equal("Tem as duas tags", vm.Tags.ModeText);
        Assert.Single(vm.Cards);

        vm.Tags.ToggleModeCommand.Execute(null);

        Assert.Equal("Tem uma das tags", vm.Tags.ModeText);
        Assert.Equal(3, vm.Cards.Count);

        vm.Tags.RemoveActiveCommand.Execute(vm.Tags.Active.Single(t => t.Text == "rejogar"));

        Assert.Equal(2, vm.Cards.Count);
        Assert.False(vm.Tags.HasMode);
    }

    [Fact]
    public async Task Renomear_atualiza_os_jogos_e_o_filtro_salvo_e_apagar_avisa_do_filtro()
    {
        var vm = await Open(Favorites);
        vm.Tags.ToggleOptionCommand.Execute(vm.Tags.OwnOptions.Single(o => o.Text == "favorito"));
        vm.Tags.OpenManageCommand.Execute(null);
        var row = vm.Tags.Rows.Single(r => r.Name == "favorito");
        Assert.Equal("2 jogos", row.GamesText);

        row.EditText = "  Pra  Rever ";
        vm.Tags.SaveRenameCommand.Execute(row);

        Assert.Equal(["pra rever"], vm.Tags.Active.Select(t => t.Text));
        Assert.Equal(["minha:pra rever"], TagsFile.Load().SavedFilter.Include);
        Assert.Equal(["pra rever"], TagsFile.Load().TagsOf(Id(0)));
        Assert.Equal(2, vm.Cards.Count);

        vm.Tags.AskDeleteCommand.Execute(vm.Tags.Rows.Single(r => r.Name == "pra rever"));
        vm.Tags.ConfirmDeleteCommand.Execute(vm.Tags.Rows.Single(r => r.Name == "pra rever"));

        Assert.Equal(["rejogar"], vm.Tags.Rows.Select(r => r.Name));
        Assert.Empty(TagsFile.Load().TagsOf(Id(0)));
        Assert.Contains("O filtro salvo foi apagado", vm.Tags.Message);
        Assert.False(vm.Tags.HasActive);
        Assert.Equal(12, vm.Cards.Count);
    }

    [Fact]
    public async Task Criar_tag_com_nome_repetido_ou_invalido_avisa_e_nada_muda()
    {
        var vm = await Open(Favorites);
        vm.Tags.OpenManageCommand.Execute(null);

        vm.Tags.NewName = "emprestado";
        vm.Tags.AddTagCommand.Execute(null);
        Assert.Equal(["emprestado", "favorito", "rejogar"], vm.Tags.Rows.Select(r => r.Name));
        Assert.Equal("0 jogos", vm.Tags.Rows[0].GamesText);

        vm.Tags.NewName = "vndb: sim";
        vm.Tags.AddTagCommand.Execute(null);
        Assert.Contains("dois-pontos", vm.Tags.Message);
        Assert.Equal(3, vm.Tags.Rows.Count);

        var row = vm.Tags.Rows.Single(r => r.Name == "rejogar");
        row.EditText = "Favorito";
        vm.Tags.SaveRenameCommand.Execute(row);
        Assert.Equal("Já existe uma tag chamada 'favorito'.", vm.Tags.Message);
        Assert.Equal(3, TagsFile.Load().Tags.Count);
    }

    [Fact]
    public async Task Arquivo_de_tags_ilegivel_nao_esconde_os_jogos()
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        await File.WriteAllTextAsync(TagsFile.Path, "{ isto não é json", TestContext.Current.CancellationToken);
        var vm = new LibraryViewModel(new LibraryFile(_config.Path), new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now),
            readTranslation: LibrarySamples.Translation, sheets: new LibrarySamples.Sheets(), tagsFile: TagsFile);

        await vm.LoadAsync();

        Assert.Equal(12, vm.Cards.Count);
        Assert.Contains("library-tags.json", vm.ErrorMessage);
    }
}
