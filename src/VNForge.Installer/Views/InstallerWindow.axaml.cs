using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VNForge.Installer.ViewModels;

namespace VNForge.Installer.Views;

public sealed partial class InstallerWindow : Window
{
    private InstallerViewModel? _model;

    public InstallerWindow()
    {
        InitializeComponent();
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        Closing += (_, e) =>
        {
            // Com o instalador trabalhando no jogo, fechar a janela não é cancelar: o botão Cancelar existe para isso, e só nas etapas em que dá.
            e.Cancel = _model is { IsBusy: true };
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.PickFolder = null;
        }

        _model = DataContext as InstallerViewModel;
        if (_model is not null)
        {
            _model.CloseRequested += OnCloseRequested;
            _model.PickFolder = PickFolderAsync;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Escolha a pasta onde o jogo está instalado", AllowMultiple = false });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }
}
