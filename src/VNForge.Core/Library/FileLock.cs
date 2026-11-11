namespace VNForge.Core.Library;

internal static class FileLock
{
    /// <summary>
    /// Trava <paramref name="lockPath"/> abrindo-o sem compartilhamento; quem chama solta com <see cref="IDisposable.Dispose"/>. Espera outro processo soltar por até
    /// <paramref name="timeout"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> parou a espera.</exception>
    /// <exception cref="LibraryException">O arquivo seguiu em uso por mais que <paramref name="timeout"/>; a mensagem é <paramref name="busyMessage"/>.</exception>
    public static FileStream Acquire(string lockPath, TimeSpan timeout, string busyMessage, CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > timeout)
                {
                    throw new LibraryException(busyMessage, e);
                }

                Thread.Sleep(20);
            }
        }
    }
}
