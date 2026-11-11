using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.AI.Review;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Images;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Vndb;
using VNForge.Platform.Windows;
using VNForge.Vndb;

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
    private readonly CliTerminal? _terminal;
    private bool _animationAllowed;

    private VnfCli(string currentDirectory, string userLogDirectory, string userConfigDirectory, TextReader input, IApiKeyStore apiKeyStore, IVndbTokenStore vndbTokenStore, CliTerminal? terminal)
    {
        _terminal = terminal;
        _currentDirectory = currentDirectory;
        _userLogDirectory = userLogDirectory;
        UserConfigDirectory = userConfigDirectory;
        Input = input;
        ApiKeyStore = apiKeyStore;
        VndbTokenStore = vndbTokenStore;

        Ansi = CliConsole.Create(TextWriter.Null, new GlobalSettings(null, false, false, false, false), CliTerminal.Plain());
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

    /// <summary>
    /// Console do Spectre sobre o <c>output</c> da invocação em andamento; os comandos escrevem por ele. Cor e animação
    /// só em terminal interativo, nunca com <c>--json</c>, <c>--quiet</c>, <c>--no-color</c> ou <c>NO_COLOR</c>
    /// (<see cref="RichOutput"/>). Entre invocações é um console de texto simples que descarta tudo.
    /// </summary>
    public IAnsiConsole Ansi { get; private set; }

    /// <summary>A invocação em andamento pode usar cor e animação.</summary>
    public bool RichOutput { get; private set; }

    /// <summary>
    /// A invocação pode redesenhar a tela (barras de progresso ao vivo): saída rica, com ANSI e terminal interativo. Sem isso o
    /// progresso segue como log de linhas, como antes.
    /// </summary>
    public bool CanAnimate => RichOutput && _animationAllowed && Ansi.Profile.Capabilities is { Ansi: true, Interactive: true };

    /// <summary>As perguntas da invocação em andamento terminam em quebra de linha (ver <see cref="CliTerminal.QuestionOnOwnLine"/>).</summary>
    public bool QuestionOnOwnLine { get; private set; }

    /// <summary>Cofre da chave de API do usuário.</summary>
    public IApiKeyStore ApiKeyStore { get; }

    /// <summary>Cofre do token do VNDB do usuário (a variável <c>VNDB_TOKEN</c> tem prioridade sobre ele).</summary>
    public IVndbTokenStore VndbTokenStore { get; }

    /// <summary>Lê variáveis de ambiente (<c>VNDB_TOKEN</c>); os testes trocam por um leitor que não olha o ambiente da máquina.</summary>
    public Func<string, string?> EnvironmentReader { get; set; } = Environment.GetEnvironmentVariable;

    /// <summary>
    /// Cria o provedor de tradução do <c>vnf translate</c> para o modelo e o esforço escolhidos; os testes trocam por um provedor falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, ITranslationProvider>? TranslationProviderFactory { get; set; }

    /// <summary>
    /// Cria o cliente da Message Batches API do <c>vnf translate --batch</c>; os testes trocam por um cliente falso.
    /// Padrão (<c>null</c>): o SDK com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<VNForge.AI.Batches.IMessageBatchClient>? MessageBatchClientFactory { get; set; }

    /// <summary>
    /// Cria o provedor de revisão do <c>vnf review</c> para o modelo e o esforço escolhidos; os testes trocam por um provedor falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, IReviewProvider>? ReviewProviderFactory { get; set; }

    /// <summary>Cria o cliente HTTP do download do dicionário (<c>vnf jmdict install</c>); os testes trocam por um que responde com um arquivo inventado.</summary>
    public Func<HttpClient> HttpClientFactory { get; set; } = () => new HttpClient();

    /// <summary>
    /// Cria o juiz de IA do <c>vnf eval</c> para o modelo e o esforço escolhidos; os testes trocam por um juiz falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, IJudgeProvider>? JudgeProviderFactory { get; set; }

    /// <summary>
    /// Cria o provedor da bíblia do jogo (<c>vnf bible generate</c>) para o modelo e o esforço escolhidos; os testes trocam por um provedor falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, IBibleProvider>? BibleProviderFactory { get; set; }

    /// <summary>
    /// Cria o tradutor da ficha do jogo (<c>vnf library sheet translate</c>) para o modelo e o esforço escolhidos; os testes trocam por um falso.
    /// Padrão (<c>null</c>): Claude, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, ClaudeEffort, VNForge.Core.Factsheet.ISheetTranslator>? SheetTranslatorFactory { get; set; }

    /// <summary>
    /// Cria o leitor de imagens do <c>vnf images scan</c> para o modelo escolhido; os testes trocam por um leitor falso.
    /// Padrão (<c>null</c>): Claude com visão, com a chave do ambiente ou do cofre.
    /// </summary>
    public Func<string, IImageTextReader>? ImageTextReaderFactory { get; set; }

    /// <summary>
    /// Cria o catálogo do VNDB do <c>vnf vndb</c>, do <c>vnf init --vndb</c> e do <c>vnf library match</c>; os testes trocam pelo <see cref="FakeVndbCatalog"/>, que não chama a rede.
    /// Padrão (<c>null</c>): o cliente da API do VNDB, com o cache em <c>vndb-cache</c> na pasta das configurações do usuário.
    /// </summary>
    public Func<IVndbCatalog>? VndbCatalogFactory { get; set; }

    /// <summary>
    /// Cria a lista do usuário do VNDB (<c>vnf library sync</c>, <c>vnf vndb token check</c>); os testes trocam pelo <see cref="FakeVndbCatalog"/>, que não chama a rede.
    /// Padrão (<c>null</c>): o cliente da API do VNDB.
    /// </summary>
    public Func<IVndbUserList>? VndbUserListFactory { get; set; }

    /// <summary>Quem abre o jogo no <c>vnf run</c>; os testes trocam por um falso.</summary>
    public IGameProcessStarter GameProcessStarter { get; set; } = new ShellGameProcessStarter();

    /// <summary>Quem abre o jogo e acompanha o processo no <c>vnf library play</c>; os testes trocam por um falso.</summary>
    public IGameLauncher GameLauncher { get; set; } = new ProcessGameLauncher();

    /// <summary>Quem copia a imagem da janela do jogo no <c>vnf library capture take</c>; os testes trocam por um falso.</summary>
    public IWindowCapturer WindowCapturer { get; set; } = new WindowCapturer();

    /// <summary>Onde ficam as capturas de tela (imagens e índice): a pasta de dados do usuário, nunca o projeto nem a pasta do jogo. Os testes apontam para uma pasta temporária.</summary>
    public string UserCapturesDirectory { get; set; } = GameCapturesFile.DefaultDirectory();

    /// <summary>O relógio do tempo de jogo do <c>vnf library play</c> e da data das capturas; os testes trocam por um falso.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>De quanto em quanto tempo o <c>vnf library play</c> olha o processo, e o limite do trecho suspenso que ainda conta.</summary>
    public PlayTrackingOptions PlayTracking { get; set; } = PlayTrackingOptions.Default;

    internal IVndbCatalog CreateVndbCatalog() =>
        VndbCatalogFactory?.Invoke() ?? VndbClient.Create(Path.Combine(UserConfigDirectory, "vndb-cache"));

    internal IVndbUserList CreateVndbUserList() =>
        VndbUserListFactory?.Invoke() ?? VndbClient.Create(Path.Combine(UserConfigDirectory, "vndb-cache"));

    /// <summary>O token do VNDB (variável de ambiente ou cofre) e de onde veio; nunca vai para log nem para <c>--json</c>.</summary>
    internal ResolvedVndbToken ResolveVndbToken() => VndbTokenResolver.Resolve(VndbTokenStore, EnvironmentReader);

    /// <summary>Versão do <c>vnf</c> sem o hash do commit.</summary>
    public static string Version { get; } = ReadVersion();

    /// <param name="currentDirectory">Diretório atual; padrão: o do processo.</param>
    /// <param name="userLogDirectory">Onde o log vai fora de um projeto; padrão: <c>%LOCALAPPDATA%/VNForge/logs</c>.</param>
    /// <param name="userConfigDirectory">Configurações do usuário; padrão: <c>%APPDATA%/VNForge</c>.</param>
    /// <param name="input">Entrada padrão; padrão: <see cref="Console.In"/>.</param>
    /// <param name="apiKeyStore">Cofre da chave; padrão: DPAPI em <paramref name="userConfigDirectory"/>.</param>
    /// <param name="vndbTokenStore">Cofre do token do VNDB; padrão: DPAPI em <paramref name="userConfigDirectory"/>.</param>
    /// <param name="terminal">Terminal da saída; padrão: o do processo quando a saída é o <see cref="Console.Out"/>, texto simples em qualquer outro writer.</param>
    public static VnfCli Create(
        string? currentDirectory = null,
        string? userLogDirectory = null,
        string? userConfigDirectory = null,
        TextReader? input = null,
        IApiKeyStore? apiKeyStore = null,
        CliTerminal? terminal = null,
        IVndbTokenStore? vndbTokenStore = null)
    {
        var configDirectory = userConfigDirectory ?? AiUserSettingsFile.DefaultDirectory();
        var cli = new VnfCli(
            currentDirectory ?? Directory.GetCurrentDirectory(),
            userLogDirectory ?? VNForgeLogging.GetUserLogDirectory(),
            configDirectory,
            input ?? Console.In,
            apiKeyStore ?? new DpapiApiKeyStore(configDirectory),
            vndbTokenStore ?? new DpapiVndbTokenStore(configDirectory),
            terminal);
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

        var terminal = _terminal ?? CliTerminal.ForWriter(output);
        RichOutput = CliConsole.IsRich(settings, terminal);
        _animationAllowed = terminal.Animate;
        QuestionOnOwnLine = terminal.QuestionOnOwnLine;
        Ansi = CliConsole.Create(output, settings, terminal);

        var logDirectory = VNForgeLogging.ResolveLogDirectory(settings.Project, _currentDirectory, _userLogDirectory);
        var level = LogLevelFor(settings);
        using var loggerFactory = VNForgeLogging.CreateFactoryForDirectory(logDirectory, level);
        var logger = loggerFactory.CreateLogger("vnf");

        var invocation = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };

        Exception? failure = null;
        try
        {
            var exitCode = await ErrorHandler.ExecuteAsync(
                async token =>
                {
                    try
                    {
                        if (parseResult.Errors.Count > 0)
                        {
                            throw UsageError(parseResult, logger);
                        }

                        ThrowIfProjectMissing(settings);
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.LogDebug("Executando: {Argumentos}", string.Join(' ', args));
                        }

                        return await parseResult.InvokeAsync(invocation, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        throw;
                    }
                },
                logger,
                error,
                logDirectory,
                settings.Json ? output : null,
                cancellationToken).ConfigureAwait(false);

            if (failure is not null && !settings.Json && exitCode == ErrorHandler.ErrorExitCode && CliErrorHints.For(failure) is { } hint)
            {
                await error.WriteLineAsync(CliErrorHints.Line(hint)).ConfigureAwait(false);
            }

            return exitCode;
        }
        finally
        {
            Ansi = CliConsole.Create(TextWriter.Null, new GlobalSettings(null, false, false, false, false), CliTerminal.Plain());
            RichOutput = false;
            _animationAllowed = false;
        }
    }

    /// <summary>Erro de uso do parser em português; o que não tem tradução sai genérico e o original vai para o log.</summary>
    private static UsageException UsageError(ParseResult parseResult, ILogger logger)
    {
        var messages = new List<string>();
        foreach (var original in parseResult.Errors.Select(error => error.Message))
        {
            if (!ParseErrorMessages.TryTranslate(original, out var translated))
            {
                logger.LogWarning("Mensagem de uso sem tradução: {Original}", original);
            }

            messages.Add(translated);
        }

        var commandPath = PortugueseHelpAction.CommandPath(parseResult.CommandResult.Command);
        return new UsageException(string.Join(' ', messages.Distinct(StringComparer.Ordinal)), CliErrorHints.HelpHint(commandPath));
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
            throw new UsageException($"A pasta do projeto não existe: {root}", CliErrorHints.ProjectFolderMissing);
        }
    }

    private void ValidateGlobalOptions(System.CommandLine.Parsing.CommandResult result)
    {
        if (result.GetValue(Options.Verbose) && result.GetValue(Options.Quiet))
        {
            result.AddError(ParseErrorMessages.VerboseWithQuiet);
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

    private static LogLevel LogLevelFor(GlobalSettings settings)
    {
        if (settings.Verbose)
        {
            return LogLevel.Debug;
        }

        return settings.Quiet ? LogLevel.Warning : LogLevel.Information;
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
