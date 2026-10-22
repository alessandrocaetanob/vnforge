using System.Text;
using VNForge.Cli;
using VNForge.Core.Logging;

Console.OutputEncoding = Encoding.UTF8;

var cancellation = new CancellationTokenSource();
var presses = 0;

// O primeiro Ctrl+C cancela o comando em andamento (saída 3 pelo ErrorHandler); o segundo encerra o processo
// na hora, para o caso de um comando que não respeita o token.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (Interlocked.Increment(ref presses) == 1)
    {
        cancellation.Cancel();
    }
    else
    {
        Environment.Exit(ErrorHandler.CancelledExitCode);
    }
};

return await VnfCli.Create().RunAsync(args, Console.Out, Console.Error, cancellation.Token);
