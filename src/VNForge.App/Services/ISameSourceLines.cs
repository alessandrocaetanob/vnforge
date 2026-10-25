namespace VNForge.App.Services;

/// <summary>O que o "Usar em todos" vai sobrescrever, para a pergunta antes de gravar.</summary>
/// <param name="Differing">Quantas das outras linhas já têm uma tradução diferente da que vai ser gravada.</param>
/// <param name="Strong">Quantas dessas já estão revisadas ou aprovadas.</param>
public sealed record SameSourcePreview(int Differing, int Strong);

/// <summary>O que o "Usar em todos" fez.</summary>
/// <param name="Applied">Quantas linhas passam a ter o texto, contando as que já o tinham.</param>
/// <param name="Requested">Quantas linhas foram pedidas.</param>
/// <param name="Changed">Quantas foram gravadas agora.</param>
/// <param name="Failed">Algum script não gravou; as linhas dele continuam como estavam.</param>
public sealed record SameSourceOutcome(int Applied, int Requested, int Changed, bool Failed);

/// <summary>As outras linhas do projeto que têm o mesmo original da linha aberta, para o "Usar em todos" do inspetor.</summary>
public interface ISameSourceLines
{
    /// <summary>Quantas outras linhas têm o mesmo original.</summary>
    int Count { get; }

    /// <summary>O que gravar <paramref name="translation"/> nessas linhas sobrescreveria.</summary>
    SameSourcePreview Inspect(string translation);

    /// <summary>Grava <paramref name="translation"/> nessas linhas e registra o "Desfazer" do lote.</summary>
    SameSourceOutcome Apply(string translation);
}
