using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha do popover "Adulto" e um cartão da aba Privacidade: o modo, o nome e a frase que diz o que ele faz.</summary>
public sealed partial class AdultModeItem(AdultMode mode, string name, string detail) : ObservableObject
{
    public AdultMode Mode { get; } = mode;

    public string Name { get; } = name;

    public string Detail { get; } = detail;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool IsShowMode => Mode == AdultMode.Show;

    public bool IsBlurMode => Mode == AdultMode.Blur;

    public bool IsHideMode => Mode == AdultMode.Hide;

    /// <summary>"Padrão": o modo de quem nunca escolheu.</summary>
    public bool IsDefault => Mode == AdultMode.Blur;

    /// <summary>Os três modos na ordem do quadro, com os textos do popover e da aba Privacidade.</summary>
    public static IReadOnlyList<AdultModeItem> All() =>
    [
        new(AdultMode.Show, "Mostrar", "Capa, tags, traços e imagens aparecem como vêm do VNDB."),
        new(AdultMode.Blur, "Desfocar", "Capa e imagens ficam borradas e um clique mostra. Tags e traços esperam o clique."),
        new(AdultMode.Hide, "Esconder", "O jogo adulto sai da grade, da lista, da busca, de Continuar jogando, da fila e do catálogo."),
    ];

    public static string NameOf(AdultMode mode) => mode switch
    {
        AdultMode.Show => "Mostrar",
        AdultMode.Hide => "Esconder",
        _ => "Desfocar",
    };
}

/// <summary>O conteúdo adulto na Biblioteca: o modo, o atalho nos filtros, o "Mostrar" por jogo (só nesta sessão) e o que o Esconder tira das listas.</summary>
public sealed partial class LibraryViewModel
{
    /// <summary>Os jogos que a pessoa revelou nesta sessão.</summary>
    public AdultReveal Reveal { get; } = new();

    /// <summary>O modo do conteúdo adulto, o do <c>library.json</c>.</summary>
    public AdultMode AdultMode => library.Privacy.Adult;

    /// <summary>Os três modos do popover do atalho, com o atual marcado.</summary>
    public IReadOnlyList<AdultModeItem> AdultModes { get; } = AdultModeItem.All();

    /// <summary>O texto do atalho nos filtros: "Desfocar", o modo de agora.</summary>
    public string AdultModeName => AdultModeItem.NameOf(AdultMode);

    [ObservableProperty]
    public partial bool IsAdultPopoverOpen { get; set; }

    /// <summary>Quantos jogos adultos o Esconder tirou da Biblioteca; zero nos outros modos.</summary>
    public int HiddenAdultCount => library.Privacy.Adult == AdultMode.Hide ? games.Count(g => library.Privacy.Hides(g)) : 0;

    /// <summary>O jogo mudou de modo: a Biblioteca foi relida do arquivo ou a pessoa escolheu outro. Quem mostra o modo em outro lugar (os Ajustes) o relê.</summary>
    public event EventHandler? AdultModeChanged;

    /// <summary>Os jogos que a privacidade deixa nas listas: todos, menos os adultos no Esconder.</summary>
    private IReadOnlyList<LibraryGame> Listed => library.Privacy.Listed(games);

    /// <summary>A privacidade para este jogo: o jogo que a pessoa revelou nesta sessão vale como Mostrar (capa, tags e traços), e os outros seguem o modo.</summary>
    internal LibraryPrivacy PrivacyOf(LibraryGame game) =>
        library.Privacy.Adult == AdultMode.Blur && Reveal.IsRevealed(game.VndbId) ? library.Privacy with { Adult = AdultMode.Show } : library.Privacy;

    /// <summary>Escolhe o modo e grava no <c>library.json</c>, o mesmo que <c>vnf library privacy --adult</c> grava. Um arquivo que não grava vira o aviso da página e o modo não muda.</summary>
    [RelayCommand]
    private void SetAdultMode(AdultModeItem? item)
    {
        IsAdultPopoverOpen = false;
        if (item is not null)
        {
            ChooseAdultMode(item.Mode);
        }
    }

    /// <summary>Escolhe o modo (o atalho dos filtros e a aba Privacidade dos Ajustes chamam este).</summary>
    public void ChooseAdultMode(AdultMode mode)
    {
        if (mode != library.Privacy.Adult && WriteAdultMode(mode))
        {
            Redraw();
        }
    }

    /// <summary>Grava o modo no arquivo e passa a usá-lo; falso quando o arquivo não gravou (o aviso já foi dado).</summary>
    internal bool WriteAdultMode(AdultMode mode)
    {
        try
        {
            if (file is not null)
            {
                library = file.Update(l =>
                {
                    l.Privacy = l.Privacy with { Adult = mode };
                    return l;
                });
                games = [.. library.Sorted()];
            }
            else
            {
                library.Privacy = library.Privacy with { Adult = mode };
            }
        }
        catch (Exception e) when (e is LibraryException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = e is LibraryException ? e.Message : $"O modo do conteúdo adulto não pôde ser gravado: {e.Message}";
            return false;
        }

        // Os jogos revelados valem só para o modo em que a pessoa os revelou.
        Reveal.Clear();
        return true;
    }

    /// <summary>O modo e o que depende dele (o atalho, a linha "adultos escondidos") seguem o arquivo que a Biblioteca acabou de ler ou gravar.</summary>
    private void SyncAdultMode()
    {
        foreach (var item in AdultModes)
        {
            item.IsSelected = item.Mode == library.Privacy.Adult;
        }

        OnPropertyChanged(nameof(AdultMode));
        OnPropertyChanged(nameof(AdultModeName));
        OnPropertyChanged(nameof(HiddenAdultCount));
        OnPropertyChanged(nameof(HiddenAdultText));
        OnPropertyChanged(nameof(HasHiddenAdult));
        AdultModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Alguém trocou o modo por fora (a aba Privacidade dos Ajustes, a CLI): relê o arquivo e redesenha.</summary>
    public Task ReloadPrivacyAsync() => LoadCoreAsync();

    [RelayCommand]
    private void ToggleAdultPopover() => IsAdultPopoverOpen = !IsAdultPopoverOpen;

    /// <summary>O "Mostrar" do jogo adulto: capa, tags, traços e retratos dele aparecem até a pessoa sair da Biblioteca.</summary>
    [RelayCommand]
    private void RevealGame(LibraryCardViewModel? card) => Reveal.Reveal(card?.Game.VndbId);

    /// <summary>O olho no canto da capa revelada: o jogo volta a ficar borrado.</summary>
    [RelayCommand]
    private void ConcealGame(LibraryCardViewModel? card) => Reveal.Conceal(card?.Game.VndbId);

    /// <summary>O texto do cabeçalho no Esconder: "1 adulto escondido", "3 adultos escondidos"; vazio nos outros modos ou sem jogo escondido.</summary>
    public string HiddenAdultText => HiddenAdultCount switch
    {
        0 => string.Empty,
        1 => "1 adulto escondido",
        var n => $"{n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} adultos escondidos",
    };

    public bool HasHiddenAdult => HiddenAdultCount > 0;
}
