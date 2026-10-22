using VNForge.Core.Logging;

var projectRoot = Directory.GetCurrentDirectory();
using var loggerFactory = VNForgeLogging.CreateFactory(projectRoot);
var logger = loggerFactory.CreateLogger("vnf");

return await ErrorHandler.ExecuteAsync(
    () => Task.FromResult(0),
    logger,
    Console.Error,
    VNForgeLogging.GetLogDirectory(projectRoot));
