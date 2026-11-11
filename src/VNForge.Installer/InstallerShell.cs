using System.Diagnostics;
using VNForge.Patching;

namespace VNForge.Installer;

/// <summary>O que a janela pede ao sistema: abrir o jogo e mostrar o leia-me.</summary>
public interface IInstallerShell
{
    /// <summary>O executável que o botão "Abrir o jogo" abre; nulo quando não há um palpite bom.</summary>
    string? FindGame(string folder, PatchManifest manifest);

    void OpenGame(string executablePath);

    /// <summary>Abre <paramref name="text"/> no leitor de texto do sistema.</summary>
    void OpenReadMe(string text);
}

public sealed class SystemShell : IInstallerShell
{
    public string? FindGame(string folder, PatchManifest manifest) => GameExecutable.Find(folder, manifest);

    public void OpenGame(string executablePath) =>
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executablePath) })?.Dispose();

    public void OpenReadMe(string text)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vnf-leiame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "LEIA-ME.txt");
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}

/// <summary>Descobre o executável que o botão "Abrir o jogo" abre. O patch não guarda isso; o melhor palpite é um <c>.exe</c> que ele mesmo troca.</summary>
public static class GameExecutable
{
    public static string? Find(string folder, PatchManifest manifest)
    {
        var patched = manifest.Files
            .Where(file => file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetFullPath(Path.Combine(folder, file.Path)))
            .FirstOrDefault(File.Exists);
        if (patched is not null)
        {
            return patched;
        }

        var candidates = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("vnf-", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
