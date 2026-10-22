using System.CommandLine;
using VNForge.Core.Logging;

var projectRoot = Directory.GetCurrentDirectory();
var logDirectory = VNForgeLogging.GetLogDirectory(projectRoot);
using var loggerFactory = VNForgeLogging.CreateFactory(projectRoot);
var logger = loggerFactory.CreateLogger("vnf");

var rootCommand = new RootCommand("VNForge: ferramenta de tradução de visual novels.");
var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };

return await ErrorHandler.ExecuteAsync(
    ct => rootCommand.Parse(args).InvokeAsync(invocation, ct),
    logger,
    Console.Error,
    logDirectory);
