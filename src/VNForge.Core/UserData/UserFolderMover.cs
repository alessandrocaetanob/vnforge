using System.Globalization;
using System.Text.RegularExpressions;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Patching;

namespace VNForge.Core.UserData;

/// <summary>A troca não pôde ser feita. A mensagem é em português; <see cref="Percent"/> e <see cref="Folder"/> dizem onde a cópia parou, quando ela chegou a começar.</summary>
public sealed class UserFolderException(string message, int? percent = null, string? folder = null, Exception? innerException = null, bool destinationHasData = false) : VNForgeException(message, innerException)
{
    /// <summary>A pasta nova já guarda uma biblioteca do VNForge: a pessoa ainda pode usá-la como está, sem copiar.</summary>
    public bool DestinationHasData { get; } = destinationHasData;

    public int? Percent { get; } = percent;

    /// <summary>A pasta (ou o arquivo, se estava na raiz) que a cópia estava copiando.</summary>
    public string? Folder { get; } = folder;

    /// <summary>A primeira frase da mensagem: o que deu errado, sem o detalhe.</summary>
    public string Headline
    {
        get
        {
            var end = Message.IndexOf(". ", StringComparison.Ordinal);
            return end < 0 ? Message : Message[..(end + 1)];
        }
    }

    /// <summary>O resto da mensagem depois de <see cref="Headline"/>.</summary>
    public string Detail => Message[Headline.Length..].TrimStart();
}

/// <summary>O que uma troca vai fazer, conferido antes de a pessoa confirmar.</summary>
/// <param name="Files">Os arquivos a copiar, com o caminho relativo à pasta de origem.</param>
/// <param name="NonLocal">A pasta nova está numa unidade de rede ou montada (rclone): o VNForge confere cada gravação, mas a trava pode não valer.</param>
public sealed record UserFolderMovePlan(UserFolderKind Kind, string From, string To, IReadOnlyList<string> Files, long Bytes, bool NonLocal)
{
    public int FileCount => Files.Count;
}

/// <param name="Percent">De 0 a 100, pelos bytes copiados.</param>
/// <param name="Folder">A pasta (ou o arquivo da raiz) em cópia agora.</param>
public sealed record UserFolderMoveProgress(int Percent, string Folder);

/// <summary>
/// A troca de uma das pastas do usuário: copia, confere cada arquivo, grava o ponteiro e só então vale. Cada arquivo passa por <see cref="AtomicFile.Copy"/> (releitura), o ponteiro
/// por <see cref="AtomicFile.WriteAllText(string, string)"/>, e a pasta antiga nunca é tocada antes de a cópia estar conferida. Falhou no meio, o que já tinha sido copiado é removido
/// (só o que esta troca criou) e o ponteiro fica como estava. A pasta antiga só é apagada quando a pessoa pede.
/// </summary>
public static partial class UserFolderMover
{
    private static readonly string[] UsageMarkers = [LibraryFile.FileName, "recent.json", "app.json", "apikey.dat"];

    /// <summary>Os arquivos que não entram na cópia: o ponteiro (mora na pasta Local, que é o cache padrão), as travas e os temporários de uma gravação interrompida.</summary>
    private static bool IsLeftBehind(string relative) =>
        relative.Equals(UserFolders.PointerFileName, StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
        || TemporaryName().IsMatch(relative);

    /// <summary>
    /// Confere a pasta nova e conta o que vai ser copiado.
    /// </summary>
    /// <exception cref="UserFolderException">O caminho não é completo, é a pasta atual ou fica dentro dela, já guarda dados do VNForge, não aceita gravação ou não tem espaço.</exception>
    public static UserFolderMovePlan Prepare(UserFolderKind kind, string from, string to)
    {
        var target = Normalize(to);
        var source = Normalize(from);
        if (target.Equals(source, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFolderException("Essa já é a pasta em uso. Escolha outra.");
        }

        if (IsInside(target, source))
        {
            throw new UserFolderException("A pasta nova não pode ficar dentro da pasta atual. Escolha outra.");
        }

        var files = Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(source, file))
                .Where(relative => !IsLeftBehind(relative))
                .ToList()
            : [];
        var bytes = files.Sum(relative => new FileInfo(Path.Combine(source, relative)).Length);

        if (Directory.Exists(target))
        {
            RefuseExistingData(kind, target, files);
        }

        CheckWritable(target);
        CheckSpace(target, bytes);
        return new UserFolderMovePlan(kind, source, target, files, bytes, !ProjectDisk.Current.IsLocal(target));
    }

    /// <summary>
    /// Copia, confere e grava o ponteiro. <paramref name="deleteOld"/> apaga da pasta antiga os arquivos que foram copiados, depois de tudo conferido (a pasta fica se sobrar algo que não é do VNForge).
    /// </summary>
    /// <exception cref="UserFolderException">A cópia falhou: nada mudou, o ponteiro é o de antes e a cópia parcial foi removida.</exception>
    /// <exception cref="OperationCanceledException">A pessoa cancelou: mesmo efeito de uma falha.</exception>
    public static void Move(UserFolders current, UserFolderMovePlan plan, bool deleteOld, Action<UserFolderMoveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(plan);
        var copied = new List<string>();
        var createdDirectories = new List<string>();
        var done = 0L;
        var percent = 0;
        var folder = string.Empty;
        var file = string.Empty;
        try
        {
            if (!Directory.Exists(plan.To))
            {
                MarkCreated(plan.To, createdDirectories);
            }

            foreach (var relative in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(plan.From, relative);
                var destination = Path.Combine(plan.To, relative);
                folder = TopLevel(relative);
                file = Path.GetFileName(relative);
                progress?.Invoke(new UserFolderMoveProgress(percent, folder));
                MarkCreated(Path.GetDirectoryName(destination)!, createdDirectories, plan.To);
                AtomicFile.Copy(source, destination);
                copied.Add(destination);
                done += new FileInfo(source).Length;
                percent = plan.Bytes == 0 ? 100 : (int)Math.Min(100, done * 100 / plan.Bytes);
            }

            Confirm(plan);
            var (config, cache) = current.PointerWith(plan.Kind, plan.To);
            UserFolderPointer.Save(current.PointerPath, config, cache);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Rollback(copied, createdDirectories);
            throw e as UserFolderException ?? Failure(plan, e, percent, folder, file);
        }
        catch (OperationCanceledException)
        {
            Rollback(copied, createdDirectories);
            throw;
        }

        progress?.Invoke(new UserFolderMoveProgress(100, folder));
        if (deleteOld)
        {
            DeleteOld(plan);
        }
    }

    /// <summary>Usa <paramref name="to"/> como está, sem copiar nada: a pasta já guarda uma biblioteca do VNForge (um segundo PC, uma pasta sincronizada) ou a anterior sumiu.</summary>
    /// <exception cref="UserFolderException">O caminho não é completo ou a pasta não aceita gravação.</exception>
    public static void Adopt(UserFolders current, UserFolderKind kind, string to)
    {
        ArgumentNullException.ThrowIfNull(current);
        var target = Normalize(to);
        CheckWritable(target);
        var (config, cache) = current.PointerWith(kind, target);
        try
        {
            UserFolderPointer.Save(current.PointerPath, config, cache);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UserFolderException($"Não consegui gravar a escolha em {current.PointerPath}. Feche as outras janelas do VNForge e tente de novo.", innerException: e);
        }
    }

    /// <summary>Grava o ponteiro com o caminho padrão de <paramref name="kind"/>: a escolha de voltar à pasta do Windows, ou a de confirmar a primeira pergunta sem mudar nada.</summary>
    public static void Choose(UserFolders current, UserFolderKind kind, string path)
    {
        ArgumentNullException.ThrowIfNull(current);
        var (config, cache) = current.PointerWith(kind, path);
        UserFolderPointer.Save(current.PointerPath, config, cache);
    }

    /// <summary>Grava o ponteiro com as duas pastas de uma vez: a primeira pergunta confirma o par que a pessoa viu.</summary>
    public static void Choose(UserFolders current, string config, string cache)
    {
        ArgumentNullException.ThrowIfNull(current);
        UserFolderPointer.Save(current.PointerPath, config, cache);
    }

    /// <summary>"31 MB", "248 KB": o tamanho do jeito que as telas mostram. Em MB arredonda para cima, para "precisa de" nunca ficar abaixo do real.</summary>
    public static string FormatSize(long bytes) =>
        bytes < 1024 * 1024
            ? $"{Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture)} KB"
            : $"{((bytes + 1024 * 1024 - 1) / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)} MB";

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path.Trim()))
        {
            throw InvalidPath();
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw InvalidPath();
        }
    }

    private static UserFolderException InvalidPath() =>
        new("Não consigo usar esse caminho. Informe uma pasta completa, como D:\\Dados\\VNForge.");

    private static bool IsInside(string child, string parent) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RefuseExistingData(UserFolderKind kind, string target, IReadOnlyList<string> incoming)
    {
        var holdsLibrary = kind == UserFolderKind.Config && UsageMarkers.Any(name => File.Exists(Path.Combine(target, name)));
        if (holdsLibrary || incoming.Any(relative => File.Exists(Path.Combine(target, relative))))
        {
            throw new UserFolderException($"{target} já tem uma biblioteca do VNForge. Use esta pasta como está ou escolha outra.", destinationHasData: true);
        }
    }

    /// <summary>Grava e apaga um arquivo de teste na pasta nova; as pastas que o teste teve de criar saem de novo, para a conferência não deixar pasta vazia onde a pessoa não confirmou nada.</summary>
    private static void CheckWritable(string target)
    {
        var created = new List<string>();
        MarkCreated(target, created);
        var probe = Path.Combine(target, ".vnforge-teste");
        try
        {
            AtomicFile.WriteAllText(probe, "teste");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UserFolderException($"Não consegui gravar em {target}. Escolha outra pasta ou libere a escrita nela.", innerException: e);
        }
        finally
        {
            Rollback([probe], created);
        }
    }

    private static void CheckSpace(string target, long needed)
    {
        long free;
        string drive;
        try
        {
            drive = Path.GetPathRoot(target)!;
            free = new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return;
        }

        if (free < needed)
        {
            throw new UserFolderException($"Faltou espaço em {drive.TrimEnd(Path.DirectorySeparatorChar)}. A pasta nova precisa de {FormatSize(needed)} livres e tem {FormatSize(free)}.");
        }
    }

    /// <summary>Releitura final: toda cópia existe e tem o tamanho da origem. A conferência por conteúdo já foi feita por <see cref="AtomicFile.Copy"/>.</summary>
    private static void Confirm(UserFolderMovePlan plan)
    {
        foreach (var relative in plan.Files)
        {
            var copy = new FileInfo(Path.Combine(plan.To, relative));
            if (!copy.Exists || copy.Length != new FileInfo(Path.Combine(plan.From, relative)).Length)
            {
                throw new UserFolderException($"A cópia de {relative} não bate com o original. Tente de novo.");
            }
        }
    }

    private static UserFolderException Failure(UserFolderMovePlan plan, Exception e, int percent, string folder, string file)
    {
        var message = e switch
        {
            _ when HasHResult(e, unchecked((int)0x80070070)) => $"Faltou espaço em {Path.GetPathRoot(plan.To)?.TrimEnd(Path.DirectorySeparatorChar)}. Libere espaço ou escolha outra pasta.",
            _ when HasHResult(e, unchecked((int)0x80070020)) || HasHResult(e, unchecked((int)0x80070021)) => $"O arquivo {file} está em uso. Feche as outras janelas do VNForge e a linha de comando, e tente de novo.",
            UnauthorizedAccessException => $"Não consegui gravar em {plan.To}. Escolha outra pasta ou libere a escrita nela.",
            _ => $"Não consegui copiar os dados para {plan.To}. {e.Message}",
        };
        return new UserFolderException(message, percent, folder, e);
    }

    private static bool HasHResult(Exception? e, int hresult) =>
        e is not null && (e is IOException && e.HResult == hresult || HasHResult(e.InnerException, hresult));

    private static string TopLevel(string relative)
    {
        var separator = relative.IndexOfAny(['\\', '/']);
        return separator < 0 ? relative : relative[..separator];
    }

    private static void MarkCreated(string directory, List<string> created, string? stopAt = null)
    {
        var pending = new Stack<string>();
        for (var current = directory; !string.IsNullOrEmpty(current) && !Directory.Exists(current); current = Path.GetDirectoryName(current)!)
        {
            pending.Push(current);
            if (stopAt is not null && current.Equals(stopAt, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        foreach (var path in pending)
        {
            if (!created.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                created.Add(path);
            }
        }
    }

    /// <summary>Remove só o que esta troca criou: os arquivos copiados e as pastas que ela fez e que ficaram vazias. Nada que já estava na pasta nova.</summary>
    private static void Rollback(List<string> copied, List<string> createdDirectories)
    {
        foreach (var file in copied)
        {
            ProjectFileIo.DeleteQuietly(file);
        }

        foreach (var directory in Enumerable.Reverse(createdDirectories))
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Uma pasta vazia que sobra não perde dado; o importante é não deixar arquivo copiado pela metade.
            }
        }
    }

    private static void DeleteOld(UserFolderMovePlan plan)
    {
        foreach (var relative in plan.Files)
        {
            ProjectFileIo.DeleteQuietly(Path.Combine(plan.From, relative));
        }

        foreach (var directory in Directory.EnumerateDirectories(plan.From, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A pasta antiga fica com o que sobrou; a pessoa apaga quando quiser.
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(plan.From).Any())
            {
                Directory.Delete(plan.From);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Idem.
        }
    }

    [GeneratedRegex(@"\.[0-9a-f]{32}\.tmp$")]
    private static partial Regex TemporaryName();

}
