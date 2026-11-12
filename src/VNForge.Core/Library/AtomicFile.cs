using VNForge.Core.Projects;

namespace VNForge.Core.Library;

internal static class AtomicFile
{
    /// <summary>Grava o texto sem deixar o arquivo pela metade: escreve num temporário ao lado e troca. <paramref name="wait"/> é a espera entre as tentativas da troca (padrão: dormir).</summary>
    public static void WriteAllText(string path, string text, Action<int>? wait = null) =>
        Write(path, temporary => File.WriteAllText(temporary, text), wait);

    /// <summary>Como <see cref="WriteAllText"/>, para bytes (uma imagem não pode aparecer pela metade).</summary>
    public static void WriteAllBytes(string path, byte[] bytes, Action<int>? wait = null) =>
        Write(path, temporary => File.WriteAllBytes(temporary, bytes), wait);

    private static void Write(string path, Action<string> writeTemporary, Action<int>? wait)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var sleep = wait ?? Thread.Sleep;
            ProjectFileIo.Retry<int>(null, () =>
            {
                writeTemporary(temporary);
                return 0;
            }, sleep);
            ProjectFileIo.Move(temporary, path, overwrite: true, sleep);
        }
        finally
        {
            ProjectFileIo.DeleteQuietly(temporary);
        }
    }
}
