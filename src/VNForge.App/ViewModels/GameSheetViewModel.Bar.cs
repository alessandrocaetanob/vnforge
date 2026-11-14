using System.Globalization;

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
            if (parts.Count == 0)
            {
                return "O VNDB não trouxe texto para traduzir.";
            }

            var plural = parts.Count > 1 || parts[0].Plural;
            var names = parts.Select(p => p.Text).ToList();
            var joined = names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";
            return $"{char.ToUpperInvariant(joined[0])}{joined[1..]} {(plural ? "estão" : "está")} como o VNDB entrega.";
        }
    }

    private readonly record struct SheetPart(string Text, bool Plural);

    private List<SheetPart> Parts()
    {
        var parts = new List<SheetPart>();
        if (HasSynopsis)
        {
            parts.Add(new SheetPart("a sinopse", false));
        }

        var tags = card is null ? 0 : card.Tags.Count + card.HiddenSpoilerTags + card.HiddenAdultTags;
        if (tags > 0)
        {
            parts.Add(new SheetPart($"{(tags == 1 ? "a" : "as")} {tags.ToString(CultureInfo.InvariantCulture)} {(tags == 1 ? "tag" : "tags")}", tags > 1));
        }

        if (tiles.Count > 0)
        {
            parts.Add(new SheetPart($"{(tiles.Count == 1 ? "o" : "os")} {tiles.Count.ToString(CultureInfo.InvariantCulture)} {(tiles.Count == 1 ? "personagem" : "personagens")}", tiles.Count > 1));
        }

        return parts;
    }

    /// <summary>O botão principal da barra é "Traduzir a ficha" enquanto falta texto e "Revisar a ficha" depois; traduzir de novo vira ação discreta.</summary>
    public bool PrimaryIsTranslate => HasPending;

    public bool PrimaryIsReview => !HasPending;

    public string PrimaryTranslateLabel => BlockCounts.Pending == BlockCounts.Total ? "Traduzir a ficha" : "Traduzir o que falta";

    /// <summary>A linha sob o botão principal: o que ele faz, ou o andamento enquanto a tradução roda.</summary>
    public string PrimaryHint => IsBusy ? BusyText : "Traduz sinopse, tags e personagens com a IA dos Ajustes.";

    public bool ShowPrimaryHint => IsBusy || PrimaryIsTranslate;

    public bool ShowRetranslate => PrimaryIsReview && !IsBusy;
}
