namespace VNForge.Core.Tests.Text;

public class SingleProviderRegistrationTests
{
    private const string AllowedFile = "GameEncodings.cs";

    [Fact]
    public void RegisterProvider_AparecaSoEmGameEncodings()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");

        var sources = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(src, f))
            .Where(r => !IsBuildOutput(r))
            .ToList();

        Assert.Contains(sources, r => Path.GetFileName(r) == AllowedFile);

        var offenders = sources
            .Where(r => Path.GetFileName(r) != AllowedFile)
            .Where(r => File.ReadAllText(Path.Combine(src, r)).Contains("RegisterProvider", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GameEncodings_ContemOUnicoRegistro()
    {
        string file = Directory
            .EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), AllowedFile, SearchOption.AllDirectories)
            .Single();

        string text = File.ReadAllText(file);

        Assert.Equal(1, CountOccurrences(text, "RegisterProvider"));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => segment is "bin" or "obj");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VNForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("VNForge.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }
}
