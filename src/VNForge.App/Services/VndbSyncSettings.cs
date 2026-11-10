using System.Text.Json;
using VNForge.Core.Library;

namespace VNForge.App.Services;

/// <summary>O que a sincronização da lista do VNDB leva (a aba VNDB de Ajustes) e se marcar o jogo como terminado aqui também o marca no VNDB.</summary>
public sealed record VndbSyncChoices
{
    public bool Labels { get; init; } = true;

    public bool Vote { get; init; } = true;

    public bool Dates { get; init; } = true;

    public bool Notes { get; init; } = true;

    public bool MarkFinishedOnVndb { get; init; }

    public LibrarySyncFields Fields =>
        (Labels ? LibrarySyncFields.Labels : 0) | (Vote ? LibrarySyncFields.Vote : 0) | (Dates ? LibrarySyncFields.Dates : 0) | (Notes ? LibrarySyncFields.Notes : 0);
}

/// <summary>As escolhas da sincronização em <c>vndb-sync.json</c>, na pasta de configuração do usuário (nunca na de um projeto). Arquivo ausente ou ilegível vale o padrão.</summary>
public sealed class VndbSyncSettingsFile(string directory)
{
    public string Path { get; } = System.IO.Path.Combine(directory, "vndb-sync.json");

    public VndbSyncChoices Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<VndbSyncChoices>(File.ReadAllText(Path)) ?? new VndbSyncChoices() : new VndbSyncChoices();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new VndbSyncChoices();
        }
    }

    public void Save(VndbSyncChoices choices)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(choices));
    }
}
