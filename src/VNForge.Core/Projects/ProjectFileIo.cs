using System.Diagnostics;

namespace VNForge.Core.Projects;

/// <summary>
/// Leitura e gravação dos arquivos do projeto que convivem com outra leitura ou gravação em andamento.
/// No Windows, trocar um arquivo por <see cref="File.Move(string, string, bool)"/> falha com <see cref="UnauthorizedAccessException"/> (acesso negado)
/// enquanto alguém o mantém aberto, mesmo que o leitor tenha aberto com <see cref="FileShare.Delete"/>, e abrir o arquivo durante a troca pode falhar com
/// <see cref="IOException"/> (violação de compartilhamento). Quem resolve é a espera: cada leitor segura o arquivo só durante a leitura (milissegundos), então
/// uma repetição curta e limitada basta; passado o limite, o erro original sobe. As chamadas são síncronas (a janela pode chamar da thread de UI), por isso o limite é pequeno.
/// O limite tem dois freios: a soma das esperas pedidas (355 ms nominais, conferida antes de cada espera) e um teto real de <see cref="RealCeiling"/>, para uma
/// <see cref="IOException"/> lenta (pasta de rede, OneDrive) não congelar a tela por tentativa multiplicada.
/// Destino somente leitura também dá acesso negado, mas é permanente: sobe na hora.
/// </summary>
internal static class ProjectFileIo
{
    private const int FirstDelayMilliseconds = 5;

    private const int MaxDelayMilliseconds = 100;

    private const int BudgetMilliseconds = 300;

    private static readonly TimeSpan RealCeiling = TimeSpan.FromSeconds(2);

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

    /// <summary>
    /// Repete <paramref name="action"/> enquanto a falha for transitória. O orçamento é a soma das esperas pedidas (5, 10, 20, 40, 80, 100 e 100 ms: sete novas tentativas),
    /// e não o relógio: com a máquina sobrecarregada uma única espera pode durar mais que o orçamento inteiro, e medir pelo relógio esgotaria as tentativas
    /// sem que o arquivo tivesse tido chance de ser solto. O orçamento é conferido antes de cada espera: 5, 10, 20, 40, 80 e 100 ms deixam a soma em 255 ms,
    /// ainda abaixo de 300, então há mais uma espera de 100 ms (355 ms) e, no total, 8 tentativas. O teto real de 2 s só vale quando as próprias tentativas são lentas.
    /// </summary>
    internal static T Retry<T>(string? writeTarget, Func<T> action)
    {
        var start = Stopwatch.GetTimestamp();
        var delay = FirstDelayMilliseconds;
        var waited = 0;
        while (true)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransient(ex, writeTarget) && waited < BudgetMilliseconds && Stopwatch.GetElapsedTime(start) < RealCeiling)
            {
                Thread.Sleep(delay);
                waited += delay;
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
