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

Console.CancelKeyPress += OnCancelKeyPress;
try
{
    return await VnfCli.Create().RunAsync(args, Console.Out, Console.Error, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}
