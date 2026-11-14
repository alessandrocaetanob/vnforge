using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Engines;
using VNForge.Core.Images;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
public sealed class ImagesViewModelTests : IDisposable
{
    private const string Pack = "img.pak";
    private const string Gate = "bg/portao.png";
    private const string Logo = "ui/logo.png";

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly ProjectFolder folder;
    private readonly byte[] gate = Png(640, 360);
    private readonly byte[] logo = Png(320, 120);
    private readonly byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xC0, 0, 11, 8, 0, 100, 0, 100, 1, 1, 0x11, 0, 0xFF, 0xD9];
    private readonly FakeComposer composer = new();

    public ImagesViewModelTests()
    {
        game = temp.SubFolder("jogo");
        FakeArchive.Write(Path.Combine(game, Pack), new Dictionary<string, byte[]> { [Gate] = gate, [Logo] = logo, ["bg/foto.jpg"] = jpeg, ["sys/dados.bin"] = [1, 2] });
        folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        folder.SaveImageInventory(
        [
            new ImageTextEntry(Pack, Gate, Hash(gate), 640, 360, "桜ヶ丘高校 校門"),
            new ImageTextEntry(Pack, Logo, Hash(logo), 320, 120, "タイトル"),
            new ImageTextEntry(Pack, "bg/foto.jpg", Hash(jpeg), 100, 100, "看板"),
            new ImageTextEntry(Pack, "bg/rua.png", "00", 640, 360, string.Empty),
        ]);
    }

    public void Dispose() => temp.Dispose();

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        bytes.AddRange("IHDR"u8.ToArray());
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        bytes.AddRange(header);
        bytes.AddRange(new byte[4]);
        return [.. bytes];
    }

    private ImagesViewModel Open()
    {
        var vm = new ImagesViewModel([new ArchiveEngine()], composer, TimeSpan.Zero);
        vm.Project = Summary();
        vm.Loading.GetAwaiter().GetResult();
        return vm;
    }

    private ProjectSummary Summary() => new("Teste", folder.Root, "yuris", 500, "fold", "claude-opus-5-5", 0, new StatusCounts(0, 0, 0, 0, 0));

    private static async Task Select(ImagesViewModel vm, string path)
    {
        vm.SelectedItem = vm.Items.Single(i => i.Path == path);
        await vm.Selecting;
    }

    [Fact]
    public async Task MarkingSavesTheFinishedImageAndTheEdit()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Colégio Sakuragaoka";
        vm.UsesSolidErase = true;
        vm.EraseColor = "#102030";

        await vm.MarkCommand.ExecuteAsync(null);

        var edit = Assert.Single(folder.LoadImageEdits());
        Assert.True(edit.Marked);
        Assert.Equal((Pack, Gate, Hash(gate)), (edit.Archive, edit.Path, edit.Hash));
        Assert.Equal(("Colégio Sakuragaoka", ImageEraseModes.Solid, "#102030"), (edit.Text, edit.Erase, edit.EraseColor));
        Assert.True(File.Exists(folder.EditedImagePath(Pack, Gate)));
        Assert.Equal(1, vm.MarkedCount);
        Assert.Equal("1 imagem alterada de 4", vm.MarkedText);
        Assert.Equal(ImageState.Marked, vm.SelectedItem!.State);
        Assert.False(vm.IsDirty);
        Assert.True(vm.IsMarked);
    }

    [Fact]
    public async Task ReadingTheImagesAsksBeforeCallingTheAiThenWritesTheInventoryWithTheSuggestion()
    {
        File.Delete(folder.ImageInventoryPath);
        var reader = new FakeImageTextReader(image => image.Data.AsSpan().SequenceEqual(gate) ? "桜ヶ丘高校 校門" : string.Empty) { InputTokens = 1000, OutputTokens = 100, Translate = _ => "Colégio Sakuragaoka, portão" };
        var vm = Open();
        vm.ReaderFactory = _ => reader;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanConfirming);
        Assert.Empty(reader.Received);
        Assert.Contains(vm.ScanFacts, f => f.Label == "Vão à IA");
        Assert.Contains(vm.ScanFacts, f => f.Label == "Custo estimado");

        vm.ConfirmScanCommand.Execute(null);
        await vm.Scanning;

        Assert.False(vm.IsScanConfirming);
        Assert.False(vm.IsScanning);
        Assert.Null(vm.ErrorMessage);
        var entry = Assert.Single(folder.LoadImageInventory(), e => e.Path == Gate);
        Assert.Equal("桜ヶ丘高校 校門", entry.Text);
        Assert.Equal("Colégio Sakuragaoka, portão", entry.Translation);
        Assert.False(vm.IsEmpty);
        Assert.Equal("Colégio Sakuragaoka, portão", vm.Items.Single(i => i.Path == Gate).Translation);
    }

    [Fact]
    public async Task TheReadingStopsBeforeCallingTheAiWhenTheEstimateBreaksTheSpendLimit()
    {
        File.Delete(folder.ImageInventoryPath);
        var reader = new FakeImageTextReader();
        var vm = Open();
        vm.ReaderFactory = _ => reader;
        vm.Limits = () => new BudgetLimits(0.0001m, null);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.IsScanConfirming);
        Assert.Contains("limite de", vm.ErrorMessage);
        Assert.Empty(reader.Received);
    }

    [Fact]
    public async Task AFailedMoveRollsTheEditsBackAndKeepsTheError()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        Directory.CreateDirectory(folder.EditedImagePath(Pack, Gate));

        await vm.MarkCommand.ExecuteAsync(null);

        Assert.Contains("Não foi possível gravar a edição de portao.png", vm.ErrorMessage);
        Assert.Empty(folder.LoadImageEdits());
        Assert.False(File.Exists(folder.EditedImagePath(Pack, Gate) + ".tmp"));
        Assert.Equal(0, vm.MarkedCount);
        Assert.False(vm.IsMarked);
    }

    /// <summary>Compositor falso: o PNG da prévia é só um rótulo, e o texto mede 10 px por letra.</summary>
    internal sealed class FakeComposer : IImageComposer
    {
        public int Renders { get; private set; }

        public int Thumbnails { get; private set; }

        public string? Refuse { get; set; }

        /// <summary>Segura a montagem da imagem pronta de uma edição marcada (a do botão "Marcar") até alguém liberar.</summary>
        public ManualResetEventSlim? HoldMarked { get; set; }

        /// <summary>Segura a decodificação da imagem de 640 px de largura e depois falha, como um arquivo corrompido.</summary>
        public ManualResetEventSlim? HoldDecode { get; set; }

        public bool ThumbnailFails { get; set; }

        public byte[] Thumbnail(byte[] encoded, int width)
        {
            Thumbnails++;
            if (ThumbnailFails)
            {
                throw new NotSupportedException("formato sem miniatura");
            }

            return Encoding.UTF8.GetBytes("miniatura:" + encoded.Length);
        }

        public IReadOnlyList<string> FontFamilies { get; } = ["Zen Kaku Gothic New", "Noto Sans"];

        public RasterPicture Decode(byte[] encoded)
        {
            if (Refuse is not null)
            {
                throw new InvalidDataException(Refuse);
            }

            var info = ImageProbe.Read(encoded) ?? throw new InvalidDataException("não é imagem");
            if (HoldDecode is not null && info.Width == 640)
            {
                HoldDecode.Wait(TimeSpan.FromSeconds(10));
                throw new InvalidDataException("arquivo corrompido");
            }

            return new RasterPicture(info.Width, info.Height, new byte[info.Width * info.Height * 4]);
        }

        public byte[] Render(RasterPicture source, ImageEdit edit)
        {
            if (edit.Marked)
            {
                HoldMarked?.Wait(TimeSpan.FromSeconds(10));
            }

            if (edit.Text == "BOOM")
            {
                throw new NotSupportedException("a fonte não desenha este texto");
            }

            Renders++;
            return Encoding.UTF8.GetBytes($"{edit.Text}|{edit.Region}|{edit.Erase}|{edit.Font}");
        }

        public (double Width, double Height) Measure(ImageEdit edit) => (edit.Text.Length * 10, edit.FontSize);
    }

    internal sealed class ArchiveEngine : IEngine
    {
        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives { get; } = [new FakeArchive()];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    internal sealed class FakeArchive : IArchiveFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions { get; } = [".pak"];

        public bool CanOpen(Stream stream) => true;

        public IArchive Open(Stream stream)
        {
            using (stream)
            {
                return new Archive(JsonSerializer.Deserialize<Dictionary<string, byte[]>>(stream)!);
            }
        }

        public static void Write(string path, Dictionary<string, byte[]> entries) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(entries));

        private sealed class Archive(Dictionary<string, byte[]> content) : IArchive
        {
            public IReadOnlyList<ArchiveEntry> Entries { get; } = [.. content.Select(e => new ArchiveEntry(e.Key, e.Value.Length, false))];

            public Stream OpenEntry(ArchiveEntry entry) => new MemoryStream(content[entry.Path]);

            public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements) => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }
}
