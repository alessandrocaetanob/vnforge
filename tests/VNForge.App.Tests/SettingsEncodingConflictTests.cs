using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Ajustes desliga a opção de Reticências e de Aspas que a estratégia de acentuação do projeto não grava, e avisa quando a troca de estratégia deixa o ajuste atual em conflito.</summary>
public class SettingsEncodingConflictTests
{
    private static SettingsViewModel Create(TempFolder temp) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null);

    private static async Task<(SettingsViewModel Vm, ProjectFolder Folder)> Open(TempFolder temp, string strategy, EllipsisStyle ellipsis = EllipsisStyle.None, QuoteStyle quotes = QuoteStyle.None)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveConfig(folder.LoadConfig() with { Encoding = new EncodingConfig(strategy) });
        folder.SaveFormatting(FormattingProfile.Default with { Typography = FormattingProfile.Default.Typography with { Ellipsis = ellipsis, Quotes = quotes } });
        var vm = Create(temp);
        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        return (vm, folder);
    }

    [Fact]
    public async Task Trocar_para_cp1252_com_o_ajuste_em_conflito_mostra_a_faixa_e_Usar_tres_pontos_resolve()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Open(temp, "sjis", EllipsisStyle.Character);
        Assert.False(vm.HasEncodingConflicts);

        vm.EncodingStrategy = "cp1252";

        var notice = Assert.Single(vm.EncodingConflicts);
        Assert.True(vm.HasEncodingConflicts);
        Assert.Equal("A estratégia cp1252 não grava as reticências (…).", notice.Message);
        Assert.Equal("Usar três pontos", notice.ButtonLabel);

        notice.FixCommand.Execute(null);

        Assert.Equal(EllipsisStyle.Dots, vm.SelectedEllipsis?.Value);
        Assert.Equal(EllipsisStyle.Dots, folder.LoadFormatting().Typography.Ellipsis);
        Assert.False(vm.HasEncodingConflicts);
        Assert.Empty(vm.EncodingConflicts);
    }
}
