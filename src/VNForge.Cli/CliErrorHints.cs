using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// A segunda linha dos erros em texto: <c>→ o que fazer</c>. Vem da <see cref="UsageException.Hint"/> ou, para os erros do Core
/// que o usuário resolve sozinho, do tipo da exceção. Erro inesperado e cancelamento não têm dica.
/// </summary>
internal static class CliErrorHints
{
    public const string Arrow = "→ ";

    public const string ProjectFolderMissing =
        "Confira o caminho de --project <pasta> ou crie o projeto com 'vnf init <pasta> --game <pasta>'.";

    public const string NotAProject =
        "Rode dentro da pasta do projeto, use --project <pasta> ou crie um projeto com 'vnf init <pasta> --game <pasta>'.";

    private const string InvalidGameFile =
        "O vnf lê arquivos YPF e YBN do YU-RIS. Confira se é o arquivo certo do jogo e se não está corrompido.";

    public static string? For(Exception exception) => exception switch
    {
        UsageException { Hint: { } hint } => hint,
        ProjectConfigException when exception.Message.StartsWith($"{ProjectFolder.ConfigFileName} não encontrado", StringComparison.Ordinal) => NotAProject,
        InvalidFileException or UnknownVersionException => InvalidGameFile,
        _ => null,
    };

    public static string Line(string hint) => Arrow + hint;

    public static string HelpHint(string commandPath) => $"Rode '{commandPath} --help' para ver as opções.";
}
