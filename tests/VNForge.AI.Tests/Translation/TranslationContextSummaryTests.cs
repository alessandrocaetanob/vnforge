using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.AI.Tests.Translation;

public sealed class TranslationContextSummaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public TranslationContextSummaryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ProjectFolder Folder()
    {
        var config = new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default);
        return ProjectFolder.Create(_root, config);
    }

    [Fact]
    public void Projeto_vazio_nao_leva_nada_junto()
    {
        var summary = TranslationContextSummary.Read(Folder());

        Assert.Equal(new TranslationContextSummary(0, 0, 0, 0, false), summary);
    }

    [Fact]
    public void Conta_glossario_voz_genero_e_guia_de_estilo()
    {
        var folder = Folder();
        folder.SaveGlossary([new GlossaryTerm("魔法", "magia"), new GlossaryTerm("剣", "espada")]);
        folder.SaveCharacters(
        [
            new CharacterEntry("ハル", "Haru", CharacterGender.Female, "calma"),
            new CharacterEntry("ケン", "Ken"),
            new CharacterEntry("ミキ", "Miki", CharacterGender.Male),
        ]);
        File.WriteAllText(Path.Combine(folder.Root, TranslationContextSummary.StyleGuideFileName), "Use você.");

        var summary = TranslationContextSummary.Read(folder);

        Assert.Equal(new TranslationContextSummary(2, 3, 1, 1, true), summary);
    }

    [Fact]
    public void Guia_de_estilo_em_branco_nao_conta()
    {
        var folder = Folder();
        File.WriteAllText(Path.Combine(folder.Root, TranslationContextSummary.StyleGuideFileName), "   \n");

        Assert.False(TranslationContextSummary.Read(folder).HasStyleGuide);
    }
}
