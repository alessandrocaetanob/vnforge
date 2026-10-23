using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.Cli;

/// <summary>
/// Raiz da CLI <c>vnf</c>: comando raiz com as opções globais e a execução com log, tratamento de erros,
/// formato <c>--json</c> e códigos de saída (0 ok, 1 erro, 2 QA bloqueante, 3 cancelado).
/// </summary>
public sealed class VnfCli
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private readonly string _currentDirectory;
    private readonly string _userLogDirectory;
    private readonly Option<bool> _versionOption;

    private VnfCli(string currentDirectory, string userLogDirectory, string userConfigDirectory, TextReader input, IApiKeyStore apiKeyStore)
    {
        _currentDirectory = currentDirectory;
        _userLogDirectory = userLogDirectory;
        UserConfigDirectory = userConfigDirectory;
        Input = input;
        ApiKeyStore = apiKeyStore;

        Options = new GlobalOptions();
        _versionOption = new Option<bool>("--version") { Description = "Mostra a versão do vnf." };

        Root = new RootCommand("VNForge: ferramenta de tradução de visual novels.");
        Root.Options.Clear();
        Options.AddTo(Root);
        Root.Options.Add(_versionOption);
        Root.Options.Add(new HelpOption { Description = "Mostra esta ajuda.", Action = new PortugueseHelpAction() });
        Root.Validators.Add(ValidateGlobalOptions);
        Root.SetAction(RunRoot);
    }

    /// <summary>Comando raiz; os comandos de cada etapa do pipeline entram aqui.</summary>
    public RootCommand Root { get; }

    public GlobalOptions Options { get; }

    /// <summary>Regras de QA que o <c>vnf build</c> roda; os testes trocam por regras próprias.</summary>
    public QaEngine Qa { get; set; } = QaEngine.Default;

    /// <summary>Diretório atual da invocação, base dos caminhos relativos.</summary>
    public string CurrentDirectory => _currentDirectory;

    /// <summary>Pasta das configurações do usuário (modelo e chave de IA), sempre fora do projeto.</summary>
    public string UserConfigDirectory { get; }

    /// <summary>Entrada padrão da invocação, de onde vêm respostas e segredos que não devem ir na linha de comando.</summary>
    public TextReader Input { get; }

    /// <summary>Cofre da chave de API do usuário.</summary>
    public IApiKeyStore ApiKeyStore { get; }

    /// <summary>
    /// Cria o provedor de tradução do <c>vnf translate</c> para o modelo e o esforço escolhidos; os testes trocam por um provedor falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, ITranslationProvider>? TranslationProviderFactory { get; set; }

    /// <summary>Quem abre o jogo no <c>vnf run</c>; os testes trocam por um falso.</summary>
    public IGameProcessStarter GameProcessStarter { get; set; } = new ShellGameProcessStarter();

    /// <summary>Versão do <c>vnf</c> sem o hash do commit.</summary>
    public static string Version { get; } = ReadVersion();

    /// <param name="currentDirectory">Diretório atual; padrão: o do processo.</param>
    /// <param name="userLogDirectory">Onde o log vai fora de um projeto; padrão: <c>%LOCALAPPDATA%/VNForge/logs</c>.</param>
    /// <param name="userConfigDirectory">Configurações do usuário; padrão: <c>%APPDATA%/VNForge</c>.</param>
    /// <param name="input">Entrada padrão; padrão: <see cref="Console.In"/>.</param>
    /// <param name="apiKeyStore">Cofre da chave; padrão: DPAPI em <paramref name="userConfigDirectory"/>.</param>
    public static VnfCli Create(
        string? currentDirectory = null,
        string? userLogDirectory = null,
        string? userConfigDirectory = null,
        TextReader? input = null,
        IApiKeyStore? apiKeyStore = null)
    {
        var configDirectory = userConfigDirectory ?? AiUserSettingsFile.DefaultDirectory();
        var cli = new VnfCli(
            currentDirectory ?? Directory.GetCurrentDirectory(),
            userLogDirectory ?? VNForgeLogging.GetUserLogDirectory(),
            configDirectory,
            input ?? Console.In,
            apiKeyStore ?? new DpapiApiKeyStore(configDirectory));
        VnfCommands.Register(cli);
        return cli;
    }

    /// <summary>
    /// Interpreta <paramref name="args"/>, executa o comando e devolve o código de saída. Os resultados vão para
    /// <paramref name="output"/>; mensagens de erro vão para <paramref name="error"/>, ou para <paramref name="output"/> em JSON com <c>--json</c>.
    /// </summary>
    public async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        CultureInfo.CurrentUICulture = PortugueseBrazil;

        var parseResult = Root.Parse(args);
        var settings = Options.Read(parseResult);

        var logDirectory = VNForgeLogging.ResolveLogDirectory(settings.Project, _currentDirectory, _userLogDirectory);
        var level = settings.Verbose ? LogLevel.Debug : settings.Quiet ? LogLevel.Warning : LogLevel.Information;
        using var loggerFactory = VNForgeLogging.CreateFactoryForDirectory(logDirectory, level);
        var logger = loggerFactory.CreateLogger("vnf");

        var invocation = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };

        return await ErrorHandler.ExecuteAsync(
            async token =>
            {
                if (parseResult.Errors.Count > 0)
                {
                    throw new UsageException(
                        string.Join(" ", parseResult.Errors.Select(e => ParseErrorMessages.Translate(e.Message))) + " Use --help para ver as opções.");
                }

                ThrowIfProjectMissing(settings);
                logger.LogDebug("Executando: {Argumentos}", string.Join(' ', args));
                return await parseResult.InvokeAsync(invocation, token).ConfigureAwait(false);
            },
            logger,
            error,
            logDirectory,
            cancellationToken,
            settings.Json ? output : null).ConfigureAwait(false);
    }

    private void ThrowIfProjectMissing(GlobalSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Project))
        {
            return;
        }

        var root = Path.GetFullPath(settings.Project, _currentDirectory);
        if (!Directory.Exists(root))
        {
            throw new UsageException($"A pasta do projeto não existe: {root}");
        }
    }

    private void ValidateGlobalOptions(System.CommandLine.Parsing.CommandResult result)
    {
        if (result.GetValue(Options.Verbose) && result.GetValue(Options.Quiet))
        {
            result.AddError("As opções --verbose e --quiet não podem ser usadas juntas.");
        }
    }

    private int RunRoot(ParseResult parseResult)
    {
        var configuration = parseResult.InvocationConfiguration;
        if (!parseResult.GetValue(_versionOption))
        {
            new PortugueseHelpAction().Invoke(parseResult);
            return ErrorHandler.OkExitCode;
        }

        if (parseResult.GetValue(Options.Json))
        {
            configuration.Output.WriteLine(JsonEnvelope.Result(new { name = "vnf", version = Version }));
        }
        else
        {
            configuration.Output.WriteLine($"vnf {Version}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static string ReadVersion()
    {
        var assembly = typeof(VnfCli).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? version[..plus] : version;
    }
}
