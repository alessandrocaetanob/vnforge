namespace VNForge.App.ViewModels;

/// <summary>A barra do topo da Ficha (#690, #692): o estado do texto em português, o spoiler e o botão principal, que muda com a etapa.</summary>
public sealed partial class GameSheetViewModel
{
    /// <summary>"Ainda em inglês" enquanto nada foi traduzido; depois, o que falta fazer com o texto.</summary>
    public string StatusTitle
    {
        get
        {
            var counts = BlockCounts;
            if (counts.Total == 0 || counts.Pending == counts.Total)
            {
                return "Ainda em inglês";
            }

            if (counts.Pending > 0)
            {
                return "Traduzida em parte";
            }

            if (counts.Machine + counts.Translated > 0)
            {
                return "Traduzida, falta revisar";
            }

            return counts.Approved == counts.Total ? "Aprovada" : "Revisada";
        }
    }

    /// <summary>Com nada traduzido a barra diz o que há no VNDB; depois disso, os selos de contagem dizem em que etapa cada bloco está.</summary>
    public bool ShowUntouchedNote => BlockCounts.Total > 0 && BlockCounts.Pending == BlockCounts.Total;

    public bool ShowCountPills => !ShowUntouchedNote && BlockCounts.Total > 0;

    public string UntouchedNote
    {
        get
        {
            var parts = Parts();
            var singular = parts is ["a sinopse"] or ["a 1 tag"] or ["o 1 personagem"];
            return $"{Capitalize(JoinParts(parts))} {(singular ? "está" : "estão")} como o VNDB entrega.";
        }
    }

    private List<string> Parts()
    {
        var parts = new List<string>();
        if (HasSynopsis)
        {
            parts.Add("a sinopse");
        }

        var tags = card is null ? 0 : card.Tags.Count + card.HiddenSpoilerTags + card.HiddenAdultTags;
        if (tags > 0)
        {
            parts.Add(tags == 1 ? "a 1 tag" : $"as {tags.ToString(System.Globalization.CultureInfo.InvariantCulture)} tags");
        }

        if (tiles.Count > 0)
        {
            parts.Add(tiles.Count == 1 ? "o 1 personagem" : $"os {tiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} personagens");
        }

        return parts;
    }

    private static string JoinParts(List<string> parts) => parts.Count switch
    {
        0 => "o texto",
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} e {parts[^1]}",
    };

    private static string Capitalize(string text) => string.Concat(char.ToUpperInvariant(text[0]), text[1..]);

    /// <summary>O botão principal da barra é "Traduzir a ficha" enquanto falta texto e "Revisar a ficha" depois; traduzir de novo vira ação discreta.</summary>
    public bool PrimaryIsTranslate => HasPending;

    public bool PrimaryIsReview => !HasPending;

    public string PrimaryTranslateLabel => BlockCounts.Pending == BlockCounts.Total ? "Traduzir a ficha" : "Traduzir o que falta";

    /// <summary>A linha sob o botão principal: o que ele faz, ou o andamento enquanto a tradução roda.</summary>
    public string PrimaryHint => IsBusy ? BusyText : "Traduz sinopse, tags e personagens com a IA dos Ajustes.";

    public bool ShowPrimaryHint => IsBusy || PrimaryIsTranslate;

    public bool ShowRetranslate => PrimaryIsReview && !IsBusy;
}
