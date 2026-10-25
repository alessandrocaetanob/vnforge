namespace VNForge.Core.Projects;

/// <summary>
/// Leitura e gravação dos arquivos do projeto que convivem com outra leitura ou gravação em andamento.
/// No Windows, trocar um arquivo por <see cref="File.Move(string, string, bool)"/> falha com <see cref="UnauthorizedAccessException"/> (acesso negado)
/// enquanto alguém o mantém aberto, mesmo que o leitor tenha aberto com <see cref="FileShare.Delete"/>, e abrir o arquivo durante a troca pode falhar com
/// <see cref="IOException"/> (violação de compartilhamento). Quem resolve é a espera: cada leitor segura o arquivo só durante a leitura (milissegundos), então
/// uma repetição curta e limitada basta; passado o limite, o erro original sobe. As chamadas são síncronas (a janela pode chamar da thread de UI), por isso o limite é pequeno.
/// Destino somente leitura também dá acesso negado, mas é permanente: sobe na hora.
/// </summary>
internal static class ProjectFileIo
{
    private const int FirstDelayMilliseconds = 5;

    private const int MaxDelayMilliseconds = 100;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    /// <summary>Lê o arquivo inteiro sem impedir que outro leitor ou a troca por rename o alcance.</summary>
    internal static byte[] ReadAllBytes(string path) =>
        Retry(null, () =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        });

    /// <summary>Move <paramref name="source"/> sobre <paramref name="destination"/>, esperando quem o estiver lendo.</summary>
    internal static void Move(string source, string destination, bool overwrite) =>
        Retry(destination, () =>
        {
            File.Move(source, destination, overwrite);
            return 0;
        });

    private static T Retry<T>(string? writeTarget, Func<T> action)
    {
        var deadline = DateTime.UtcNow + Budget;
        var delay = FirstDelayMilliseconds;
        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransient(ex, writeTarget) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, MaxDelayMilliseconds);
            }
        }
    }

    private static bool IsTransient(Exception ex, string? writeTarget) => ex switch
    {
        UnauthorizedAccessException => writeTarget is null || !IsReadOnly(writeTarget),
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException or EndOfStreamException => false,
        IOException => true,
        _ => false,
    };

    private static bool IsReadOnly(string path) =>
        File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
}
