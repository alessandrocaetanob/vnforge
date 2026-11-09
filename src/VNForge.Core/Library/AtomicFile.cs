namespace VNForge.Core.Library;

internal static class AtomicFile
{
    /// <summary>Grava o texto sem deixar o arquivo pela metade: escreve num temporário ao lado e troca.</summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
