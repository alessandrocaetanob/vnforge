namespace VNForge.App.Controls;

/// <summary>Nome acessível de um marcador, para o leitor de tela (doc 02, tabela de marcadores).</summary>
public static class MarkerLabels
{
    public static string Describe(string marker, MarkerKind kind)
    {
        ArgumentNullException.ThrowIfNull(marker);

        if (kind == MarkerKind.Variable)
        {
            return $"Variável {marker}";
        }

        return marker switch
        {
            @"\n" => @"Marcador \n, quebra de linha",
            @"\p" => @"Marcador \p, nova página",
            @"\c" => @"Marcador \c, esperar clique",
            @"\u" => @"Marcador \u, código desconhecido",
            @"\\" => @"Marcador \\, barra invertida literal",
            @"\{" => @"Marcador \{, chave literal",
            _ => $"Marcador {marker}",
        };
    }
}
