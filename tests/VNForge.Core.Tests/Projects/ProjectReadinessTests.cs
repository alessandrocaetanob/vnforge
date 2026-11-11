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
    public void Guia_de_estilo_em_branco_conta_como_ausente()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        File.WriteAllText(Path.Combine(project.Root, "style.md"), "  \n");

        Assert.False(ReadinessFacts.Read(project, null).HasStyleGuide);
    }
}
