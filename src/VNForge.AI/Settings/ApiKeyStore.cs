using System.Security.Cryptography;
using System.Text;

namespace VNForge.AI.Settings;

/// <summary>Cofre da chave de API do usuário. A chave nunca é gravada em texto puro nem em arquivo do projeto.</summary>
public interface IApiKeyStore
{
    /// <summary>A chave guardada, ou <see langword="null"/> se não há.</summary>
    string? Load();

    void Save(string apiKey);

    /// <returns>Verdadeiro se havia uma chave guardada.</returns>
    bool Delete();
}

/// <summary>
/// Cofre do usuário com DPAPI (<see cref="DataProtectionScope.CurrentUser"/>): o arquivo <c>apikey.dat</c> só é
/// legível pelo mesmo usuário do Windows, na mesma máquina.
/// </summary>
public sealed class DpapiApiKeyStore(string directory) : IApiKeyStore
{
    private const string FileName = "apikey.dat";

    // Entropia fixa do aplicativo: impede que outro programa do mesmo usuário abra o arquivo com a chamada padrão.
    private static readonly byte[] Entropy = "VNForge.ApiKey.v1"u8.ToArray();

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
            throw new AiSettingsException(
                $"Não foi possível abrir a chave guardada em {Path} (outro usuário ou outra máquina?). Grave de novo com 'vnf config ai --set-key'.", e);
        }
    }

    public void Save(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllBytes(Path, Protect(Encoding.UTF8.GetBytes(apiKey)));
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
            : throw new AiSettingsException("O cofre de chaves usa o DPAPI e só funciona no Windows. Defina a variável de ambiente ANTHROPIC_API_KEY.");

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)
            : throw new AiSettingsException("O cofre de chaves usa o DPAPI e só funciona no Windows. Defina a variável de ambiente ANTHROPIC_API_KEY.");
}
