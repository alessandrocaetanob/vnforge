using VNForge.Core.Glossary;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

[Trait("Risco", "R2")]
public sealed class ImageInventoryFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public ImageInventoryFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static readonly ImageTextEntry[] Sample =
    [
        new("pac/ui.pak", "menu/titulo.png", "ab12", 640, 480, "タイトル\nはじめから"),
        new("pac/ui.pak", "bg/rua.png", "cd34", 1280, 720, string.Empty),
    ];

    [Fact]
    public void Sem_o_arquivo_o_inventario_e_vazio()
    {
        Assert.Empty(_project.LoadImageInventory());
        Assert.False(File.Exists(_project.ImageInventoryPath));
    }
}
