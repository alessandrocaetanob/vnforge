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
        return new MainWindowViewModel(start, new ResolvedApiKey(null, ApiKeySource.None), settings: settings);
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
