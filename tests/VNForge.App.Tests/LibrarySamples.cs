using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.Tests;

/// <summary>Os 12 jogos do quadro da Biblioteca (#548), montados em código: nenhum arquivo de jogo, nenhuma rede.</summary>
internal static class LibrarySamples
{
    public static readonly DateTimeOffset Now = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    public const string Synopsis = "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola do irmão e acha, no jardim do telhado, uma caixa de música que só toca quando alguém mente.";

    private static readonly LibraryEngine YuRis = new("yuris", "Yu-RIS", LibraryEngineSource.Engine);
    private static readonly LibraryEngine Siglus = new("siglus", "Siglus", LibraryEngineSource.Signature);
    private static readonly LibraryEngine KiriKiri = new("kirikiri", "KiriKiri", LibraryEngineSource.Signature);
    private static readonly LibraryEngine RenPy = new("renpy", "Ren'Py", LibraryEngineSource.Signature);

    private static LibraryTag[] Tags() =>
    [
        new("g1", "Escola", "cont", 3, 0), new("g2", "Mistério", "cont", 2.9, 0), new("g3", "Chuva", "cont", 2.8, 0), new("g4", "Drama", "cont", 2.5, 0),
        new("g5", "Irmãos", "cont", 2.4, 0), new("g6", "Música", "cont", 2.2, 0), new("g7", "Verão", "cont", 2, 0), new("g8", "Amizade", "cont", 1.9, 0),
        new("g9", "Segredos", "cont", 1.8, 0), new("g10", "Final trágico", "cont", 1.5, 2),
    ];

    private static LibraryGame Game(string folder, string title, string original, LibraryEngine engine, int? length, int? minutes, bool confirmed = true, bool adult = false, string? project = null)
    {
        var path = Path.Combine("D:\\", "Jogos", "VN", folder);
        var game = new LibraryGame(GameLibrary.IdFor(path), GameLibrary.NormalizeFolder(path), folder + ".exe", engine, title, original, Now.AddDays(-3))
        {
            ProjectPath = project,
            SearchedAt = Now.AddDays(-3),
        };
        return confirmed
            ? game with
            {
                VndbId = "v31201",
                Confirmation = LibraryConfirmation.Auto,
                Info = new LibraryVndbInfo(title, original, "2019-04-12", "https://t.vndb.org/cv/" + folder + ".jpg", "Sinopse do VNDB.", length, minutes, 40, Tags(), adult),
            }
            : game;
    }

    /// <summary>Os 12 jogos: 5 sem suporte (Siglus, três KiriKiri, Ren'Py), um sem engine, um adulto, um a confirmar e três com projeto (38%, 100% e 12%).</summary>
    public static IReadOnlyList<LibraryGame> Games()
    {
        var korin = Game("korin_no_mori", "Kōrin no Mori", "光輪の森", RenPy, null, null, confirmed: false) with
        {
            Exe = "game.exe",
            Candidates =
            [
                new LibraryCandidate("v7", "Kōrin no Mori", "光輪の森", "2021", 0.95, ["janela", "pasta", "exe"]) { Image = new VndbImageRef("https://t.vndb.org/cv/korin_no_mori.jpg"), Length = 2, LengthMinutes = 240, Rating = 76 },
                new LibraryCandidate("v8", "Kōrin no Mori: Gaiden", "光輪の森 外伝", "2022", 0.7, ["pasta"]) { Image = new VndbImageRef("https://t.vndb.org/cv/gaiden.jpg"), Length = 2, LengthMinutes = 120, Rating = 71 },
                new LibraryCandidate("v9", "Kōrin", "光輪", "2014", 0.45, []) { Image = new VndbImageRef("https://t.vndb.org/cv/korin.jpg"), Length = 2, LengthMinutes = 180, Rating = 64 },
            ],
        };
        var toge = Game("toge_no_kaji", "Tōge no Kaji", "峠の鍛冶", LibraryEngine.Unknown, null, null, confirmed: false) with { WindowTitle = "峠の鍛冶" };
        return
        [
            Game("amaoto_hakoniwa", "Amaoto no Hakoniwa", "雨音の箱庭", YuRis, 3, 1080, project: "C:\\Projetos\\amaoto"),
            Game("shirogane_st9", "Shirogane Station 9", "銀の九番線", Siglus, 4, 2520),
            Game("yugure_kissaten", "Yūgure Kissaten", "夕暮れ喫茶", KiriKiri, 2, 360),
            korin,
            Game("nanairo_ressha", "Nanairo Ressha", "七色列車", YuRis, 3, 840),
            Game("hoshikuzu_diary", "Hoshikuzu Diary", "星屑日記", YuRis, 2, 420, project: "C:\\Projetos\\hoshikuzu"),
            Game("kuroneko_archive", "Kuroneko Archive", "黒猫", KiriKiri, 3, 660, adult: true),
            toge,
            Game("namiuchigiwa", "Namiuchigiwa no Shiori", "波打ち際の栞", YuRis, 3, 1200, project: "C:\\Projetos\\namiuchigiwa"),
            Game("akatsuki_koro", "Akatsuki no Kōro", "暁の航路", YuRis, 4, 2160),
            Game("tsukiyo_hanabi", "Tsukiyo no Hanabi", "月夜の花火", YuRis, 2, 300),
            Game("fuyu_shiori", "Fuyu no Shiori", "冬の栞", KiriKiri, 3, 900),
        ];
    }

    /// <summary>O andamento dos três projetos do quadro: 38% (18.802 de 48.211), 100% e 12%.</summary>
    public static StatusCounts? Translation(LibraryGame game) => game.Title switch
    {
        "Amaoto no Hakoniwa" => new StatusCounts(48211 - 18802, 0, 0, 18802, 0),
        "Hoshikuzu Diary" => new StatusCounts(0, 0, 0, 7000, 0),
        "Namiuchigiwa no Shiori" => new StatusCounts(8800, 0, 0, 1200, 0),
        _ => null,
    };

    public static GameLibrary Library(IEnumerable<LibraryGame>? games = null) =>
        new(games ?? Games(), roots: [new LibraryRoot("D:\\Jogos\\VN", ScannedAt: Now.AddHours(-2)), new LibraryRoot("E:\\VN", ScannedAt: Now.AddHours(-5))]);

    public static LibraryFile Save(string directory, GameLibrary library)
    {
        var file = new LibraryFile(directory);
        file.Save(library);
        return file;
    }

    /// <summary>A sinopse em português revisada do quadro.</summary>
    public sealed class Sheets : ILibrarySheetSource
    {
        public LibrarySheet? SheetOf(LibraryGame game) =>
            game.Title == "Amaoto no Hakoniwa" ? new LibrarySheet(Synopsis, true, [], new Dictionary<string, string>(), SynopsisReviewed: true) : null;
    }

    /// <summary>Uma fonte de imagens que pinta um degradê por endereço, para a capa aparecer sem rede.</summary>
    public sealed class Images : IVndbImageSource
    {
        public event EventHandler? PrivacyChanged
        {
            add { }
            remove { }
        }

        public BitmapLease? TryGet(VndbImageRef image) => BitmapLease.Own(Png(image.Url));

        public void RevalidatePrivacy()
        {
        }

        public Task<BitmapLease?> LoadAsync(VndbImageRef image, CancellationToken cancellationToken) => Task.FromResult<BitmapLease?>(BitmapLease.Own(Png(image.Url)));

        private static Bitmap Png(string url)
        {
            var hue = Math.Abs(url.Aggregate(7, (h, c) => (h * 31) + c) % 360);
            const int width = 240;
            const int height = 320;
            using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var frame = bitmap.Lock();
            var pixels = new byte[frame.RowBytes * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var color = new Avalonia.Media.HslColor(1, hue, 0.55, 0.5 - (0.3 * y / height)).ToRgb();
                    var o = (y * frame.RowBytes) + (x * 4);
                    pixels[o] = color.B;
                    pixels[o + 1] = color.G;
                    pixels[o + 2] = color.R;
                    pixels[o + 3] = 255;
                }
            }

            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);

            // O Skia só redimensiona (o borrão do Desfocar) um bitmap decodificado, como o do cache; o gravável não.
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return new Bitmap(stream);
        }
    }
}
