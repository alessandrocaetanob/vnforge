using System.Security.Cryptography;

namespace VNForge.Patching.Tests;

/// <summary>
/// Um "jogo" sintético em disco: a pasta do jogo original, a pasta do build (os mesmos arquivos com pequenas trocas, mais um arquivo novo)
/// e o pacote de patch entre as duas. Os arquivos são aleatórios, para o delta não ter o que comprimir fora das trocas.
/// </summary>
internal sealed class SyntheticGame : IDisposable
{
    public const int ScriptSize = 3 * 1024 * 1024;

    public const string CreatedFile = "Game/extra/fonte.dat";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "vnf-patching-" + Guid.NewGuid().ToString("N"));

    public string Original => Path.Combine(Root, "original");

    public string Build => Path.Combine(Root, "build");

    public string Game => Path.Combine(Root, "game");

    public SyntheticGame()
    {
        Write(Original, "Game/script.ypf", Random(1, ScriptSize));
        Write(Original, "Game/Game.exe", Random(2, 200_000));
        Write(Original, "Game/system.ypf", Random(3, 500_000));
        Write(Original, "Game/leiame.txt", "Texto original"u8.ToArray());

        var script = Random(1, ScriptSize);
        Overwrite(script, 1_000_000, 300);
        Overwrite(script, 2_000_000, 120);
        Write(Build, "Game/script.ypf", script);
        var exe = Random(2, 200_000);
        Overwrite(exe, 4_000, 16);
        Write(Build, "Game/Game.exe", exe);
        Write(Build, CreatedFile, Random(9, 4_000));

        CopyDirectory(Original, Game);
    }

    public IReadOnlyList<PatchSource> Sources() =>
    [
        new("Game/script.ypf", Path.Combine(Original, "Game/script.ypf"), Path.Combine(Build, "Game/script.ypf")),
        new("Game/Game.exe", Path.Combine(Original, "Game/Game.exe"), Path.Combine(Build, "Game/Game.exe")),
        new(CreatedFile, null, Path.Combine(Build, CreatedFile)),
    ];

    public PatchInfo Info(string version = "1.2.0", string? previous = null) =>
        new("Jogo Sintético", version, previous, "Tradução: Equipe", "Leia-me");

    public MemoryStream BuildPackage(PatchInfo? info = null)
    {
        var package = new MemoryStream();
        PatchBuilder.Build(info ?? Info(), Sources(), package);
        package.Position = 0;
        return package;
    }

    public static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static void AssertSameTree(string expected, string actual, params string[] ignoreTopLevel)
    {
        var left = Files(expected, ignoreTopLevel);
        var right = Files(actual, ignoreTopLevel);
        Assert.Equal(left, right);
        foreach (var relative in left)
        {
            Assert.True(
                File.ReadAllBytes(Path.Combine(expected, relative)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(actual, relative))),
                $"{relative} difere");
        }
    }

    private static List<string> Files(string root, string[] ignore) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !ignore.Any(i => f.StartsWith(i + "/", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static byte[] Random(int seed, int length)
    {
        var bytes = new byte[length];
        new System.Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static void Overwrite(byte[] data, int at, int length)
    {
        for (var i = 0; i < length; i++)
        {
            data[at + i] = (byte)(data[at + i] ^ 0x5A);
        }
    }

    private static void Write(string root, string relative, byte[] data)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
