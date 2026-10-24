using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string? pickerAnswer = null, IApiKeyStore? vault = null)
    {
        var config = temp.SubFolder("config");
        var settings = new SettingsViewModel(vault ?? new MemoryApiKeyStore(), new AppSettingsFile(config), _ => null);
        var start = new StartViewModel(new RecentProjectsFile(config), new FakeFolderPicker(pickerAnswer), new FixedClock(Now));
        return new MainWindowViewModel(start, new ResolvedApiKey(null, ApiKeySource.None), settings);
    }

    [Fact]
    public void Ajustes_fica_sempre_habilitado_e_abre_a_tela_de_ajustes()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        var item = vm.Nav.Single(n => n.Key == NavKey.Settings);
        vm.SelectedNav = item;

        Assert.True(item.IsEnabled);
        Assert.Equal("Ajustes", item.Label);
        Assert.Same(vm.Settings, vm.CurrentPage);
        Assert.False(vm.Settings.HasProject);
    }

    [Fact]
    public async Task Abrir_um_projeto_entrega_o_projeto_aos_ajustes()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        Assert.True(vm.Settings.HasProject);
        Assert.Equal("Minha VN", vm.Settings.ProjectName);
    }

    [Fact]
    public void Guardar_a_chave_atualiza_a_barra_de_status_sem_mostrar_a_chave()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        Assert.False(vm.ApiKeyConfigured);

        vm.Settings.NewApiKey = "sk-ant-segredo";
        vm.Settings.SaveKeyCommand.Execute(null);

        Assert.True(vm.ApiKeyConfigured);
        Assert.Equal("Chave do Claude no Cofre do Windows", vm.ApiKeyStatus);

        vm.Settings.ClearKeyCommand.Execute(null);

        Assert.False(vm.ApiKeyConfigured);
        Assert.StartsWith("Chave do Claude não configurada", vm.ApiKeyStatus);
    }
}
