namespace VNForge.App.Controls;

/// <summary>
/// Como o app chama cada marcador do jogo. A pessoa vê "virar a página", não <c>\p</c>: o texto cru só aparece no nome acessível (doc 02, tabela de marcadores).
/// </summary>
public static class MarkerLabels
{
    /// <summary>Nomes de variável do jogo que valem "nome do jogador"; a comparação ignora maiúsculas.</summary>
    private static readonly string[] PlayerNames = ["nome", "name", "player", "playername", "myname", "hero"];

    /// <summary>O nome em português do marcador: "virar a página", "quebra de linha", "nome do jogador".</summary>
    public static string Name(string marker, MarkerKind kind)
    {
        ArgumentNullException.ThrowIfNull(marker);

        if (kind == MarkerKind.Variable)
        {
            return IsPlayerName(marker) ? "nome do jogador" : "variável do jogo";
        }

        return marker switch
        {
            @"\n" => "quebra de linha",
            @"\p" => "virar a página",
            @"\c" => "esperar o clique",
            @"\u" => "código do jogo",
            @"\\" => "barra invertida",
            @"\{" => "chave",
            _ => "código do jogo",
        };
    }

    /// <summary>O desenho que abre o selo: <c>↵</c> para a quebra de linha, <c>✱</c> para variável e nulo (o triângulo de virar a página) nos demais.</summary>
    public static string? Glyph(string marker, MarkerKind kind)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return kind == MarkerKind.Variable ? "✱" : marker == @"\n" ? "↵" : null;
    }

    /// <summary>Nome acessível do marcador, para o leitor de tela: o nome em português e, entre parênteses, o marcador como está no texto.</summary>
    public static string Describe(string marker, MarkerKind kind)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return $"Marcador {Name(marker, kind)} ({marker})";
    }

    private static bool IsPlayerName(string marker)
    {
        var inner = marker.Trim('{', '}');
        var name = inner[(inner.IndexOf(':') + 1)..];
        return PlayerNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}