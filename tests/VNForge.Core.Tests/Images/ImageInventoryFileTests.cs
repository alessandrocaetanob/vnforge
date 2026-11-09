using VNForge.Core.Glossary;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

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

    [Fact]
    public void Grava_em_images_json_e_le_de_volta_igual()
    {
        _project.SaveImageInventory(Sample);

        Assert.Equal(Path.Combine(_project.Root, "images.json"), _project.ImageInventoryPath);
        Assert.Equal(Sample, _project.LoadImageInventory());
    }

    [Fact]
    public void O_arquivo_guarda_o_japones_sem_escape_e_uma_entrada_por_linha()
    {
        _project.SaveImageInventory(Sample);

        var text = File.ReadAllText(_project.ImageInventoryPath);

        Assert.Contains("タイトル", text);
        Assert.Contains("\"archive\":\"pac/ui.pak\"", text);
        Assert.Equal(Sample.Length, text.Split('\n').Count(l => l.Contains("\"hash\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Entrada_repetida_pelo_lugar_e_recusada_na_gravacao()
    {
        var twice = new[] { Sample[0], Sample[0] with { Hash = "outro" } };

        var error = Assert.Throws<GlossaryFileException>(() => _project.SaveImageInventory(twice));

        Assert.Contains("aparece mais de uma vez", error.Message);
        Assert.False(File.Exists(_project.ImageInventoryPath));
    }

    [Fact]
    public void Arquivo_com_campo_faltando_e_recusado_em_portugues()
    {
        File.WriteAllText(_project.ImageInventoryPath, """{ "version": 1, "entries": [ { "archive": "a.pak", "path": "x.png" } ] }""");

        Assert.Throws<GlossaryFileException>(() => _project.LoadImageInventory());
    }
}
