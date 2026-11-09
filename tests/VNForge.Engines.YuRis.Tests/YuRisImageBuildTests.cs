using System.Security.Cryptography;
using VNForge.Core;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis.Tests;

/// <summary>
/// O teste de ida e volta do editor de imagens (#535) sobre um YPF sintético: uma imagem trocada no <c>img.ypf</c>, e todo o resto do arquivo
/// (cabeçalho, entradas e bytes armazenados das outras entradas) igual ao original (regra 3 do <c>CLAUDE.md</c>).
/// </summary>
public sealed class YuRisImageBuildTests : IDisposable
{
    private const string ImgYpf = "img.ypf";
    private const string Gate = "bg/portao.png";
    private const string GateName = "bg\\portao.png";

    private static readonly byte[] Original = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7 % 251)).ToArray();
    private static readonly byte[] Edited = Enumerable.Range(0, 2500).Select(i => (byte)(i * 13 % 241)).ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-yuris-imagens-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _out;
    private readonly ProjectFolder _project;
    private readonly byte[] _ypf;

    public YuRisImageBuildTests()
    {
        _game = Path.Combine(_root, "jogo");
        _out = Path.Combine(_root, "saida");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_out);
        _ypf = new SyntheticYpfBuilder(500)
            .AddFile("ui\\logo.png", Enumerable.Repeat((byte)'L', 4000).ToArray(), SyntheticYpfFileType.Png, compress: true)
            .AddFile(GateName, Original, SyntheticYpfFileType.Png, compress: true)
            .AddFile("bgm\\tema.ogg", [1, 2, 3, 4, 5, 6, 7, 8], SyntheticYpfFileType.Ogg)
            .AddFile("sys\\vazio.bin", [], SyntheticYpfFileType.Ybn)
            .AddFile("bg\\rua.png", Enumerable.Range(0, 900).Select(i => (byte)i).ToArray(), SyntheticYpfFileType.Png)
            .Build().Bytes;
        File.WriteAllBytes(Path.Combine(_game, ImgYpf), _ypf);
        _project = ProjectFolder.Create(
            Path.Combine(_root, "projeto"),
            new ProjectConfig("Teste", new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"), new EngineConfig("yuris"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("sjis"), AiConfig.Default));
        _project.SaveImageEdits([new ImageEdit(ImgYpf, Gate, Convert.ToHexStringLower(SHA256.HashData(Original)), 64, 36, new ImageRegion(4, 4, 20, 8), ImageEraseModes.Surrounding, null, "Portão", "Noto Sans", 18, false, "#000000", null, true)]);
        var file = _project.EditedImagePath(ImgYpf, Gate);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, Edited);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ImageBuildResult Apply() =>
        ImageBuild.Apply(_project, _game, new YuRisEngine().Archives, _out, cancellationToken: TestContext.Current.CancellationToken);

    private static (YpfArchive Archive, byte[] Bytes) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ((YpfArchive)new YpfArchiveFormat().Open(new MemoryStream(bytes)), bytes);
    }

    [Fact]
    public void OneSwappedImageLeavesTheRestOfTheYpfByteForByteEqual()
    {
        var result = Apply();

        Assert.Equal(1, result.Archives.Single().ReplacedFiles);
        Assert.Empty(result.Warnings);
        var (before, beforeBytes) = (((YpfArchive)new YpfArchiveFormat().Open(new MemoryStream(_ypf))), _ypf);
        var (after, afterBytes) = Read(Path.Combine(_out, ImgYpf));
        using (before)
        using (after)
        {
            Assert.Equal(before.Entries.Select(e => e.Path), after.Entries.Select(e => e.Path));
            foreach (var (old, now) in before.Entries.Zip(after.Entries))
            {
                if (old.Path == Gate)
                {
                    using var stream = after.OpenEntry(now);
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    Assert.Equal(Edited, copy.ToArray());
                    continue;
                }

                Assert.Equal(old.NameHash, now.NameHash);
                Assert.Equal(old.DataHash, now.DataHash);
                Assert.Equal(old.Size, now.Size);
                Assert.Equal(old.IsCompressed, now.IsCompressed);
                Assert.Equal(
                    beforeBytes[(int)old.Offset..(int)(old.Offset + old.SizeStored)],
                    afterBytes[(int)now.Offset..(int)(now.Offset + now.SizeStored)]);
            }
        }

        Assert.Equal(beforeBytes[..SyntheticYpf.FixedHeaderSize].Take(8), afterBytes[..SyntheticYpf.FixedHeaderSize].Take(8));
    }

    [Fact]
    public void TheGameFileIsNeverTouched()
    {
        Apply();

        Assert.Equal(_ypf, File.ReadAllBytes(Path.Combine(_game, ImgYpf)));
    }

    [Fact]
    public void TheSameBytesInTheEditedImageGiveAnIdenticalFile()
    {
        File.WriteAllBytes(_project.EditedImagePath(ImgYpf, Gate), Original);

        Apply();

        Assert.Equal(_ypf, File.ReadAllBytes(Path.Combine(_out, ImgYpf)));
    }
}
