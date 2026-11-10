using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.Core.Library;

/// <summary>O que a pasta de um jogo da biblioteca tem agora: os arquivos como vieram, os do patch do VNForge, ou não dá para saber.</summary>
public enum LibraryTranslationKind
{
    /// <summary>Nenhuma injeção do projeto vale na pasta: o jogo está como veio.</summary>
    Original,

    /// <summary>Uma injeção do projeto está na pasta e o backup que a desfaz existe.</summary>
    Translated,

    /// <summary>Sem projeto ligado, projeto de outra pasta ou ilegível, ou backup que sumiu: o VNForge não afirma nada nem troca nada.</summary>
    Unknown,
}

/// <summary>
/// O estado de um jogo da biblioteca. <paramref name="Patch"/> é o que está na pasta (tipo, quando foi gravado, backup, impressão digital dos textos) e só existe em
/// <see cref="LibraryTranslationKind.Translated"/>; <paramref name="Reason"/> diz, em português, por que o estado é <see cref="LibraryTranslationKind.Unknown"/>.
/// <paramref name="PatchRecorded"/> é falso quando há backup vivo mas o <c>game-state.json</c> não diz o que a injeção pôs (<c>vnf patch-exe</c>, estado que não gravou): então <paramref name="Patch"/> só traz a data e o backup, e o tipo não é conhecido.
/// <paramref name="FilesChanged"/> conta os arquivos que o backup vivo guardou (os da última injeção) e <paramref name="FilesWithoutOriginal"/> os que a injeção criou, sem original para guardar; os dois só valem em <see cref="LibraryTranslationKind.Translated"/>.
/// </summary>
public sealed record LibraryTranslationState(LibraryTranslationKind Kind, GameInGame? Patch = null, string? Reason = null, bool PatchRecorded = true, int FilesChanged = 0, int FilesWithoutOriginal = 0);

/// <summary>
/// Lê o estado de um jogo da biblioteca no projeto ligado a ele, pelo mesmo <c>game-state.json</c> e pelos mesmos backups que a barra de "Testar no jogo" usa.
/// Só lê; nunca toca na pasta do jogo.
/// </summary>
public static class LibraryTranslationReader
{
    public static LibraryTranslationState Read(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Read(game, out _);
    }

    /// <summary>Abre o projeto ligado a <paramref name="game"/> para quem vai gravar, ou recusa com o motivo.</summary>
    /// <exception cref="LibraryException">Não há projeto ligado, ele não abre, ou aponta para outra pasta de jogo.</exception>
    internal static ProjectFolder OpenProject(LibraryGame game)
    {
        var state = Read(game, out var project);
        return project is not null && state.Kind != LibraryTranslationKind.Unknown
            ? project
            : throw new LibraryException(Refusal(game, state));
    }

    internal static string Refusal(LibraryGame game, LibraryTranslationState state) =>
        $"Não dá para trocar {game.Title}: {state.Reason ?? "o estado do jogo é desconhecido."}";

    private static LibraryTranslationState Read(LibraryGame game, out ProjectFolder? opened)
    {
        opened = null;
        if (string.IsNullOrWhiteSpace(game.ProjectPath))
        {
            return Unknown("nenhum projeto de tradução está ligado a este jogo (vnf library link).");
        }

        ProjectFolder project;
        string configured;
        try
        {
            project = ProjectFolder.Open(game.ProjectPath);
            configured = Path.GetFullPath(project.LoadConfig().Game.Path, project.Root);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unknown($"o projeto ligado em '{game.ProjectPath}' não abre: {e.Message}");
        }

        if (!GameLibrary.IsUnder(configured, game.Folder) || !GameLibrary.IsUnder(game.Folder, configured))
        {
            return Unknown($"o projeto ligado aponta para outra pasta de jogo ('{configured}'), e o VNForge só grava na pasta deste jogo ('{game.Folder}').");
        }

        try
        {
            var state = StateOf(project, game.Folder);
            opened = project;
            return state;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return Unknown($"não foi possível ler os backups do projeto: {e.Message}");
        }
    }

    /// <summary>
    /// Mesma regra da faixa do app: vale o backup mais novo que ainda não foi restaurado, e o <c>game-state.json</c> só vale se não houve injeção depois dele.
    /// Sem backup vivo, o estado que cita um backup que sumiu deixa o jogo desconhecido; um backup já restaurado, não.
    /// </summary>
    private static LibraryTranslationState StateOf(ProjectFolder project, string gameFolder)
    {
        var backups = GameInjector.ListBackups(project);
        var saved = GameStateFile.Load(project).InGame;
        foreach (var backup in backups)
        {
            if (backup.Manifest is null)
            {
                return Unknown($"a cópia de segurança '{backup.Name}' tem um manifesto ilegível, então não dá para saber o que está no jogo nem devolvê-lo sozinho. {backup.Problem}");
            }

            if (backup.Restored)
            {
                continue;
            }

            // A restauração devolve a pasta do manifesto, não a do jogo da Biblioteca: se forem diferentes, trocar mexeria em outro jogo.
            if (!GameLibrary.IsUnder(backup.Manifest.GameDirectory, gameFolder) || !GameLibrary.IsUnder(gameFolder, backup.Manifest.GameDirectory))
            {
                return Unknown($"a cópia de segurança '{backup.Name}' é de outra pasta de jogo ('{backup.Manifest.GameDirectory}'), e o VNForge só troca a pasta deste jogo ('{gameFolder}').");
            }

            // Sem estado que combine com o backup (vnf patch-exe, um estado que não gravou, outra versão do arquivo) o VNForge sabe que há injeção, mas não o que ela pôs.
            var changed = backup.Manifest.Files.Count;
            var created = backup.Manifest.Files.Count(f => !f.Existed);
            return saved is not null && saved.AppliedAt >= backup.Manifest.CreatedAt
                ? new LibraryTranslationState(LibraryTranslationKind.Translated, saved, FilesChanged: changed, FilesWithoutOriginal: created)
                : new LibraryTranslationState(LibraryTranslationKind.Translated, new GameInGame(GameTestKind.Translation, backup.Manifest.CreatedAt, backup.Name, null, null), PatchRecorded: false, FilesChanged: changed, FilesWithoutOriginal: created);
        }

        if (saved?.BackupDirectory is { } name && !backups.Any(b => string.Equals(b.Name, name, StringComparison.Ordinal)))
        {
            return Unknown("o projeto diz que o jogo tem a tradução, mas a cópia de segurança com os originais não está mais nele. Para voltar ao original, reinstale o jogo ou use a verificação de arquivos da loja.");
        }

        return new LibraryTranslationState(LibraryTranslationKind.Original);
    }

    private static LibraryTranslationState Unknown(string reason) => new(LibraryTranslationKind.Unknown, null, reason);
}
