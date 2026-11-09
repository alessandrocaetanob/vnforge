using VNForge.Core.Memory;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>O que o inspetor do Editor pede à memória de tradução: as sugestões de uma linha, em outros projetos.</summary>
public interface IMemorySuggestions
{
    /// <summary>As traduções aprovadas em outros projetos para o original (exata primeiro, depois as aproximadas). Vazio se a memória não pôde ser lida.</summary>
    IReadOnlyList<MemoryMatch> Find(string source);

    /// <summary>Por que a memória não pôde ser lida ou gravada na última operação (com o caminho do arquivo); nulo quando tudo correu bem.</summary>
    string? Problem { get; }
}

/// <summary>
/// A memória de tradução vista de um projeto aberto no Editor. A memória é uma conveniência: um arquivo ilegível vale como "sem sugestões" e não atrapalha a edição.
/// </summary>
public sealed class ProjectMemory(TranslationMemory memory, string projectRoot, string projectName, TimeProvider clock) : IMemorySuggestions
{
    private readonly string key = TranslationMemory.KeyOf(projectRoot);
    private volatile string? problem;

    public string? Problem => problem;

    public IReadOnlyList<MemoryMatch> Find(string source)
    {
        try
        {
            var found = memory.Find(source, key, excludeProjectName: projectName);
            problem = null;
            return found;
        }
        catch (MemoryFileException e)
        {
            problem = Describe(e);
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
        catch (MemoryFileException e)
        {
            // Sem a memória o projeto segue editável; a próxima abertura tenta de novo.
            problem = Describe(e);
        }
    }

    /// <summary>Acompanha uma linha recém-gravada: aprovada entra na memória, qualquer outro estado tira o par dela.</summary>
    public void Record(TextUnit unit)
    {
        try
        {
            memory.Record(key, projectName, unit, clock.GetUtcNow());
        }
        catch (MemoryFileException e)
        {
            problem = Describe(e);
        }
    }

    private static string Describe(MemoryFileException e) => $"A memória de tradução não pôde ser lida, então não há sugestões. {e.Message} O arquivo não foi alterado.";
}
