using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VNForge.Vndb;

/// <summary>
/// Cache local das respostas de consulta do VNDB (<c>/vn</c>, <c>/character</c>, <c>/release</c>), um arquivo por pedido numa pasta. A chave é o hash do caminho e do corpo do pedido,
/// então a mesma busca não gasta um lugar da fila. Cada entrada vale <see cref="MaxAge"/>; depois disso é refeita. A lista do usuário e o token nunca passam por aqui.
/// </summary>
public sealed class VndbCache
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    private readonly string _directory;
    private readonly TimeProvider _time;

    public VndbCache(string directory, TimeProvider time, TimeSpan? maxAge = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);
        _directory = directory;
        _time = time;
        MaxAge = maxAge ?? DefaultMaxAge;
    }

    public TimeSpan MaxAge { get; }

    public static string KeyFor(string path, string body)
    {
        var raw = path + '\n' + body;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>A resposta guardada, se existe e ainda vale. Arquivo ilegível conta como ausente.</summary>
    public bool TryGet(string key, out string response)
    {
        response = string.Empty;
        var file = FileFor(key);
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(file, Encoding.UTF8));
            if (entry?.Response is null || _time.GetUtcNow() - entry.FetchedAt >= MaxAge)
            {
                return false;
            }

            response = entry.Response;
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Grava a resposta num arquivo temporário e o move por cima do destino, para uma queda no meio não deixar meia entrada. Falha de disco não derruba a consulta.</summary>
    public void Set(string key, string response)
    {
        var file = FileFor(key);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(new Entry(_time.GetUtcNow(), response)), new UTF8Encoding(false));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
        }
    }

    private string FileFor(string key) => Path.Combine(_directory, key + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // O arquivo temporário que sobrar é só lixo na pasta do cache.
        }
    }

    private sealed record Entry(DateTimeOffset FetchedAt, string Response);
}
