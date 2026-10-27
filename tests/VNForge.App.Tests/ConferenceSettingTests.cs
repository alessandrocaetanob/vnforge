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
    public async Task O_ajuste_vira_um_card_com_a_contagem_no_lugar_das_linhas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp, ThreeEllipsisLines());

        var card = Assert.Single(vm.SettingCards);
        Assert.Equal("O ajuste Reticências gera 3 erros", card.Title);
        Assert.Equal("A estratégia cp1252 não grava as reticências (…). As traduções têm três pontos; o ajuste é que troca.", card.Text);
        Assert.Equal("Usar três pontos", card.ButtonLabel);
        Assert.Equal(3, card.Count);
        Assert.True(vm.HasSettingCards);
        Assert.Empty(vm.Rows);
        Assert.False(vm.ShowEmptyText);
        Assert.Equal(3, vm.ErrorCount);
        Assert.Equal("3", vm.BlockingCount);
    }

    [Fact]
    public async Task Um_erro_so_diz_erro_no_singular()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp, Unit(0, "Espera... vai"));

        Assert.Equal("O ajuste Reticências gera 1 erro", Assert.Single(vm.SettingCards).Title);
    }

    [Fact]
    public async Task O_filtro_Um_clique_e_o_botao_do_cabecalho_contam_as_linhas_que_o_ajuste_resolve()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp, [.. ThreeEllipsisLines(), Unit(3, "olá  mundo")]);

        Assert.Equal(4, vm.Filters.Single(f => f.Kind == ConferenceFilterKind.OneClick).Count);
        Assert.Equal("Corrigir 4 com um clique", vm.FixAllLabel);
        Assert.True(vm.HasFixable);

        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.OneClick));

        Assert.True(vm.HasSettingCards);
        Assert.Equal(["espaco-duplo"], vm.Rows.Select(r => r.RuleId));
    }

    [Fact]
    public async Task O_card_some_nos_filtros_que_nao_o_incluem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp, [.. ThreeEllipsisLines(), Unit(3, "olá  mundo")]);

        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.Box));

        Assert.False(vm.HasSettingCards);
    }

    [Fact]
    public async Task Usar_tres_pontos_grava_o_perfil_avisa_o_app_e_confere_de_novo_sem_erro_de_codificacao()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp, ThreeEllipsisLines());
        var changed = 0;
        vm.SettingsChanged += (_, _) => changed++;

        await vm.ApplySettingCommand.ExecuteAsync(vm.SettingCards[0]);

        Assert.Equal(EllipsisStyle.Dots, folder.LoadFormatting().Typography.Ellipsis);
        Assert.Equal(1, changed);
        Assert.Empty(vm.SettingCards);
        Assert.Empty(vm.Rows);
        Assert.True(vm.IsClear);
        Assert.Equal("O ajuste Reticências passou de Caractere … para Três pontos.", vm.NoticeMessage);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(["Espera... vai", "Hum... sim", "Ah... não"], folder.LoadText("yst00001").Select(u => u.Translation));
    }

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
    public async Task Sem_outra_correcao_o_aviso_so_fala_do_ajuste()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp, ThreeEllipsisLines());

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal("O ajuste Reticências passou de Caractere … para Três pontos.", vm.NoticeMessage);
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

    [Fact]
    public async Task Linha_com_o_caractere_digitado_continua_na_lista_e_nao_vira_card()
    {
        using var temp = new TempFolder();
        var folder = CreateProject(temp, Unit(0, "Espera★ vai"));
        folder.SaveFormatting(FormattingProfile.Default);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };

        await vm.RefreshAsync();

        Assert.Empty(vm.SettingCards);
        Assert.Equal(["caractere-nao-codificavel"], vm.Rows.Select(r => r.RuleId));
    }

    private static ConferenceIssueRowViewModel Row(int codePoint) =>
        new(new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00001:000001", "yst00001", $"o caractere '{char.ConvertFromUtf32(codePoint)}' (U+{codePoint:X4}) não existe na estratégia 'cp1252'."),
            encoder: TextEncoderRegistry.Default.Create("cp1252"));

    [Theory]
    [InlineData(0x2026, "O … (reticências, U+2026) não existe na acentuação do projeto. Use três pontos (...) no lugar das reticências (…).")]
    [InlineData(0x201C, "O “ (aspas curvas, U+201C) não existe na acentuação do projeto. Use aspas retas (\") no lugar das aspas curvas (“).")]
    [InlineData(0x201D, "O ” (aspas curvas, U+201D) não existe na acentuação do projeto. Use aspas retas (\") no lugar das aspas curvas (”).")]
    [InlineData(0x2014, "O \u2014 (travessão, U+2014) não existe na acentuação do projeto. Use hífen (-) no lugar do travessão (U+2014).")]
    [InlineData(0x2013, "O \u2013 (meia-risca, U+2013) não existe na acentuação do projeto. Use hífen (-) no lugar da meia-risca (U+2013).")]
    [InlineData(0xFF5E, "O ～ (til japonês, U+FF5E) não existe na acentuação do projeto. Use ~ no lugar do til japonês (～).")]
    public void A_dica_diz_por_extenso_o_que_trocar_quando_o_substituto_se_confunde_com_o_original(int codePoint, string expected)
    {
        Assert.Equal(expected, Row(codePoint).Detail);
    }

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
            settings: [conflict]);

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
