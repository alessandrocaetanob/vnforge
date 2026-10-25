using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SkiaSharp;
using VNForge.App.Views;

namespace VNForge.App.Tests;

/// <summary>
/// Guarda o ícone do app (#292): o <c>.ico</c> gerado do <c>Brand.Seal</c> por <c>tools/New-AppIcon.ps1</c>, o <c>ApplicationIcon</c> do exe
/// (Explorer) e o <c>Icon</c> da janela (barra de título e barra de tarefas).
/// </summary>
public class AppIconTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string IcoUri = "avares://VNForge.App/Assets/VNForge.ico";
    private static readonly int[] ExpectedSizes = [16, 24, 32, 48, 256];

    // brand-shu do tema claro (docs/design/tokens.json): a tinta do selo cheio, legível sobre barra de tarefas clara e escura.
    private static readonly SKColor Shu = new(0xB8, 0x40, 0x2E);

    private sealed record IcoEntry(int Size, byte[] Png);

    private static byte[] ReadIco()
    {
        using var stream = AssetLoader.Open(new Uri(IcoUri));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static List<IcoEntry> Parse(byte[] ico)
    {
        Assert.True(ico.Length > 6, "O .ico está vazio.");
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0)); // reservado
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2)); // tipo 1 = ícone
        var count = BitConverter.ToUInt16(ico, 4);
        var entries = new List<IcoEntry>();
        for (var i = 0; i < count; i++)
        {
            var at = 6 + i * 16;
            var width = ico[at] == 0 ? 256 : ico[at];
            var height = ico[at + 1] == 0 ? 256 : ico[at + 1];
            Assert.Equal(width, height);
            var length = (int)BitConverter.ToUInt32(ico, at + 8);
            var offset = (int)BitConverter.ToUInt32(ico, at + 12);
            entries.Add(new IcoEntry(width, ico[offset..(offset + length)]));
        }

        return entries;
    }

    [Fact]
    public async Task Ico_embutido_traz_16_24_32_48_e_256()
    {
        await fixture.Session.Dispatch(
            () => Assert.Equal(ExpectedSizes, Parse(ReadIco()).Select(e => e.Size).Order().ToArray()),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cada_tamanho_decodifica_no_proprio_tamanho()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                foreach (var entry in Parse(ReadIco()))
                {
                    using var bitmap = SKBitmap.Decode(entry.Png);
                    Assert.NotNull(bitmap);
                    Assert.Equal(entry.Size, bitmap.Width);
                    Assert.Equal(entry.Size, bitmap.Height);
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Selo_cheio_tem_a_placa_em_shu_cantos_transparentes_e_o_glifo_claro()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var big = Parse(ReadIco()).Single(e => e.Size == 256);
                using var bitmap = SKBitmap.Decode(big.Png);

                Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
                Assert.Equal(0, bitmap.GetPixel(255, 255).Alpha);

                // Borda esquerda da placa, fora do glifo: shu opaco.
                var plate = bitmap.GetPixel(40, 128);
                Assert.Equal(255, plate.Alpha);
                Assert.InRange(Math.Abs(plate.Red - Shu.Red), 0, 6);
                Assert.InRange(Math.Abs(plate.Green - Shu.Green), 0, 6);
                Assert.InRange(Math.Abs(plate.Blue - Shu.Blue), 0, 6);

                // O 訳 em claro sobre a placa: uma fração visível dos pixels é clara.
                var light = 0;
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.Alpha == 255 && p.Red > 220 && p.Green > 200 && p.Blue > 190)
                        light++;
                }

                Assert.InRange(light / (double)(bitmap.Width * bitmap.Height), 0.05, 0.40);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Janela_principal_usa_o_ico_como_icone()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new MainWindow();
                try
                {
                    Assert.NotNull(window.Icon);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Csproj_define_o_ApplicationIcon_do_exe()
    {
        var csproj = Path.Combine(RepoRoot(), "src", "VNForge.App", "VNForge.App.csproj");
        var icon = XDocument.Load(csproj).Descendants("ApplicationIcon").Single().Value;
        Assert.Equal(@"Assets\VNForge.ico", icon);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "VNForge.App", icon)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VNForge.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("VNForge.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}
