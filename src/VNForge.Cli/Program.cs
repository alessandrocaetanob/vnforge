using System.Text;
using VNForge.Cli;
using VNForge.Core.Logging;

Console.OutputEncoding = Encoding.UTF8;

using var cancellation = new CancellationTokenSource();
var presses = 0;

// O primeiro Ctrl+C cancela o comando em andamento (saída 3 pelo ErrorHandler); o segundo encerra o processo
// na hora, para o caso de um comando que não respeita o token.
void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    if (Interlocked.Increment(ref presses) != 1)
    {
        RestoreCursor();
        Environment.Exit(ErrorHandler.CancelledExitCode);
    }

    try
    {
        cancellation.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // O comando já terminou e o token foi descartado.
    }
}

// A barra ao vivo esconde o cursor (ESC[?25l) e só o mostra ao terminar; Environment.Exit pula esse fim.
void RestoreCursor()
{
    if (!Console.IsOutputRedirected && !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb")
    {
        Console.Out.Write("\u001B[?25h");
        Console.Out.Flush();
    }
}

Console.CancelKeyPress += OnCancelKeyPress;
try
{
    return await VnfCli.Create().RunAsync(args, Console.Out, Console.Error, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}
