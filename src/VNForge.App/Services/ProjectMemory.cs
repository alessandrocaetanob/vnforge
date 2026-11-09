using VNForge.Core.Memory;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>O que o inspetor do Editor pede à memória de tradução: as sugestões de uma linha, em outros projetos.</summary>
public interface IMemorySuggestions
{
    /// <summary>As traduções aprovadas em outros projetos para o original (exata primeiro, depois as aproximadas). Vazio se a memória não pôde ser lida.</summary>
    IReadOnlyList<MemoryMatch> Find(string source);
}

/// <summary>
/// A memória de tradução vista de um projeto aberto no Editor. A memória é uma conveniência: um arquivo ilegível vale como "sem sugestões" e não atrapalha a edição.
/// </summary>
public sealed class ProjectMemory(TranslationMemory memory, string projectRoot, string projectName, TimeProvider clock) : IMemorySuggestions
{
    private readonly string key = TranslationMemory.KeyOf(projectRoot);

    public IReadOnlyList<MemoryMatch> Find(string source)
    {
        try
        {
            return memory.Find(source, key);
        }
        catch (MemoryFileException)
        {
            return [];
        }
    }

    /// <summary>Leva à memória os pares aprovados de hoje do projeto (<paramref name="units"/> são todas as linhas dele).</summary>
    public void Sync(IEnumerable<TextUnit> units)
    {
        try
        {
            memory.Sync(key, projectName, units, clock.GetUtcNow());
        }
        catch (MemoryFileException)
        {
            // Sem a memória o projeto segue editável; a próxima abertura tenta de novo.
        }
    }

    /// <summary>Acompanha uma linha recém-gravada: aprovada entra na memória, qualquer outro estado tira o par dela.</summary>
    public void Record(TextUnit unit)
    {
        try
        {
            memory.Record(key, projectName, unit, clock.GetUtcNow());
        }
        catch (MemoryFileException)
        {
            // Idem.
        }
    }
}
