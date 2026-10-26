using System.Text.Json;

namespace VNForge.App.Services;

/// <summary>
/// Lembra, por página, se "Detalhes técnicos" ficou aberto ou fechado. As páginas são recriadas a cada visita, então o estado vive aqui. Com uma pasta,
/// grava <c>technical-details.json</c> nas preferências do usuário (fora do projeto); sem pasta, vale só até o app fechar. É uma conveniência: arquivo
/// ilegível vale como "nada lembrado" e uma gravação que falha não atrapalha quem está traduzindo.
/// </summary>
public sealed class TechnicalDetailsMemory
{
    private const string FileName = "technical-details.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Dictionary<string, bool> states = new(StringComparer.Ordinal);
    private readonly string? path;

    public TechnicalDetailsMemory(string? directory = null)
    {
        if (directory is null)
        {
            return;
        }

        path = Path.Combine(directory, FileName);
        Load();
    }

    /// <summary>A memória que os controles consultam; o app troca por uma gravada em disco ao abrir. Os testes isolam com <see cref="Install"/>.</summary>
    public static TechnicalDetailsMemory Current { get; private set; } = new();

    /// <summary>Troca <see cref="Current"/>. Descartar o retorno volta à memória de antes.</summary>
    public static IDisposable Install(TechnicalDetailsMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var before = Current;
        Current = memory;
        return new Release(before);
    }

    /// <summary>Como a pessoa deixou a página; <see langword="null"/> quando ela nunca mexeu, e vale o que a tela define.</summary>
    public bool? Get(string key) => states.TryGetValue(key, out var open) ? open : null;

    public void Set(string key, bool open)
    {
        if (states.TryGetValue(key, out var current) && current == open)
        {
            return;
        }

        states[key] = open;
        Save();
    }

    private void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    states[property.Name] = property.Value.GetBoolean();
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            states.Clear();
        }
    }

    private void Save()
    {
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(states, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sem gravar, o estado continua valendo até o app fechar.
        }
    }

    private sealed class Release(TechnicalDetailsMemory before) : IDisposable
    {
        public void Dispose() => Current = before;
    }
}
