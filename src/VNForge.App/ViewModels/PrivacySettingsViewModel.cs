using CommunityToolkit.Mvvm.ComponentModel;

namespace VNForge.App.ViewModels;

/// <summary>
/// A aba Privacidade dos Ajustes: o modo do conteúdo adulto (Mostrar, Desfocar ou Esconder). Vale para toda a Biblioteca e fica no <c>library.json</c>, o mesmo que
/// <c>vnf library privacy --adult</c> grava; quem lê e quem grava é a Biblioteca, então o atalho "Adulto" dos filtros e esta aba sempre mostram o mesmo modo.
/// </summary>
public sealed partial class PrivacySettingsViewModel : ObservableObject
{
    private readonly Func<VNForge.Core.Library.AdultMode> read;
    private readonly Func<VNForge.Core.Library.AdultMode, bool> write;
    private readonly Func<string?>? readError;

    /// <param name="write">Grava o modo; falso quando o arquivo não gravou.</param>
    /// <param name="readError">O aviso que a Biblioteca deu quando a gravação falhou.</param>
    public PrivacySettingsViewModel(Func<VNForge.Core.Library.AdultMode> read, Func<VNForge.Core.Library.AdultMode, bool> write, Func<string?>? readError = null)
    {
        this.read = read;
        this.write = write;
        this.readError = readError;
        foreach (var item in Modes)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AdultModeItem.IsSelected) && item.IsSelected && !refreshing)
                {
                    if (this.write(item.Mode))
                    {
                        ErrorMessage = null;
                    }
                    else
                    {
                        // O arquivo não gravou: o cartão volta ao modo que vale e o aviso fica na própria aba.
                        ErrorMessage = this.readError?.Invoke() ?? "O modo do conteúdo adulto não pôde ser gravado.";
                        Refresh();
                    }
                }
            };
        }

        Refresh();
    }

    private bool refreshing;

    /// <summary>Por que o modo escolhido não foi gravado; nulo quando a última escolha valeu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Os três cartões, na ordem do quadro.</summary>
    public IReadOnlyList<AdultModeItem> Modes { get; } = AdultModeItem.All();

    /// <summary>O comando de terminal que lê e grava o mesmo ajuste.</summary>
    public string CommandText => "vnf library privacy --adult mostrar|desfocar|esconder";

    /// <summary>Marca o cartão do modo de agora (a Biblioteca o mudou ou o arquivo foi relido).</summary>
    public void Refresh()
    {
        refreshing = true;
        try
        {
            var current = read();
            foreach (var item in Modes)
            {
                item.IsSelected = item.Mode == current;
            }
        }
        finally
        {
            refreshing = false;
        }
    }
}
