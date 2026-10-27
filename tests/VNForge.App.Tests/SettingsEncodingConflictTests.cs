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
    public async Task Com_cp1252_a_opcao_Caractere_fica_desligada_com_a_dica_e_as_outras_ficam_ligadas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "cp1252");

        var character = vm.EllipsisStyles.Single(e => e.Value == EllipsisStyle.Character);
        Assert.False(character.IsEnabled);
        Assert.Equal("A estratégia cp1252 não grava o '…'.", character.DisabledReason);
        Assert.All(vm.EllipsisStyles.Where(e => e.Value != EllipsisStyle.Character), e => Assert.True(e.IsEnabled));
        Assert.All(vm.EllipsisStyles.Where(e => e.Value != EllipsisStyle.Character), e => Assert.Null(e.DisabledReason));
        Assert.False(vm.HasEncodingConflicts);
    }

    [Fact]
    public async Task Com_cp1252_as_aspas_de_canto_ficam_desligadas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "cp1252");

        var corner = vm.QuoteStyles.Single(q => q.Value == QuoteStyle.Corner);
        Assert.False(corner.IsEnabled);
        Assert.Equal("A estratégia cp1252 não grava os caracteres '「' e '」'.", corner.DisabledReason);
        Assert.All(vm.QuoteStyles.Where(q => q.Value != QuoteStyle.Corner), q => Assert.True(q.IsEnabled));
    }

    [Fact]
    public async Task Com_sjis_todas_as_opcoes_ficam_ligadas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "sjis");

        Assert.All(vm.EllipsisStyles, e => Assert.True(e.IsEnabled));
        Assert.All(vm.QuoteStyles, q => Assert.True(q.IsEnabled));
    }

    [Fact]
    public async Task Trocar_a_estrategia_liga_e_desliga_as_opcoes()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "sjis");
        var character = vm.EllipsisStyles.Single(e => e.Value == EllipsisStyle.Character);
        Assert.True(character.IsEnabled);

        vm.EncodingStrategy = "cp1252";
        Assert.False(character.IsEnabled);

        vm.EncodingStrategy = "sjis";
        Assert.True(character.IsEnabled);
        Assert.Null(character.DisabledReason);
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
        Assert.Equal("A estratégia cp1252 não grava o '…'.", notice.Message);
        Assert.Equal("Usar três pontos", notice.ButtonLabel);

        notice.FixCommand.Execute(null);

        Assert.Equal(EllipsisStyle.Dots, vm.SelectedEllipsis?.Value);
        Assert.Equal(EllipsisStyle.Dots, folder.LoadFormatting().Typography.Ellipsis);
        Assert.False(vm.HasEncodingConflicts);
        Assert.Empty(vm.EncodingConflicts);
    }

    [Fact]
    public async Task Escolher_outra_opcao_a_mao_tambem_some_com_a_faixa()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "sjis", quotes: QuoteStyle.Corner);
        vm.EncodingStrategy = "cp1252";
        Assert.Equal("Usar aspas retas", Assert.Single(vm.EncodingConflicts).ButtonLabel);

        vm.SelectedQuotes = vm.QuoteStyles.Single(q => q.Value == QuoteStyle.Ascii);

        Assert.False(vm.HasEncodingConflicts);
    }

    [Fact]
    public async Task Estrategia_sem_encoder_nao_desliga_nada()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Open(temp, "remap");

        Assert.All(vm.EllipsisStyles, e => Assert.True(e.IsEnabled));
        Assert.False(vm.HasEncodingConflicts);
    }
}
