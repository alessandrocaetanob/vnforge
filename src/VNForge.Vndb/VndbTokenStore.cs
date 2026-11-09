using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>Cofre do token do VNDB do usuário. O token nunca é gravado em texto puro nem em arquivo do projeto ou da Biblioteca (regra 4 do CLAUDE.md).</summary>
public interface IVndbTokenStore
{
    /// <summary>O token guardado, ou <see langword="null"/> se não há.</summary>
    string? Load();

    void Save(string token);

    /// <returns>Verdadeiro se havia um token guardado.</returns>
    bool Delete();
}

/// <summary>
/// Cofre do usuário com DPAPI (<see cref="DataProtectionScope.CurrentUser"/>), no mesmo molde do cofre da chave da Anthropic: o arquivo <c>vndb-token.dat</c>
/// só é legível pelo mesmo usuário do Windows, na mesma máquina.
/// </summary>
public sealed class DpapiVndbTokenStore(string directory) : IVndbTokenStore
{
    private const string FileName = "vndb-token.dat";

    // Entropia fixa do aplicativo, diferente da da chave de IA: um arquivo não abre no lugar do outro.
    private static readonly byte[] Entropy = "VNForge.VndbToken.v1"u8.ToArray();

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    public string? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(Path)));
        }
        catch (CryptographicException e)
        {
            throw new VndbException($"Não foi possível abrir o token guardado em {Path} (outro usuário ou outra máquina?). Grave de novo com 'vnf vndb token set'.", null, e);
        }
    }

    public void Save(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllBytes(Path, Protect(Encoding.UTF8.GetBytes(token)));
    }

    public bool Delete()
    {
        if (!File.Exists(Path))
        {
            return false;
        }

        File.Delete(Path);
        return true;
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser)
            : throw new VndbException("O cofre do token usa o DPAPI e só funciona no Windows. Defina a variável de ambiente VNDB_TOKEN.");

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)
            : throw new VndbException("O cofre do token usa o DPAPI e só funciona no Windows. Defina a variável de ambiente VNDB_TOKEN.");
}
