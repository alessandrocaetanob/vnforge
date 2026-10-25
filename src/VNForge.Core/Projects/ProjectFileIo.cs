namespace VNForge.Core.Projects;

/// <summary>
/// Leitura e gravação dos arquivos do projeto que convivem com outra leitura ou gravação em andamento.
/// No Windows, trocar um arquivo por <see cref="File.Move(string, string, bool)"/> falha com <see cref="UnauthorizedAccessException"/> (acesso negado)
/// enquanto alguém o mantém aberto, mesmo com <see cref="FileShare.Delete"/>, e abrir o arquivo durante a troca pode falhar com <see cref="IOException"/>
/// (violação de compartilhamento). Os dois lados duram milissegundos, então uma espera curta e limitada resolve; passado o limite, o erro original sobe.
/// </summary>
internal static class ProjectFileIo
{
    private const int FirstDelayMilliseconds = 5;

    private const int MaxDelayMilliseconds = 100;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>Lê o arquivo inteiro sem impedir que outro leitor ou a troca por rename o alcance.</summary>
    internal static byte[] ReadAllBytes(string path) =>
        Retry(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        });

    /// <summary>Move <paramref name="source"/> sobre <paramref name="destination"/>, esperando quem o estiver lendo.</summary>
    internal static void Move(string source, string destination, bool overwrite) =>
        Retry(() =>
        {
            File.Move(source, destination, overwrite);
            return 0;
        });

    private static T Retry<T>(Func<T> action)
    {
        var deadline = DateTime.UtcNow + Budget;
        var delay = FirstDelayMilliseconds;
        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransient(ex) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, MaxDelayMilliseconds);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException || ex is IOException and not FileNotFoundException and not DirectoryNotFoundException and not PathTooLongException;
}
