using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ProjectReadinessTests
{
    private static ProjectFolder NewProject(string root, StyleConfig? style = null) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default) { Style = style ?? StyleConfig.Default });

    private static ReadinessKind[] Kinds(ReadinessFacts facts) => [.. ProjectReadiness.Evaluate(facts).Select(i => i.Kind)];

    [Fact]
    public void Quando_nada_falta_a_lista_e_vazia()
    {
        Assert.Empty(ProjectReadiness.Evaluate(new ReadinessFacts()));
    }

    [Fact]
    public void Cada_fato_vira_um_item_com_a_quantidade()
    {
        var facts = new ReadinessFacts(
            HasApiKey: false, QaErrors: 2, QaWarnings: 5, CharactersWithoutGender: 4, NarratorDefined: false, HasStyleGuide: false, TestedInGame: false, SpendLimitReached: true);

        var items = ProjectReadiness.Evaluate(facts);

        Assert.Equal(
            [ReadinessKind.ApiKey, ReadinessKind.QaErrors, ReadinessKind.SpendLimit, ReadinessKind.CharacterGender, ReadinessKind.AccentTest, ReadinessKind.Narrator, ReadinessKind.StyleGuide, ReadinessKind.QaWarnings],
            items.Select(i => i.Kind));
        Assert.Equal(2, items.Single(i => i.Kind == ReadinessKind.QaErrors).Count);
        Assert.Equal(4, items.Single(i => i.Kind == ReadinessKind.CharacterGender).Count);
        Assert.Equal(5, items.Single(i => i.Kind == ReadinessKind.QaWarnings).Count);
    }

    [Fact]
    public void Os_erros_vem_antes_dos_avisos()
    {
        var items = ProjectReadiness.Evaluate(new ReadinessFacts(CharactersWithoutGender: 1, QaErrors: 1, HasApiKey: false));

        Assert.Equal([ReadinessSeverity.Error, ReadinessSeverity.Error, ReadinessSeverity.Warning], items.Select(i => i.Severity));
        Assert.Equal([ReadinessKind.ApiKey, ReadinessKind.QaErrors, ReadinessKind.CharacterGender], items.Select(i => i.Kind));
    }

    [Fact]
    public void Sem_a_chave_e_um_erro()
    {
        var item = Assert.Single(ProjectReadiness.Evaluate(new ReadinessFacts(HasApiKey: false)));

        Assert.Equal(ReadinessKind.ApiKey, item.Kind);
        Assert.Equal(ReadinessSeverity.Error, item.Severity);
    }

    [Fact]
    public void Le_do_projeto_o_que_ele_guarda_e_o_que_falta_aparece()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        project.SaveCharacters(
        [
            new CharacterEntry("美咲", "Misaki", CharacterGender.Female),
            new CharacterEntry("健太", "Kenta"),
            new CharacterEntry("蓮", "Ren", CharacterGender.Unknown),
        ]);

        var facts = ReadinessFacts.Read(project, new QaTotals(1, 3));

        Assert.Equal(2, facts.CharactersWithoutGender);
        Assert.False(facts.NarratorDefined);
        Assert.False(facts.HasStyleGuide);
        Assert.False(facts.TestedInGame);
        Assert.Equal(1, facts.QaErrors);
        Assert.Equal(3, facts.QaWarnings);
        Assert.Equal(
            [ReadinessKind.QaErrors, ReadinessKind.CharacterGender, ReadinessKind.AccentTest, ReadinessKind.Narrator, ReadinessKind.StyleGuide, ReadinessKind.QaWarnings],
            Kinds(facts));
    }

    [Fact]
    public void Narrador_guia_de_estilo_e_teste_no_jogo_prontos_tiram_os_itens()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path, new StyleConfig { Narrator = new NarratorStyle { Who = "Kenta, o protagonista" } });
        File.WriteAllText(Path.Combine(project.Root, "style.md"), "Tom leve, frases curtas.");
        GameStateFile.Save(project, new GameStateFile(GameStateFile.CurrentVersion, null, new GameLastTest(DateTimeOffset.UnixEpoch, GameTestKind.AccentTest, [])));

        var facts = ReadinessFacts.Read(project, new QaTotals(0, 0));

        Assert.True(facts.NarratorDefined);
        Assert.True(facts.HasStyleGuide);
        Assert.True(facts.TestedInGame);
        Assert.Empty(ProjectReadiness.Evaluate(facts));
    }

    [Fact]
    public void Guia_de_estilo_em_branco_conta_como_ausente()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        File.WriteAllText(Path.Combine(project.Root, "style.md"), "  \n");

        Assert.False(ReadinessFacts.Read(project, null).HasStyleGuide);
    }

    [Fact]
    public void Characters_json_quebrado_nao_derruba_a_leitura()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        File.WriteAllText(project.CharactersPath, "isto nao e json");

        var facts = ReadinessFacts.Read(project, null);

        Assert.Equal(0, facts.CharactersWithoutGender);
    }

    [Fact]
    public void O_painel_traz_os_fatos_o_ritmo_e_o_estado_do_jogo()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        project.SaveText("yst00001", [new VNForge.Core.Text.TextUnit("yst00001:000000", VNForge.Core.Text.TextUnitKind.Dialogue, null, "原文", "tradução", VNForge.Core.Text.TranslationStatus.Machine)]);
        SessionsFile.Save(project, SessionsFile.Empty.With(new ApprovalRecord("yst00001", "yst00001:000000", DateTimeOffset.UtcNow)));

        var dashboard = ProjectDashboard.Read(project);

        Assert.Single(dashboard.Sessions.Approvals);
        Assert.False(dashboard.Readiness.TestedInGame);
        Assert.Null(dashboard.GameState.LastTest);
        Assert.Equal("Parte 1", Assert.Single(dashboard.Chapters()).Name);
    }
}
