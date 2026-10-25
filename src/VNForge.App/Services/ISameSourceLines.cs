namespace VNForge.App.Services;

/// <summary>As outras linhas do projeto que têm o mesmo original da linha aberta, para o "Usar em todos" do inspetor.</summary>
public interface ISameSourceLines
{
    /// <summary>Quantas outras linhas têm o mesmo original.</summary>
    int Count { get; }

    /// <summary>Grava <paramref name="translation"/> nessas linhas. Devolve quantas mudaram; as que já tinham o texto não contam.</summary>
    int Apply(string translation);
}
