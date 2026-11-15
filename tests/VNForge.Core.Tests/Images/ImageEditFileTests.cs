using VNForge.Core.Glossary;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

[Trait("Risco", "R2")]
public sealed class ImageEditFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public ImageEditFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig("Teste", new GameConfig("jogo", "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("sjis"), AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static ImageEdit Sample(string path = "bg/portao.png") =>
        new("img.ypf", path, "ab12", 1280, 720, new ImageRegion(100, 80, 188, 62), ImageEraseModes.Surrounding, null, "Colégio Sakuragaoka", "Zen Kaku Gothic New", 18, true, "#1d1d2a", "#ffffff", true);

    [Fact]
    public void WithoutTheFileThereAreNoEdits()
    {
        Assert.Empty(_project.LoadImageEdits());
        Assert.False(File.Exists(_project.ImageEditsPath));
    }
}
