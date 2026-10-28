using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.Controls;

namespace VNForge.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
            {
                UpdateMicaFallback();
            }
        };
        UpdateMicaFallback();
        Motion.Follow(this);
        AddHandler(KeyDownEvent, OnShortcutKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => FollowTextAppearance();
        Closing += (_, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel vm && !vm.TryClose())
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>
    /// <c>?</c> abre e fecha o mapa de atalhos; <c>Esc</c> fecha. Num campo de texto o <c>?</c> é só o caractere digitado.
    /// </summary>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape && vm.IsShortcutMapOpen)
        {
            vm.CloseShortcutMapCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && TextSizeCommand(vm.Settings, e.Key) is { } command)
        {
            command.Execute(null);
            e.Handled = true;
        }
        else if (e.KeySymbol == "?" && FocusManager?.GetFocusedElement() is not (TextBox or AvaloniaEdit.Editing.TextArea))
        {
            vm.ToggleShortcutMapCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary><c>Ctrl</c> <c>+</c> e <c>Ctrl</c> <c>-</c> (também no teclado numérico) mudam o tamanho do texto; <c>Ctrl</c> <c>0</c> volta a 100%.</summary>
    private static System.Windows.Input.ICommand? TextSizeCommand(ViewModels.SettingsViewModel settings, Key key) => key switch
    {
        Key.OemPlus or Key.Add => settings.IncreaseTextSizeCommand,
        Key.OemMinus or Key.Subtract => settings.DecreaseTextSizeCommand,
        Key.D0 or Key.NumPad0 => settings.ResetTextSizeCommand,
        _ => null,
    };

    private ViewModels.MainWindowViewModel? followed;

    private void FollowTextAppearance()
    {
        if (followed is not null)
        {
            followed.PropertyChanged -= OnFollowedChanged;
            followed.Settings.TextAppearanceChanged -= OnTextAppearanceChanged;
        }

        followed = DataContext as ViewModels.MainWindowViewModel;
        if (followed is not null)
        {
            followed.PropertyChanged += OnFollowedChanged;
            followed.Settings.TextAppearanceChanged += OnTextAppearanceChanged;
        }

        ApplyTextAppearance();
    }

    private void OnFollowedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.CurrentPage))
        {
            ApplyTextAppearance();
        }
    }

    private void OnTextAppearanceChanged(object? sender, EventArgs e) => ApplyTextAppearance();

    /// <summary>
    /// A fonte do japonês vale para a janela toda. O tamanho do texto vale só no Editor (grade, inspetor e Modo revisão): a área das páginas recebe os
    /// tokens escalados enquanto ele é a página, e o trilho, a barra de título e as outras telas ficam em 100%.
    /// </summary>
    private void ApplyTextAppearance()
    {
        if (followed is null)
        {
            return;
        }

        var settings = followed.Settings;
        Resources["Vnf.FontJp"] = Services.TextAppearance.JapaneseFamily(settings.JapaneseFont);
        PageArea.Resources = Services.TextAppearance.Build(followed.CurrentPage is ViewModels.EditorViewModel ? settings.TextScalePercent : Services.AppSettings.DefaultTextScale);
    }

    /// <summary>O Windows desliga a transparência em sessão remota e em economia de bateria; sem Mica, a reserva bg-base cobre o fundo.</summary>
    private void UpdateMicaFallback() =>
        MicaFallback.IsVisible = ActualTransparencyLevel != WindowTransparencyLevel.Mica;
}
