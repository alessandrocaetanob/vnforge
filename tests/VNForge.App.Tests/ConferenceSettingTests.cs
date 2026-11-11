using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O ajuste de formatação que a estratégia de acentuação não grava: o card da Conferência no lugar das linhas, o botão que troca o ajuste e a dica legível.</summary>
public class ConferenceSettingTests
{
    private static TextUnit Unit(int n, string translation) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", translation, TranslationStatus.Translated);

    /// <summary>Projeto cp1252 com Reticências = "Caractere …": as linhas têm três pontos e o ajuste é que cria o <c>…</c>.</summary>
    private static ProjectFolder CreateProject(TempFolder temp, params TextUnit[] units)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("vn"), new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Teste", "Teste.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Typography = FormattingProfile.Default.Typography with { Ellipsis = EllipsisStyle.Character } });
        folder.SaveText("yst00001", units);
        return folder;
    }

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> Loaded(TempFolder temp, params TextUnit[] units)
    {
        var folder = CreateProject(temp, units);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();
        return (vm, folder);
    }

    private static TextUnit[] ThreeEllipsisLines() => [Unit(0, "Espera... vai"), Unit(1, "Hum... sim"), Unit(2, "Ah... não")];

    [Fact]
    public async Task Corrigir_com_um_clique_tambem_troca_o_ajuste_e_o_aviso_diz_qual()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp, [.. ThreeEllipsisLines(), Unit(3, "olá  mundo")]);
        var changed = 0;
        vm.SettingsChanged += (_, _) => changed++;

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal(EllipsisStyle.Dots, folder.LoadFormatting().Typography.Ellipsis);
        Assert.Equal(1, changed);
        Assert.Empty(vm.SettingCards);
        Assert.Empty(vm.Rows);
        Assert.Equal("1 correção aplicada. O ajuste Reticências passou de Caractere … para Três pontos.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Sem_conflito_o_botao_nao_grava_nem_avisa_o_app()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp, ThreeEllipsisLines());
        var card = vm.SettingCards[0];
        folder.SaveFormatting(FormattingProfile.Default);
        var changed = 0;
        vm.SettingsChanged += (_, _) => changed++;

        await vm.ApplySettingCommand.ExecuteAsync(card);

        Assert.Equal(0, changed);
        Assert.Equal("O ajuste já não estava em conflito.", vm.NoticeMessage);
        Assert.Empty(vm.SettingCards);
    }

    private static ConferenceIssueRowViewModel Row(int codePoint) =>
        new(new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00001:000001", "yst00001", $"o caractere '{char.ConvertFromUtf32(codePoint)}' (U+{codePoint:X4}) não existe na estratégia 'cp1252'."),
            characters: new ConferenceRowCharacterContext(TextEncoderRegistry.Default.Create("cp1252")));

    [Fact]
    public void O_ponto_final_nao_cola_no_caractere_citado()
    {
        var detail = Row(0x2026).Detail;

        Assert.EndsWith("(…).", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("….", detail.Replace("(…).", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void O_painel_explica_o_ajuste_quando_o_caractere_vem_dele()
    {
        FormattingConflict conflict = new(FormattingConflicts.EllipsisField, "Reticências", "character", "Caractere …", "…", "dots", "Três pontos");
        var row = new ConferenceIssueRowViewModel(
            new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00001:000001", "yst00001", "o caractere '…' (U+2026) não existe na estratégia 'cp1252'."),
            characters: new ConferenceRowCharacterContext(Settings: [conflict]));

        Assert.DoesNotContain("Troque por um que ela aceite", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("ajuste Reticências (Caractere …)", row.Explanation, StringComparison.Ordinal);
        Assert.Contains("Ajustes", row.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_ajuste_o_painel_mantem_o_texto_de_antes()
    {
        var row = new ConferenceIssueRowViewModel(
            new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00001:000001", "yst00001", "o caractere '★' (U+2605) não existe na estratégia 'cp1252'."));

        Assert.Contains("Troque por um que ela aceite", row.Explanation, StringComparison.Ordinal);
    }
}
