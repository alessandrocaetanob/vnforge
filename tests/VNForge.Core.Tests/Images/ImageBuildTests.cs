using System.Security.Cryptography;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

public sealed class ImageBuildTests : IDisposable
{
    private const string Pack = "img.pak";
    private const string Gate = "bg/portao.png";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly string _out;
    private readonly ProjectFolder _project;
    private readonly byte[] _original = SyntheticImages.Png(64, 36, 10);
    private readonly byte[] _other = SyntheticImages.Png(32, 32, 99);
    private readonly byte[] _edited = SyntheticImages.Png(64, 36, 200);
    private readonly WritableArchiveFormat _format = new();

    public ImageBuildTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        _out = Path.Combine(_temp.Path, "saida");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_out);
        WritableArchiveFormat.Write(Path.Combine(_game, Pack), new Dictionary<string, byte[]> { [Gate] = _original, ["bg/outra.png"] = _other, ["sys/dados.bin"] = [1, 2, 3] });
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig("Teste", new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("sjis"), AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private ImageEdit Edit(string path = Gate, string? hash = null, bool marked = true) =>
        new(Pack, path, hash ?? HashOf(_original), 64, 36, new ImageRegion(4, 4, 20, 8), ImageEraseModes.Surrounding, null, "Portão", "Noto Sans", 18, true, "#1d1d2a", null, marked);

    private void SaveEdit(ImageEdit edit, byte[]? image)
    {
        _project.SaveImageEdits([edit]);
        if (image is not null)
        {
            var file = _project.EditedImagePath(edit.Archive, edit.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, image);
        }
    }

    private ImageBuildResult Apply(Func<string, string>? originalOf = null) =>
        ImageBuild.Apply(_project, _game, [_format], _out, originalOf);

    private Dictionary<string, byte[]> Output() => WritableArchiveFormat.Read(Path.Combine(_out, Pack));

    [Fact]
    public void WithoutMarkedEditsNothingIsWritten()
    {
        SaveEdit(Edit(marked: false), _edited);

        var result = Apply();

        Assert.Empty(result.Archives);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(Path.Combine(_out, Pack)));
    }

    [Fact]
    public void ReplacesOnlyTheEditedImageAndKeepsTheOtherEntriesByteForByte()
    {
        SaveEdit(Edit(), _edited);

        var result = Apply();

        Assert.Equal([new RepackedArchive(Pack, 1)], result.Archives);
        Assert.Empty(result.Warnings);
        var written = Output();
        Assert.Equal(_edited, written[Gate]);
        Assert.Equal(_other, written["bg/outra.png"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, written["sys/dados.bin"]);
        Assert.Equal(["bg/portao.png", "bg/outra.png", "sys/dados.bin"], written.Keys);
    }

    [Fact]
    public void BuildsOnTopOfAnArchiveThatTheScriptStepAlreadyWrote()
    {
        WritableArchiveFormat.Write(Path.Combine(_out, Pack), new Dictionary<string, byte[]> { [Gate] = _original, ["bg/outra.png"] = _other, ["sys/dados.bin"] = [9, 9] });
        SaveEdit(Edit(), _edited);

        Apply();

        var written = Output();
        Assert.Equal(new byte[] { 9, 9 }, written["sys/dados.bin"]);
        Assert.Equal(_edited, written[Gate]);
    }

    [Fact]
    public void ReadsTheOriginalFromTheResolverWhenTheGameIsAlreadyInjected()
    {
        var backup = Path.Combine(_temp.Path, "backup.pak");
        File.Copy(Path.Combine(_game, Pack), backup);
        WritableArchiveFormat.Write(Path.Combine(_game, Pack), new Dictionary<string, byte[]> { ["so-traduzido"] = [0] });
        SaveEdit(Edit(), _edited);

        var result = Apply(name => name == Pack ? backup : name);

        Assert.Equal(1, result.Archives.Single().ReplacedFiles);
        Assert.Equal(_edited, Output()[Gate]);
    }

    [Fact]
    public void SkipsAnEditWhenTheGameImageChanged()
    {
        SaveEdit(Edit(hash: HashOf(SyntheticImages.Png(64, 36, 77))), _edited);

        var result = Apply();

        Assert.Empty(result.Archives);
        Assert.Contains("mudou depois da edição", Assert.Single(result.Warnings));
        Assert.False(File.Exists(Path.Combine(_out, Pack)));
    }

    [Fact]
    public void SkipsAnEditWhoseFinishedImageIsMissing()
    {
        SaveEdit(Edit(), null);

        var result = Apply();

        Assert.Empty(result.Archives);
        Assert.Contains("imagem pronta", Assert.Single(result.Warnings));
    }

    [Fact]
    public void SkipsAnEditWhoseImageLeftThePackage()
    {
        SaveEdit(Edit("bg/sumiu.png"), _edited);

        var result = Apply();

        Assert.Empty(result.Archives);
        Assert.Contains("não existe mais", Assert.Single(result.Warnings));
    }

    [Fact]
    public void WarnsWhenThePackageIsNotInTheGame()
    {
        _project.SaveImageEdits([Edit() with { Archive = "outro.pak" }]);

        var result = Apply();

        Assert.Empty(result.Archives);
        Assert.Contains("outro.pak", Assert.Single(result.Warnings));
    }

    [Fact]
    public void FingerprintIsEmptyWithoutMarkedEditsAndChangesWithTheFinishedImage()
    {
        Assert.Equal(string.Empty, ImageBuild.Fingerprint(_project));
        SaveEdit(Edit(marked: false), _edited);
        Assert.Equal(string.Empty, ImageBuild.Fingerprint(_project));

        SaveEdit(Edit(), _edited);
        var first = ImageBuild.Fingerprint(_project);
        File.WriteAllBytes(_project.EditedImagePath(Pack, Gate), SyntheticImages.Png(64, 36, 201));

        Assert.NotEqual(string.Empty, first);
        Assert.NotEqual(first, ImageBuild.Fingerprint(_project));
    }

    [Fact]
    public void EditedImagePathRefusesToLeaveTheImagesFolder()
    {
        Assert.Throws<ArgumentException>(() => _project.EditedImagePath(Pack, "../../fora.png"));
    }
}
