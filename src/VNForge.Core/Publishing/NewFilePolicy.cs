namespace VNForge.Core.Publishing;

/// <summary>
/// Regra 1 do CLAUDE.md no lançamento: o patch leva só diferença de arquivo que o jogo tem. Um arquivo que o build cria e o jogo não tem iria inteiro no
/// <c>.vnfpatch</c>, então só passa o que o VNForge gera e não é conteúdo do jogo: arquivos de texto da lista abaixo. Pacotes, scripts e executáveis
/// nunca passam, nem um nome que só difere de um original na caixa (o patch não acharia o original e levaria o arquivo inteiro).
/// </summary>
internal static class NewFilePolicy
{
    private static readonly string[] TextExtensions = [".txt", ".md", ".json", ".ini", ".cfg"];

    public static void EnsureAllowed(string gameDirectory, string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        if (!TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new VNForgeException(
                $"O build criaria '{relativePath}', que o jogo não tem. O patch só leva diferença de arquivo do jogo, nunca um arquivo inteiro; só entram arquivos novos de texto ({string.Join(", ", TextExtensions)}).");
        }

        var directory = Path.GetDirectoryName(Path.Combine(gameDirectory, relativePath));
        if (directory is not null && Directory.Exists(directory)
            && Directory.EnumerateFileSystemEntries(directory).Any(e => string.Equals(Path.GetFileName(e), Path.GetFileName(relativePath), StringComparison.OrdinalIgnoreCase)))
        {
            throw new VNForgeException($"O build criaria '{relativePath}', mas o jogo já tem um arquivo com esse nome (só muda a caixa das letras). O patch não leva arquivo inteiro do jogo.");
        }
    }
}
