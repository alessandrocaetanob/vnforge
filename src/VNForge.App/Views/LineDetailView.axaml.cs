using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Só liga o <c>TextEditor</c> (que não tem binding de texto) ao ViewModel e monta os chips do original; a lógica fica no <see cref="LineDetailViewModel"/>.</summary>
public sealed partial class LineDetailView : UserControl
{
    private static readonly TimeSpan AgeRefresh = TimeSpan.FromSeconds(30);

    /// <summary>Abaixo desta largura os botões do inspetor largam a tecla e o ícone para o texto caber.</summary>
    private const double CompactWidth = 360;

    private readonly DispatcherTimer ageTimer;
    private LineDetailViewModel? current;
    private bool syncing;

    public LineDetailView()
    {
        InitializeComponent();
        TranslationEditor.TextArea.TextView.ElementGenerators.Add(new MarkerChipGenerator());
        TranslationEditor.TextChanged += OnEditorTextChanged;

        // O "há 2 min" do cartão da IA envelhece sozinho enquanto a linha fica aberta.
        ageTimer = new DispatcherTimer { Interval = AgeRefresh };
        ageTimer.Tick += (_, _) => current?.RefreshSuggestionAge();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Classes.Set("compact", Bounds.Width is > 0 and < CompactWidth);
        }
    }

    /// <summary>Põe o cursor no campo de tradução.</summary>
    public void FocusTranslation() => TranslationEditor.TextArea.Focus();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ageTimer.Start();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (current is not null)
        {
            current.PropertyChanged -= OnViewModelPropertyChanged;
        }

        current = DataContext as LineDetailViewModel;
        SourceText.Inlines?.Clear();
        if (current is null)
        {
            return;
        }

        current.PropertyChanged += OnViewModelPropertyChanged;
        SetEditorText(current.Translation);
        foreach (var segment in current.SourceSegments)
        {
            if (!segment.IsMarker)
            {
                SourceText.Inlines?.Add(new Run(segment.Text));
                continue;
            }

            var kind = segment.Text.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control;
            // Centrado na linha: alinhado pela linha de base, o chip empurraria o texto para baixo e a linha de 24 px o cortaria.
            SourceText.Inlines?.Add(new InlineUIContainer(MarkerChipGenerator.CreateChip(segment.Text, kind)) { BaselineAlignment = BaselineAlignment.Center });
        }

        Dispatcher.UIThread.Post(() => TranslationEditor.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Sair do Editor ou fechar a janela tira o painel da tela antes do debounce; grava o que estiver pendente.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ageTimer.Stop();
        current?.Flush();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!syncing && current is not null)
        {
            current.Translation = TranslationEditor.Text ?? string.Empty;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LineDetailViewModel.Translation) && current is not null && TranslationEditor.Text != current.Translation)
        {
            SetEditorText(current.Translation);
        }
    }

    private void SetEditorText(string text)
    {
        syncing = true;
        try
        {
            TranslationEditor.Text = text;
        }
        finally
        {
            syncing = false;
        }
    }
}
