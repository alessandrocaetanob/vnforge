using VNForge.Core.Projects;

namespace VNForge.Core.Library;

internal static class AtomicFile
{
    /// <summary>Grava o texto sem deixar o arquivo pela metade: escreve num temporário ao lado e troca. <paramref name="wait"/> é a espera entre as tentativas da troca (padrão: dormir).</summary>
    public static void WriteAllText(string path, string text, Action<int>? wait = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, text);
            ProjectFileIo.Move(temporary, path, overwrite: true, wait ?? Thread.Sleep);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
