namespace VNForge.Core.Memory;

/// <summary>Um par aprovado guardado na memória de tradução: o original, a tradução e de onde ele veio.</summary>
/// <param name="Source">Original da linha, como está em <c>text/*.json</c>.</param>
/// <param name="Target">Tradução aprovada.</param>
/// <param name="ProjectKey">Pasta do projeto de origem, normalizada; identifica o projeto mesmo que o nome mude.</param>
/// <param name="ProjectName">Nome do projeto de origem (<c>vnforge.json</c>) quando o par foi gravado.</param>
/// <param name="UnitId">Id da linha no projeto de origem; com <paramref name="ProjectKey"/> é a chave do par.</param>
/// <param name="ApprovedAt">Quando o par entrou na memória, ou seja, quando a aprovação foi vista pela primeira vez.</param>
public sealed record MemoryEntry(string Source, string Target, string ProjectKey, string ProjectName, string UnitId, DateTimeOffset ApprovedAt);

/// <summary>Um par da memória que serve a um original: <see cref="Percent"/> é 100 só quando o original é o mesmo.</summary>
public sealed record MemoryMatch(MemoryEntry Entry, int Percent)
{
    public bool IsExact => Percent == 100;
}

/// <summary>O que <see cref="TranslationMemory.SyncProject"/> mudou na memória.</summary>
public sealed record MemorySyncResult(int Added, int Updated, int Removed, int ProjectTotal)
{
    public bool Changed => Added + Updated + Removed > 0;
}

/// <summary>Tamanho da memória: quantos pares e de quantos projetos.</summary>
public sealed record MemoryStats(int Entries, int Projects);
