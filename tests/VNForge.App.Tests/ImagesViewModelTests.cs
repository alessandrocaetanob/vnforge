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
    public async Task OnlyPngImagesAreEditable()
    {
        var vm = Open();

        await Select(vm, "bg/foto.jpg");

        Assert.False(vm.IsEditable);
        Assert.Contains("PNG", vm.ErrorMessage);
    }

    [Fact]
    public async Task ARenamedGameImageWarnsButStaysEditable()
    {
        var vm = Open();
        FakeArchive.Write(Path.Combine(game, Pack), new Dictionary<string, byte[]> { [Gate] = Png(640, 361), [Logo] = logo });

        await Select(vm, Gate);

        Assert.True(vm.IsEditable);
        Assert.Contains("mudou no jogo", vm.ErrorMessage);
    }

    [Fact]
    public async Task FitTextSaysWhetherTheTranslationFitsTheArea()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.SetRegion(100, 100, 200, 40);

        vm.Text = "Colégio";
        await vm.Previewing;
        Assert.Equal("Cabe na placa · 70 de 200 px de largura", vm.FitText);
        Assert.True(vm.FitsRegion);

        vm.Text = "Colégio Sakuragaoka do bairro";
        await vm.Previewing;
        Assert.StartsWith("Não cabe na placa · 290 de 200 px de largura", vm.FitText);
        Assert.False(vm.FitsRegion);
    }

    [Fact]
    public async Task EveryChangeRendersAgain()
    {
        var vm = Open();
        await Select(vm, Gate);
        var before = composer.Renders;

        vm.Text = "Portão";
        await vm.Previewing;
        vm.IsBold = false;
        await vm.Previewing;

        Assert.Equal(before + 2, composer.Renders);
        Assert.True(vm.IsDirty);
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
    public async Task UndoGoesBackToWhatIsSaved()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.MarkCommand.ExecuteAsync(null);
        vm.Text = "Outra coisa";
        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);

        Assert.Equal("Portão", vm.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task TheStateTextFollowsTheEditAndTheChipsReadTheirCountAsText()
    {
        var vm = Open();
        var withText = vm.Items.Single(i => i.Path == Gate);
        var empty = vm.Items.Single(i => i.Path == "bg/rua.png");
        Assert.Equal("Com japonês, sem tradução", withText.StateText);
        Assert.Equal("Sem texto", empty.StateText);
        Assert.Equal("4", vm.Chips[0].CountLabel);

        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.MarkCommand.ExecuteAsync(null);
        Assert.Equal("Editada e marcada para o build", withText.StateText);

        await vm.UnmarkCommand.ExecuteAsync(null);
        Assert.Equal("Editada, ainda fora do build", withText.StateText);
    }

    [Theory]
    [InlineData("24", 24)]
    [InlineData("18 px", 18)]
    [InlineData("12.5PX", 12.5)]
    public async Task TheFontSizeFieldAcceptsTheNumberWithOrWithoutPxAndKeepsTheValueOtherwise(string typed, double expected)
    {
        var vm = Open();
        await Select(vm, Gate);

        vm.FontSizeText = typed;
        Assert.Equal(expected, vm.FontSize);

        vm.FontSizeText = "grande";
        Assert.Equal(expected, vm.FontSize);
    }

    [Fact]
    public async Task ReopeningTheProjectBringsTheSavedEditBack()
    {
        var first = Open();
        await Select(first, Gate);
        first.Text = "Portão";
        first.FontSize = 33;
        first.HasOutline = true;
        await first.MarkCommand.ExecuteAsync(null);

        var second = Open();
        await Select(second, Gate);

        Assert.Equal("Portão", second.Text);
        Assert.Equal(33, second.FontSize);
        Assert.True(second.HasOutline);
        Assert.True(second.IsMarked);
        Assert.Equal(1, second.MarkedCount);
    }

    [Fact]
    public async Task FiltersFollowTheMarkedState()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.MarkCommand.ExecuteAsync(null);

        vm.SetFilterCommand.Execute(ImageFilter.Edited);
        Assert.Equal([Gate], vm.Visible.Select(i => i.Path));

        vm.SetFilterCommand.Execute(ImageFilter.Untranslated);
        Assert.Equal([Logo, "bg/foto.jpg"], vm.Visible.Select(i => i.Path));

        vm.SetFilterCommand.Execute(ImageFilter.WithoutText);
        Assert.Equal(["bg/rua.png"], vm.Visible.Select(i => i.Path));
    }

    [Fact]
    public void SwitchingTheFilterOfThousandsOfImagesNotifiesOnceAndKeepsTheOrder()
    {
        const int count = 5000;
        folder.SaveImageInventory([.. Enumerable.Range(0, count).Select(i => new ImageTextEntry(Pack, $"bg/{i:D5}.png", "00", 640, 360, i % 4 == 0 ? string.Empty : "文"))]);
        var vm = Open();
        Assert.Equal(count, vm.Visible.Count);
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        vm.Visible.CollectionChanged += (_, e) => changes.Add(e.Action);

        vm.SetFilterCommand.Execute(ImageFilter.WithoutText);

        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Reset], changes);
        Assert.Equal(Enumerable.Range(0, count).Where(i => i % 4 == 0).Select(i => $"bg/{i:D5}.png"), vm.Visible.Select(i => i.Path));

        changes.Clear();
        vm.SetFilterCommand.Execute(ImageFilter.All);

        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Reset], changes);
        Assert.Equal(vm.Items.Select(i => i.Path), vm.Visible.Select(i => i.Path));
    }
    [Fact]
    public async Task WithoutAChosenAreaTheCornerIsNotAResizeHandle()
    {
        var vm = Open();
        await Select(vm, "bg/rua.png");
        Assert.Equal((0, 0), (vm.RegionWidth, vm.RegionHeight));

        Assert.False(vm.TryResizeAnchor(0, 0, out _));
        Assert.False(vm.TryResizeAnchor(5, 5, out _));
    }

    [Fact]
    public async Task TheRewriteDialogCountsTheImagesInTheTitleAndTheButton()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.MarkCommand.ExecuteAsync(null);
        await Select(vm, Logo);
        vm.Text = "Título";
        await vm.MarkCommand.ExecuteAsync(null);

        await vm.RewriteCommand.ExecuteAsync(null);

        Assert.Equal("Regravar 2 imagens no PAK?", vm.ConfirmationTitle);
        Assert.Equal("Regravar 2 imagens", vm.ConfirmationButton);
        Assert.Equal("Regravar no PAK", vm.RewriteLabel);
        Assert.Equal(2, vm.ConfirmationFiles.Count);
    }

    [Fact]
    public async Task TheToolbarKeepsOneActiveToolAndSelectAreaIsTheDefault()
    {
        var vm = Open();
        await Select(vm, Gate);

        Assert.True(vm.IsSelectTool);

        vm.UseToolCommand.Execute(ImageTool.Erase);
        Assert.True(vm.IsEraseTool);
        Assert.False(vm.IsSelectTool);

        vm.UseToolCommand.Execute(ImageTool.Text);
        Assert.True(vm.IsTextTool);
        Assert.False(vm.IsEraseTool);
    }

    [Fact]
    public async Task TheTextColorIsChosenAmongThreeSwatchesAndAnOldCustomColorGetsItsOwn()
    {
        var vm = Open();
        await Select(vm, Gate);

        Assert.Equal(["#1d1d2a", "#f1eee6", "#5a4630"], vm.TextSwatches.Select(w => w.Hex));
        Assert.Equal(["#1d1d2a"], vm.TextSwatches.Where(w => w.IsSelected).Select(w => w.Hex));

        vm.PickTextColorCommand.Execute("#f1eee6");
        Assert.Equal("#f1eee6", vm.TextColor);
        Assert.Equal(["#f1eee6"], vm.TextSwatches.Where(w => w.IsSelected).Select(w => w.Hex));

        vm.TextColor = "#336699";
        Assert.Equal(4, vm.TextSwatches.Count);
        Assert.Equal(["#336699"], vm.TextSwatches.Where(w => w.IsSelected).Select(w => w.Hex));
        Assert.True(vm.TextSwatches[1].IsLight);
        Assert.False(vm.TextSwatches[0].IsLight);
    }

    [Fact]
    public async Task TheOutlineFieldSaysNoneOrTakesAColor()
    {
        var vm = Open();
        await Select(vm, Gate);

        Assert.Equal("Nenhum", vm.OutlineText);
        Assert.False(vm.HasOutline);

        vm.OutlineText = "#FFFFFF";
        Assert.True(vm.HasOutline);
        Assert.Equal("#ffffff", vm.OutlineColor);

        vm.OutlineText = "isto não é cor";
        Assert.True(vm.HasOutline);
        Assert.Equal("#ffffff", vm.OutlineColor);

        vm.OutlineText = " nenhum ";
        Assert.False(vm.HasOutline);

        vm.HasOutline = true;
        vm.OutlineColor = "#000000";
        Assert.Equal("#000000", vm.OutlineText);
    }

    [Fact]
    public async Task AHandleOfTheAreaHandsBackTheOppositeCornerAsTheAnchor()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.SetRegion(100, 80, 200, 60);
        vm.OverlayScale = 0.5;

        Assert.Equal(20, vm.HandleSize);
        Assert.Equal(4, vm.OverlayStroke);
        Assert.True(vm.TryResizeAnchor(98, 82, out var fromTopLeft));
        Assert.Equal((300d, 140d), fromTopLeft);
        Assert.True(vm.TryResizeAnchor(305, 135, out var fromBottomRight));
        Assert.Equal((100d, 80d), fromBottomRight);
        Assert.False(vm.TryResizeAnchor(200, 110, out _));
    }

    [Fact]
    public async Task TheInventoryOpensInTheSystemProgram()
    {
        var vm = Open();
        string? opened = null;
        vm.OpenPath = path => opened = path;

        Assert.True(vm.CanOpenInventory);
        vm.OpenInventoryCommand.Execute(null);

        Assert.Equal(folder.ImageInventoryPath, opened);
        await Task.CompletedTask;
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
    public async Task CancellingTheReadingDialogCallsNothing()
    {
        File.Delete(folder.ImageInventoryPath);
        var reader = new FakeImageTextReader();
        var vm = Open();
        vm.ReaderFactory = _ => reader;
        await vm.ScanCommand.ExecuteAsync(null);

        vm.CancelScanCommand.Execute(null);

        Assert.False(vm.IsScanConfirming);
        Assert.Empty(reader.Received);
        Assert.Empty(folder.LoadImageInventory());
    }

    [Fact]
    public async Task WithoutAnApiKeyTheReadingExplainsInsteadOfFailingLater()
    {
        File.Delete(folder.ImageInventoryPath);
        var vm = Open();
        vm.ReaderFactory = _ => throw new InvalidOperationException("Falta a chave da IA.");
        vm.HasApiKey = () => false;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.IsScanConfirming);
        Assert.Contains("chave de API", vm.ErrorMessage);
        Assert.DoesNotContain("vnf", vm.ErrorMessage);
    }

    [Fact]
    public async Task AKeyRemovedBetweenTheEstimateAndTheConfirmationStillShowsTheKeyMessage()
    {
        File.Delete(folder.ImageInventoryPath);
        var hasKey = true;
        var vm = Open();
        vm.HasApiKey = () => hasKey;
        vm.ReaderFactory = _ => hasKey ? new FakeImageTextReader() : throw new InvalidOperationException("Falta a chave da IA.");
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsScanConfirming);
        hasKey = false;

        vm.ConfirmScanCommand.Execute(null);
        await vm.Scanning;

        Assert.Contains("chave de API", vm.ErrorMessage);
        Assert.DoesNotContain("parou", vm.ErrorMessage);
        Assert.False(vm.IsScanning);
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
    public async Task WithNothingMarkedThereIsNothingToRewrite()
    {
        var vm = Open();

        await vm.RewriteCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirming);
        Assert.False(vm.CanRewrite);
    }

    [Fact]
    public async Task ConfirmingAsksForTheBuildAndCancellingDoesNot()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.MarkCommand.ExecuteAsync(null);
        var requests = 0;
        vm.BuildRequested += (_, _) => requests++;

        await vm.RewriteCommand.ExecuteAsync(null);
        vm.CancelRewriteCommand.Execute(null);
        Assert.Equal(0, requests);
        Assert.False(vm.IsConfirming);

        await vm.RewriteCommand.ExecuteAsync(null);
        vm.ConfirmRewriteCommand.Execute(null);
        Assert.Equal(1, requests);
        Assert.False(vm.IsConfirming);
    }


    [Fact]
    public async Task AnImageBeyondTheSizeLimitIsRefusedWithAMessage()
    {
        var vm = Open();
        composer.Refuse = "A imagem tem 20000×20000 px, acima do que o editor abre.";

        await Select(vm, Gate);

        Assert.False(vm.IsEditable);
        Assert.Contains("acima do que o editor abre", vm.ErrorMessage);
    }

    [Fact]
    public async Task ChoosingSolidEraseThroughTheSegmentedControlSwitchesTheMode()
    {
        var vm = Open();
        await Select(vm, Gate);

        vm.SelectedEraseChoice = "Cor lisa";
        await vm.Previewing;

        Assert.True(vm.UsesSolidErase);
        Assert.Contains("a cor", vm.ErasedText);
    }


    [Fact]
    public async Task MarkingAThenOpeningBDuringTheRenderDoesNotGiveBTheEditOfA()
    {
        var vm = Open();
        await Select(vm, Gate);
        vm.Text = "Portão";
        await vm.Previewing;
        composer.HoldMarked = new ManualResetEventSlim();

        var marking = vm.MarkCommand.ExecuteAsync(null);
        await Select(vm, Logo);
        composer.HoldMarked.Set();
        await marking;

        Assert.Equal(Logo, vm.SelectedItem!.Path);
        Assert.Equal(string.Empty, vm.Text);
        Assert.False(vm.IsMarked);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.Equal(ImageState.Marked, vm.Items.Single(i => i.Path == Gate).State);
        Assert.Equal(ImageState.Pending, vm.SelectedItem.State);
        Assert.Equal(1, vm.MarkedCount);
        vm.UndoCommand.Execute(null);
        Assert.Equal(string.Empty, vm.Text);
    }

    [Fact]
    public async Task ADecodeErrorOfAnImageThatIsNoLongerSelectedShowsNoMessage()
    {
        var vm = Open();
        composer.HoldDecode = new ManualResetEventSlim();
        vm.SelectedItem = vm.Items.Single(i => i.Path == Gate);
        var first = vm.Selecting;

        await Select(vm, Logo);
        composer.HoldDecode.Set();
        await first;

        Assert.Equal(Logo, vm.SelectedItem!.Path);
        Assert.True(vm.IsEditable);
        Assert.Null(vm.ErrorMessage);
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

    [Fact]
    public async Task APreviewThatFailsForAnUnexpectedReasonShowsTheMessage()
    {
        var vm = Open();
        await Select(vm, Gate);

        vm.Text = "BOOM";
        await vm.Previewing;

        Assert.Contains("a fonte não desenha este texto", vm.ErrorMessage);
    }

    [Fact]
    public async Task DisposingCancelsAPendingPreviewWithoutErrors()
    {
        var vm = new ImagesViewModel([new ArchiveEngine()], composer, TimeSpan.FromMilliseconds(300));
        vm.Project = Summary();
        await vm.Loading;
        await Select(vm, Gate);
        var before = composer.Renders;
        vm.Text = "Portão";

        vm.Dispose();
        await vm.Previewing;

        Assert.Equal(before, composer.Renders);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task AThumbnailThatFailsLeavesTheReasonOnTheItem()
    {
        var vm = Open();
        composer.ThumbnailFails = true;
        var item = vm.Items.Single(i => i.Path == Gate);

        _ = item.ThumbnailPng;
        await item.ThumbnailLoading;

        Assert.Null(item.ThumbnailPng);
        Assert.Contains("formato sem miniatura", item.Caption);
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
