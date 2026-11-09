using System.Security.Cryptography;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Images;

/// <summary>O <c>vnf build</c> com imagens editadas (#535): o pacote de imagens vai para o <c>build/</c> junto com os scripts.</summary>
public sealed class ImageBuildProjectTests : IDisposable
{
    private const string Pack = "img.pak";
    private const string Gate = "bg/portao.png";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine;
    private readonly byte[] _original = SyntheticImages.Png(64, 36, 10);
    private readonly byte[] _edited = SyntheticImages.Png(64, 36, 200);

    public ImageBuildProjectTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        WritableArchiveFormat.Write(Path.Combine(_game, Pack), new Dictionary<string, byte[]> { [Gate] = _original, ["sys/dados.bin"] = [1, 2, 3] });
        _engine = new FakeEngine { Archives = [new WritableArchiveFormat()] };
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig("Teste", new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("sjis"), AiConfig.Default));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _temp.Dispose();

    private BuildResult Build(BuildOptions? options = null) =>
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: options);

    private void MarkEdit(bool marked = true)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(_original));
        _project.SaveImageEdits([new ImageEdit(Pack, Gate, hash, 64, 36, new ImageRegion(4, 4, 20, 8), ImageEraseModes.Surrounding, null, "Portão", "Noto Sans", 18, false, "#000000", null, marked)]);
        var file = _project.EditedImagePath(Pack, Gate);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, _edited);
    }

    private string BuiltPack => Path.Combine(_project.BuildDirectory, Pack);

    [Fact]
    public void WithoutEditsTheImagePackageIsNotInTheBuild()
    {
        var result = Build();

        Assert.False(File.Exists(BuiltPack));
        Assert.DoesNotContain(result.Archives, a => a.GamePath == Pack);
    }

    [Fact]
    public void AMarkedEditPutsTheRewrittenPackageInTheBuild()
    {
        MarkEdit();

        var result = Build();

        Assert.Equal(1, result.Archives.Single(a => a.GamePath == Pack).ReplacedFiles);
        var built = WritableArchiveFormat.Read(BuiltPack);
        Assert.Equal(_edited, built[Gate]);
        Assert.Equal(new byte[] { 1, 2, 3 }, built["sys/dados.bin"]);
        Assert.Equal(_original, WritableArchiveFormat.Read(Path.Combine(_game, Pack))[Gate]);
    }

    [Fact]
    public void AnIncrementalBuildPicksUpAnEditAddedLater()
    {
        Build();
        MarkEdit();

        Build(new BuildOptions { OnlyChanged = true });

        Assert.Equal(_edited, WritableArchiveFormat.Read(BuiltPack)[Gate]);
    }

    [Fact]
    public void AnIncrementalBuildDropsAnEditThatWasUnmarked()
    {
        MarkEdit();
        Build();
        MarkEdit(marked: false);

        Build(new BuildOptions { OnlyChanged = true });

        Assert.False(File.Exists(BuiltPack));
    }

    [Fact]
    public void AnIncrementalBuildWithNothingNewReusesTheBuild()
    {
        MarkEdit();
        Build();
        var before = File.GetLastWriteTimeUtc(BuiltPack);

        Build(new BuildOptions { OnlyChanged = true });

        Assert.Equal(before, File.GetLastWriteTimeUtc(BuiltPack));
        Assert.Equal(_edited, WritableArchiveFormat.Read(BuiltPack)[Gate]);
    }

    [Fact]
    public void ADroppedEditShowsUpAsABuildWarning()
    {
        MarkEdit();
        File.Delete(_project.EditedImagePath(Pack, Gate));

        var result = Build();

        Assert.Contains(result.Warnings, w => w.Contains("imagem pronta", StringComparison.Ordinal));
        Assert.False(File.Exists(BuiltPack));
    }
}
