using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Só liga o <c>TextEditor</c> (que não tem binding de texto) ao ViewModel e monta os chips do original; a lógica fica no <see cref="LineDetailViewModel"/>.</summary>
public sealed partial class LineDetailView : UserControl
{
    private static readonly TimeSpan AgeRefresh = TimeSpan.FromSeconds(30);

    /// <summary>Abaixo desta largura os botões do inspetor largam a tecla e o ícone para o texto caber.</summary>
    private const double CompactWidth = 360;

    /// <summary>
    /// Com <c>true</c> o inspetor se divide nas abas Tradução, Sugestão e Contexto (janela abaixo de <see cref="EditorBreakpoints"/>). O Editor define o valor
    /// e ele desce até aqui pela árvore; os mesmos blocos mudam de lugar, então o ViewModel e as ligações são os de sempre.
    /// </summary>
    public static readonly AttachedProperty<bool> UseTabsProperty =
        AvaloniaProperty.RegisterAttached<LineDetailView, Control, bool>("UseTabs", inherits: true);

    private const int TranslationTabIndex = 0;
    private const int SuggestionTabIndex = 1;

    private readonly DispatcherTimer ageTimer;
    private LineDetailViewModel? current;
    private bool syncing;
    private bool tabbed;
    private bool wasSuggesting;

    public LineDetailView()
    {
        InitializeComponent();
        TranslationEditor.TextArea.TextView.ElementGenerators.Add(new MarkerChipGenerator());
        TranslationEditor.TextChanged += OnEditorTextChanged;
        InspectorTabs.SelectionChanged += (_, _) => UpdateSuggestionTab();

        // O "há 2 min" do cartão da IA envelhece sozinho enquanto a linha fica aberta.
        ageTimer = new DispatcherTimer { Interval = AgeRefresh };
        ageTimer.Tick += (_, _) => current?.RefreshSuggestionAge();
    }

    public static bool GetUseTabs(Control control) => control.GetValue(UseTabsProperty);

    public static void SetUseTabs(Control control, bool value) => control.SetValue(UseTabsProperty, value);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateCompact();
        }
        else if (change.Property == UseTabsProperty)
        {
            ApplyTabs(change.GetNewValue<bool>());
        }
    }

    /// <summary>Com menos de 360 px (as abas e a janela mínima) a barra de ações larga a tecla e o ícone dos botões para os dois textos caberem.</summary>
    private void UpdateCompact() => Classes.Set("compact", Bounds.Width is > 0 and < CompactWidth);

    private void UpdateSuggestionTab() => Classes.Set("suggestion-tab", tabbed && InspectorTabs.SelectedIndex == SuggestionTabIndex);

    /// <summary>Na ordem do artboard, numa coluna só.</summary>
    private Control[] WideOrder =>
    [
        HeaderFragment, OriginalFragment, ExplainFragment, SceneFragment, TranslationFragment, FitFragment,
        SameSourceFragment, SuggestFragment, ProblemsFragment, ContextFragment,
    ];

    /// <summary>Muda os blocos de lugar entre a coluna única e as abas; nada é recriado, então o campo de tradução guarda o texto e o cursor.</summary>
    private void ApplyTabs(bool useTabs)
    {
        if (useTabs == tabbed)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var hadFocus = focused is not null && this.IsVisualAncestorOf(focused);
        tabbed = useTabs;
        WideStack.Children.Clear();
        TabbedHeaderHost.Child = null;
        TranslationTab.Children.Clear();
        SuggestionTab.Children.Clear();
        ContextTab.Children.Clear();

        if (useTabs)
        {
            TabbedHeaderHost.Child = HeaderFragment;
            TranslationTab.Children.AddRange([OriginalFragment, TranslationFragment, FitFragment, SameSourceFragment, ProblemsFragment]);
            SuggestionTab.Children.Add(SuggestFragment);
            ContextTab.Children.AddRange([ExplainFragment, SceneFragment, ContextFragment]);
        }
        else
        {
            WideStack.Children.AddRange(WideOrder);
        }

        WideScroll.IsVisible = !useTabs;
        TabbedRoot.IsVisible = useTabs;
        InspectorTabs.SelectedIndex = TranslationTabIndex;
        GameBox.Size = useTabs ? GameTextboxSize.Small : GameTextboxSize.Medium;
        Classes.Set("tabbed", useTabs);
        UpdateSuggestionTab();
        UpdateCompact();

        // Tirar o campo da árvore tira o foco dele, e sem foco os atalhos do inspetor deixam de responder.
        if (hadFocus)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (focused is not null && this.IsVisualAncestorOf(focused) && focused.Focusable)
                    {
                        focused.Focus();
                    }
                    else
                    {
                        FocusTranslation();
                    }
                },
                DispatcherPriority.Input);
        }
    }

    /// <summary>Troca de aba por código e deixa o foco dentro do inspetor, para Ctrl+Enter, Alt+Cima, Alt+Baixo e Ctrl+Espaço continuarem valendo.</summary>
    private void SelectTab(int index)
    {
        InspectorTabs.SelectedIndex = index;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (index == TranslationTabIndex)
                {
                    FocusTranslation();
                }
                else
                {
                    (InspectorTabs.ContainerFromIndex(index) as TabItem)?.Focus();
                }
            },
            DispatcherPriority.Input);
    }

    /// <summary>"Usar esta sugestão" da aba Sugestão: a tradução mudou, então a pessoa volta ao campo.</summary>
    private void OnUseSuggestionClick(object? sender, RoutedEventArgs e) => SelectTab(TranslationTabIndex);

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
        wasSuggesting = current?.IsSuggesting ?? false;
        SourceText.Inlines?.Clear();
        if (current is null)
        {
            return;
        }

        current.PropertyChanged += OnViewModelPropertyChanged;
        SetEditorText(current.FieldText);
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
            current.FieldText = TranslationEditor.Text ?? string.Empty;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Pedir a sugestão, de qualquer aba, leva a pessoa ao cartão da IA, uma vez por pedido: a resposta que chega depois não puxa de volta quem já saiu da aba.
        if (e.PropertyName == nameof(LineDetailViewModel.IsSuggesting) && current is { } model)
        {
            var started = model.IsSuggesting && !wasSuggesting;
            wasSuggesting = model.IsSuggesting;
            if (started && tabbed)
            {
                SelectTab(SuggestionTabIndex);
            }
        }

        if (e.PropertyName == nameof(LineDetailViewModel.FieldText) && current is not null && TranslationEditor.Text != current.FieldText)
        {
            SetEditorText(current.FieldText);
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
