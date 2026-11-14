namespace VNForge.Patching;

/// <summary>
/// O que a gravação conferida (<c>AtomicFile</c>) precisa do disco e que o teste troca: se o volume do arquivo é um disco local (NTFS, ReFS, exFAT, FAT32) onde o rename sobre um
/// arquivo existente é confiável, a própria troca e a escrita do temporário. Num disco de rede ou FUSE a troca pode falhar sem avisar (rclone com cache em modo full devolve sucesso e
/// deixa o destino com 0 byte), então o Core usa outro caminho e confere o resultado.
/// </summary>
public interface IProjectDisk
{
    /// <summary>O volume de <paramref name="path"/> é um disco local onde trocar por rename é seguro. Na dúvida, falso.</summary>
    bool IsLocal(string path);

    void Move(string source, string destination, bool overwrite);

    /// <summary>Grava <paramref name="bytes"/> num arquivo novo (o temporário da troca). O padrão é <see cref="File.WriteAllBytes(string, byte[])"/>; o teste troca por uma gravação que falha no meio (disco cheio, pasta somente leitura).</summary>
    void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    /// <summary>Copia <paramref name="source"/> para <paramref name="destination"/> sobrescrevendo (o temporário da troca e a cópia de segurança). O padrão é <see cref="File.Copy(string, string, bool)"/>.</summary>
    void Copy(string source, string destination) => File.Copy(source, destination, overwrite: true);
}

/// <summary>O disco que o Core usa; <see cref="Use"/> troca por um falso só na cadeia de chamadas do teste (não vaza para testes em paralelo).</summary>
public static class ProjectDisk
{
    private static readonly AsyncLocal<IProjectDisk?> Override = new();

    private static readonly PhysicalProjectDisk Physical = new();

    public static IProjectDisk Current => Override.Value ?? Physical;

    /// <summary>Usa <paramref name="disk"/> até o descarte do retorno.</summary>
    public static IDisposable Use(IProjectDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var previous = Override.Value;
        Override.Value = disk;
        return new Restore(previous);
    }

    /// <summary>Aviso em português para o projeto num volume que não é local; nulo quando é local.</summary>
    public static string? NonLocalWarning(string path) =>
        Current.IsLocal(path)
            ? null
            : $"Aviso: '{path}' está num disco que não é local (rede, FUSE ou formato que não é de disco local). O VNForge confere cada gravação relendo o arquivo e usa um caminho mais lento; se a gravação falhar, o arquivo anterior fica guardado ao lado, com a extensão .bak.";

    private sealed class Restore(IProjectDisk? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
