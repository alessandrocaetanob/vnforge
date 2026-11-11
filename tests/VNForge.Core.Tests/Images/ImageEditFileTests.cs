using VNForge.Core.Glossary;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

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
    public void SavesToImageEditsJsonAndReadsBackEqual()
    {
        ImageEdit[] edits = [Sample(), Sample("sys/logo.png") with { Erase = ImageEraseModes.Solid, EraseColor = "#102030", Outline = null, Marked = false }];

        _project.SaveImageEdits(edits);

        Assert.Equal(Path.Combine(_project.Root, "image-edits.json"), _project.ImageEditsPath);
        Assert.Equal(edits, _project.LoadImageEdits());
    }

    [Theory]
    [InlineData("color", "azul")]
    [InlineData("fontSize", "0")]
    public void RefusesAnInvalidEditWithoutWritingAnything(string field, string value)
    {
        var edit = field switch
        {
            "erase" => Sample() with { Erase = value },
            "color" => Sample() with { Color = value },
            _ => Sample() with { FontSize = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
        };

        Assert.Throws<GlossaryFileException>(() => _project.SaveImageEdits([edit]));
        Assert.False(File.Exists(_project.ImageEditsPath));
    }

    [Fact]
    public void RefusesSolidEraseWithoutAColor()
    {
        Assert.Throws<GlossaryFileException>(() => _project.SaveImageEdits([Sample() with { Erase = ImageEraseModes.Solid, EraseColor = null }]));
    }

    [Fact]
    public void RefusesAnEmptyRegion()
    {
        Assert.Throws<GlossaryFileException>(() => _project.SaveImageEdits([Sample() with { Region = new ImageRegion(0, 0, 0, 10) }]));
    }
}
