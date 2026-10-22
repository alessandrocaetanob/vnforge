using System.Text;
using VNForge.Cli;

Console.OutputEncoding = Encoding.UTF8;

using var cancellation = new CancellationTokenSource();

// Ctrl+C cancela o comando em andamento; o código de saída 3 sai do ErrorHandler.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await VnfCli.Create().RunAsync(args, Console.Out, Console.Error, cancellation.Token);
