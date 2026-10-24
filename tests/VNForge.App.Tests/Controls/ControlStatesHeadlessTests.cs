using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Tests.Rendering;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Controls;

/// <summary>
/// Estados de cada controle do design v2 com o tema e os estilos reais do app, nos dois temas: pseudo-classes, partes visíveis e as cores
/// que vêm dos tokens. Pega erro de XAML, seletor que não casa e token trocado que os testes de ViewModel não veem.
/// </summary>
public class ControlStatesHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private Task OnUi(string theme, Action<Ui> test) =>
        fixture.Session.Dispatch(
            () =>
            {
                using var ui = new Ui(Variant(theme));
                test(ui);
            },
            TestContext.Current.CancellationToken);

    /// <summary>A janela de teste: criada e usada na thread da sessão headless, que é onde os controles precisam nascer.</summary>
    private sealed class Ui(ThemeVariant theme) : IDisposable
    {
        private Window? window;

        public void Show(Control content)
        {
            window = new Window { Width = 900, Height = 700, RequestedThemeVariant = theme, Content = content };
            window.Show();
            Settle();
        }

        public void Settle() => ScreenRenderer.Settle(window!);

        public void Dispose() => window?.Close();
    }

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), $"Token {key} não existe");
        return Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static T Part<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static ContentPresenter PresenterOf(Visual item) =>
        item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    // ---------------------------------------------------------------- SeverityTag

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SeverityTag_erro_aviso_e_ok_trocam_cor_icone_e_texto(string theme)
    {
        await OnUi(theme, ui =>
        {
            var error = new SeverityTag { Level = SeverityLevel.Error, Count = 2 };
            var warning = new SeverityTag { Level = SeverityLevel.Warning, Count = 1204 };
            var ok = new SeverityTag { Level = SeverityLevel.Ok };
            ui.Show(new StackPanel { Children = { error, warning, ok } });
            Assert.Contains(":error", error.Classes);
            Assert.Contains(":warning", warning.Classes);
            Assert.Contains(":ok", ok.Classes);
            Assert.Equal("2 erros", error.Text);
            Assert.Equal("1.204 avisos", warning.Text);
            Assert.Equal("Sem problemas", ok.Text);

            Assert.Equal(Token(error, "Vnf.SignalErrorBg"), ColorOf(Part<Border>(error, "PART_Root").Background));
            Assert.Equal(Token(warning, "Vnf.SignalWarningBg"), ColorOf(Part<Border>(warning, "PART_Root").Background));
            Assert.Equal(Token(ok, "Vnf.StatusApprovedBg"), ColorOf(Part<Border>(ok, "PART_Root").Background));
            Assert.Equal(Token(error, "Vnf.SignalErrorFg"), ColorOf(Part<PathIcon>(error, "PART_Icon").Foreground));

            var icons = new[] { error, warning, ok }.Select(t => Part<PathIcon>(t, "PART_Icon").Data).ToArray();
            Assert.Equal(3, icons.Distinct().Count());
            Assert.All(new[] { error, warning, ok }, t => Assert.Equal(22, t.Bounds.Height));
        });
    }

    [Fact]
    public async Task SeverityTag_anuncia_o_texto_para_leitor_de_tela()
    {
        await OnUi("Dark", ui =>
        {
            var tag = new SeverityTag { Level = SeverityLevel.Warning, Count = 1 };
            ui.Show(tag);
            Assert.Equal("1 aviso", AutomationProperties.GetName(tag));
        });
    }

    // ---------------------------------------------------------------- PipelineStepper

    private static PipelineStepper Stepper(ICommand? command = null)
    {
        var states = PipelineStepper.Resolve([true, false, false, false, false]);
        string[] titles = ["Extrair", "Traduzir", "Conferir", "Build", "Injetar"];
        return new PipelineStepper
        {
            ItemsSource = titles.Select((t, i) => new PipelineStep(i + 1, t, $"sub {t}", states[i], command, t)).ToList(),
        };
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task PipelineStepper_marca_concluida_atual_e_pendente(string theme)
    {
        await OnUi(theme, ui =>
        {
            var stepper = Stepper();
            ui.Show(stepper);
            var items = stepper.GetVisualDescendants().OfType<PipelineStepItem>().ToList();
            Assert.Equal(5, items.Count);
            Assert.Contains(":done", items[0].Classes);
            Assert.Contains(":active", items[1].Classes);
            Assert.DoesNotContain(":done", items[2].Classes);
            Assert.DoesNotContain(":active", items[2].Classes);

            Assert.True(Part<PathIcon>(items[0], "PART_Check").IsVisible);
            Assert.False(Part<TextBlock>(items[0], "PART_Number").IsVisible);
            Assert.True(Part<TextBlock>(items[2], "PART_Number").IsVisible);
            Assert.False(Part<PathIcon>(items[2], "PART_Check").IsVisible);

            Assert.Equal(Token(items[0], "Vnf.SegApproved"), ColorOf(Part<Border>(items[0], "PART_Circle").Background));
            Assert.Equal(Token(items[1], "Vnf.Accent"), ColorOf(Part<Border>(items[1], "PART_Circle").BorderBrush));
            Assert.True(Part<Border>(items[1], "PART_Underline").IsVisible);
            Assert.False(Part<Border>(items[0], "PART_Underline").IsVisible);
            Assert.Equal(Token(items[1], "Vnf.AccentText"), ColorOf(Part<TextBlock>(items[1], "PART_Number").Foreground));
        });
    }

    [Fact]
    public async Task PipelineStepper_tem_cantos_arredondados_so_nas_pontas_e_larguras_iguais()
    {
        await OnUi("Dark", ui =>
        {
            var stepper = Stepper();
            ui.Show(stepper);
            var items = stepper.GetVisualDescendants().OfType<PipelineStepItem>().ToList();
            Assert.Equal(new CornerRadius(8, 0, 0, 8), items[0].CornerRadius);
            Assert.Equal(new CornerRadius(0), items[2].CornerRadius);
            Assert.Equal(new CornerRadius(0, 8, 8, 0), items[4].CornerRadius);
            Assert.Single(items.Select(i => Math.Round(i.Bounds.Width, 1)).Distinct());
            Assert.All(items, i => Assert.True(i.Bounds.Height > 0));
        });
    }

    [Fact]
    public async Task PipelineStepper_clicar_na_etapa_dispara_o_comando_com_o_parametro()
    {
        await OnUi("Dark", ui =>
        {
            var received = new List<object?>();
            var stepper = Stepper(new RelayCommand(received.Add));
            ui.Show(stepper);
            var second = stepper.GetVisualDescendants().OfType<PipelineStepItem>().ElementAt(1);
            second.Command!.Execute(second.CommandParameter);
            Assert.Equal(["Traduzir"], received);
            Assert.Contains("Traduzir, atual", AutomationProperties.GetName(second));
        });
    }

    private sealed class RelayCommand(Action<object?> run) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run(parameter);
    }

    // ---------------------------------------------------------------- LineRow

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task LineRow_narracao_sem_traducao_aviso_e_erro(string theme)
    {
        await OnUi(theme, ui =>
        {
            var plain = new LineRow { Number = 39, Status = TranslationStatus.Approved, Speaker = "Misaki", Original = "原文", Translation = "Bom dia." };
            var narration = new LineRow { Number = 38, Status = TranslationStatus.Approved, Original = "原文", Translation = "Lá fora." };
            var empty = new LineRow { Number = 47, Status = TranslationStatus.Untranslated, Speaker = "Misaki", Original = "原文" };
            var warning = new LineRow { Number = 43, Status = TranslationStatus.Machine, Original = "原文", Translation = "x", Issue = LineIssue.Warning, IssueLabel = "1 aviso do QA" };
            var error = new LineRow { Number = 44, Status = TranslationStatus.Translated, Original = "原文", Translation = "x", Issue = LineIssue.Error, IssueLabel = "1 erro do QA" };
            ui.Show(new StackPanel { Children = { plain, narration, empty, warning, error } });
            Assert.Equal("0039", plain.NumberText);
            Assert.Equal("Misaki", plain.SpeakerText);
            Assert.Equal(LineRow.NarrationLabel, narration.SpeakerText);
            Assert.Equal(LineRow.NoTranslationLabel, empty.TranslationText);
            Assert.Contains(":narration", narration.Classes);
            Assert.DoesNotContain(":narration", plain.Classes);
            Assert.Contains(":untranslated", empty.Classes);

            Assert.Equal(Token(narration, "Vnf.TextSecondary"), ColorOf(Part<TextBlock>(narration, "PART_Speaker").Foreground));
            Assert.Equal(Token(empty, "Vnf.TextSecondary"), ColorOf(Part<TextBlock>(empty, "PART_Translation").Foreground));
            Assert.NotEqual(Token(plain, "Vnf.TextSecondary"), ColorOf(Part<TextBlock>(plain, "PART_Translation").Foreground));

            Assert.False(Part<PathIcon>(plain, "PART_Warning").IsVisible);
            Assert.False(Part<PathIcon>(plain, "PART_Error").IsVisible);
            Assert.True(Part<PathIcon>(warning, "PART_Warning").IsVisible);
            Assert.False(Part<PathIcon>(warning, "PART_Error").IsVisible);
            Assert.True(Part<PathIcon>(error, "PART_Error").IsVisible);
            Assert.Equal(Token(error, "Vnf.SignalErrorFg"), ColorOf(Part<PathIcon>(error, "PART_Error").Foreground));
            Assert.Equal("1 erro do QA", AutomationProperties.GetName(Part<PathIcon>(error, "PART_Error")));
            Assert.All(new[] { plain, warning }, r => Assert.Equal(36, r.Bounds.Height));
        });
    }

    [Fact]
    public async Task LineRow_na_lista_tem_altura_fixa_e_a_selecionada_mostra_a_barra()
    {
        await OnUi("Dark", ui =>
        {
            var list = new ListBox { Classes = { "projects", "lines" } };
            list.Items.Add(new ListBoxItem { Content = new LineRow { Number = 1 } });
            list.Items.Add(new ListBoxItem { Content = new LineRow { Number = 2 } });
            list.SelectedIndex = 1;
            ui.Show(list);
            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.All(items, i => Assert.Equal(36, i.Bounds.Height));
            Assert.False(Part<Border>(items[0], "SelectionBar").IsVisible);
            Assert.True(Part<Border>(items[1], "SelectionBar").IsVisible);
        });
    }

    // ---------------------------------------------------------------- ScriptItem

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task ScriptItem_completo_atual_traduzindo_e_sem_nome(string theme)
    {
        await OnUi(theme, ui =>
        {
            var done = new ScriptItem { Key = "yst00012", Label = "Prólogo", Percent = 100 };
            var current = new ScriptItem { Key = "yst00013", Label = "Capítulo 1", Percent = 55 };
            var running = new ScriptItem { Key = "yst00014", Label = "Capítulo 1", Percent = 35, IsRunning = true };
            var bare = new ScriptItem { Key = "yst00015" };
            var list = new ListBox { Classes = { "scripts" } };
            foreach (var item in new[] { done, current, running, bare })
            {
                list.Items.Add(new ListBoxItem { Content = item });
            }

            list.SelectedIndex = 1;

            ui.Show(list);
            Assert.Equal("100%", done.PercentText);
            Assert.Contains(":complete", done.Classes);
            Assert.Equal(Token(done, "Vnf.SegApproved"), ColorOf(Part<ProgressBar>(done, "PART_Bar").Foreground));
            Assert.Equal(Token(current, "Vnf.Accent"), ColorOf(Part<ProgressBar>(current, "PART_Bar").Foreground));
            Assert.Equal(4, Part<ProgressBar>(current, "PART_Bar").Bounds.Height);

            Assert.True(Part<PathIcon>(running, "PART_Running").IsVisible);
            Assert.False(Part<PathIcon>(current, "PART_Running").IsVisible);
            Assert.Equal(Token(running, "Vnf.StatusAiFg"), ColorOf(Part<TextBlock>(running, "PART_PercentText").Foreground));

            Assert.False(Part<TextBlock>(bare, "PART_Label").IsVisible);
            Assert.True(Part<TextBlock>(current, "PART_Label").IsVisible);

            var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.Equal(FontWeight.SemiBold, Part<TextBlock>(current, "PART_Id").FontWeight);
            Assert.NotEqual(FontWeight.SemiBold, Part<TextBlock>(done, "PART_Id").FontWeight);
            Assert.Equal(Token(items[1], "Vnf.AccentSoft"), ColorOf(PresenterOf(items[1]).Background));
        });
    }

    // ---------------------------------------------------------------- ToggleRule

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task ToggleRule_liga_desliga_e_a_ultima_linha_nao_tem_divisoria(string theme)
    {
        await OnUi(theme, ui =>
        {
            var first = new ToggleRule { Title = "Espaço duplo", Description = "antes → depois", IsChecked = false };
            var last = new ToggleRule { Title = "Sem exemplo", IsChecked = true };
            var panel = new StackPanel { Children = { first, last } };
            ui.Show(panel);
            var firstSwitch = Part<ToggleSwitch>(first, "PART_Switch");
            var lastSwitch = Part<ToggleSwitch>(last, "PART_Switch");
            Assert.False(firstSwitch.IsChecked);
            Assert.True(lastSwitch.IsChecked);

            firstSwitch.IsChecked = true;
            Assert.True(first.IsChecked);
            last.IsChecked = false;
            Assert.False(lastSwitch.IsChecked);

            Assert.True(Part<TextBlock>(first, "PART_Description").IsVisible);
            Assert.False(Part<TextBlock>(last, "PART_Description").IsVisible);
            Assert.Equal(new Thickness(0, 0, 0, 1), Part<Border>(first, "PART_Root").BorderThickness);
            Assert.Equal(new Thickness(0), Part<Border>(last, "PART_Root").BorderThickness);
            Assert.Null(firstSwitch.OnContent);
            Assert.Null(firstSwitch.OffContent);
            Assert.Equal("Espaço duplo", AutomationProperties.GetName(firstSwitch));
        });
    }

    // ---------------------------------------------------------------- Segmentado e abas

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Segmentado_marca_um_segmento_em_accent_e_troca_ao_selecionar(string theme)
    {
        await OnUi(theme, ui =>
        {
            var seg = new ListBox { Classes = { "seg" } };
            foreach (var text in new[] { "Baixo", "Médio", "Alto" })
            {
                seg.Items.Add(new ListBoxItem { Content = text });
            }

            seg.SelectedIndex = 1;

            ui.Show(seg);
            ContentPresenter Presenter(int i) => PresenterOf(seg.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(i));

            Assert.Equal(Token(seg, "Vnf.Accent"), ColorOf(Presenter(1).Background));
            Assert.Equal(Token(seg, "Vnf.OnAccent"), ColorOf(Presenter(1).Foreground));
            Assert.Equal(Token(seg, "Vnf.BgInset"), ColorOf(Presenter(0).Background));
            Assert.All(seg.GetVisualDescendants().OfType<ListBoxItem>(), i => Assert.Equal(30, i.Bounds.Height));

            seg.SelectedIndex = 2;
            ui.Settle();
            Assert.Equal(Token(seg, "Vnf.Accent"), ColorOf(Presenter(2).Background));
            Assert.Equal(Token(seg, "Vnf.BgInset"), ColorOf(Presenter(1).Background));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Abas_mostram_o_sublinhado_so_na_aba_marcada(string theme)
    {
        await OnUi(theme, ui =>
        {
            var tabs = new TabControl { Classes = { "tabs" }, SelectedIndex = 0 };
            foreach (var header in new[] { "IA", "Formatação", "Acentuação" })
            {
                tabs.Items.Add(new TabItem { Header = header, Content = new TextBlock { Text = header } });
            }

            ui.Show(tabs);
            List<TabItem> Items() => tabs.GetVisualDescendants().OfType<TabItem>().ToList();

            Assert.True(Part<Border>(Items()[0], "PART_Underline").IsVisible);
            Assert.False(Part<Border>(Items()[1], "PART_Underline").IsVisible);
            Assert.Equal(Token(tabs, "Vnf.Accent"), ColorOf(Part<Border>(Items()[0], "PART_Underline").Background));
            Assert.Equal(3, Part<Border>(Items()[0], "PART_Underline").Bounds.Height);
            Assert.All(Items(), i => Assert.Equal(40, i.Bounds.Height));

            tabs.SelectedIndex = 2;
            ui.Settle();
            Assert.False(Part<Border>(Items()[0], "PART_Underline").IsVisible);
            Assert.True(Part<Border>(Items()[2], "PART_Underline").IsVisible);
        });
    }

    // ---------------------------------------------------------------- FilterChip

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task FilterChip_marcado_ganha_accent_e_a_contagem_vem_em_pt_br(string theme)
    {
        await OnUi(theme, ui =>
        {
            var off = new FilterChip { Label = "Pendentes", Count = 18320 };
            var on = new FilterChip { Label = "Todas", Count = 48211, IsChecked = true };
            var bare = new FilterChip { Label = "Sem contagem" };
            var disabled = new FilterChip { Label = "Desativado", Count = 0, IsEnabled = false };
            ui.Show(new StackPanel { Children = { off, on, bare, disabled } });
            Assert.Equal("18.320", off.CountText);
            Assert.Equal("48.211", on.CountText);
            Assert.Null(bare.CountText);
            Assert.True(Part<TextBlock>(off, "PART_Count").IsVisible);
            Assert.False(Part<TextBlock>(bare, "PART_Count").IsVisible);

            Assert.Equal(Token(on, "Vnf.AccentSoft"), ColorOf(Part<Border>(on, "PART_Root").Background));
            Assert.Equal(Token(on, "Vnf.Accent"), ColorOf(Part<Border>(on, "PART_Root").BorderBrush));
            Assert.Equal(Token(on, "Vnf.AccentText"), ColorOf(Part<TextBlock>(on, "PART_Label").Foreground));
            Assert.Equal(FontWeight.SemiBold, Part<TextBlock>(on, "PART_Label").FontWeight);

            Assert.Equal(Token(off, "Vnf.StrokeSubtle"), ColorOf(Part<Border>(off, "PART_Root").BorderBrush));
            Assert.Equal(Token(off, "Vnf.TextSecondary"), ColorOf(Part<TextBlock>(off, "PART_Label").Foreground));
            Assert.Equal(Token(off, "Vnf.TextTertiary"), ColorOf(Part<TextBlock>(disabled, "PART_Label").Foreground));
            Assert.Equal(28, off.Bounds.Height);

            off.IsChecked = true;
            Assert.Contains(":checked", off.Classes);
        });
    }

    // ---------------------------------------------------------------- AiSuggestion

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task AiSuggestion_mostra_insercoes_em_verde_e_remocoes_riscadas(string theme)
    {
        await OnUi(theme, ui =>
        {
            var suggestion = new AiSuggestion
            {
                Model = "Claude Opus 5.5",
                Details = "· esforço médio · há 2 min",
                Current = "Não enche… Eu não achava o guarda-chuva.\\p",
                Suggestion = "Me deixa… Não achei o guarda-chuva.\\p",
            };
            ui.Show(suggestion);
            var body = Part<SelectableTextBlock>(suggestion, "PART_Body");
            var runs = body.Inlines!.OfType<Run>().Where(r => r.Text != " ").ToList();
            var inserted = runs.Where(r => r.Classes.Contains("ins")).Select(r => r.Text).ToList();
            var removed = runs.Where(r => r.Classes.Contains("del")).Select(r => r.Text).ToList();
            Assert.Equal(["Me", "deixa…", "achei"], inserted);
            Assert.Equal(["enche…", "Eu", "não", "achava"], removed);

            var ins = runs.First(r => r.Classes.Contains("ins"));
            var del = runs.First(r => r.Classes.Contains("del"));
            Assert.Equal(Token(suggestion, "Vnf.StatusApprovedBg"), ColorOf(ins.Background));
            Assert.Equal(Token(suggestion, "Vnf.StatusApprovedFg"), ColorOf(ins.Foreground));
            Assert.Equal(Token(suggestion, "Vnf.TextSecondary"), ColorOf(del.Foreground));
            Assert.Same(TextDecorations.Strikethrough, del.TextDecorations);

            Assert.Equal(Token(suggestion, "Vnf.StatusAiFg"), ColorOf(Part<TextBlock>(suggestion, "PART_Model").Foreground));
            Assert.False(Part<ProgressBar>(suggestion, "PART_Busy").IsVisible);
            Assert.True(Part<Button>(suggestion, "PART_Use").IsVisible);
            Assert.False(Part<Button>(suggestion, "PART_Cancel").IsVisible);
        });
    }

    [Fact]
    public async Task AiSuggestion_sem_traducao_atual_nao_tem_marcacao_e_gerando_troca_o_botao()
    {
        await OnUi("Dark", ui =>
        {
            var suggestion = new AiSuggestion { Model = "Claude Opus 5.5", Suggestion = "Ehehe… Desculpaaa." };
            ui.Show(suggestion);
            var body = Part<SelectableTextBlock>(suggestion, "PART_Body");
            Assert.All(body.Inlines!.OfType<Run>(), r => Assert.DoesNotContain("ins", r.Classes));
            Assert.All(body.Inlines!.OfType<Run>(), r => Assert.DoesNotContain("del", r.Classes));

            suggestion.IsBusy = true;
            ui.Settle();
            Assert.True(Part<ProgressBar>(suggestion, "PART_Busy").IsVisible);
            Assert.False(Part<Button>(suggestion, "PART_Use").IsVisible);
            Assert.True(Part<Button>(suggestion, "PART_Cancel").IsVisible);

            suggestion.Suggestion = "Outra sugestão.";
            Assert.Equal("Outra sugestão.", string.Concat(body.Inlines!.OfType<Run>().Select(r => r.Text)));
        });
    }

    // ---------------------------------------------------------------- Legend

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Legend_da_distribuicao_tem_cinco_itens_com_a_cor_do_segmento(string theme)
    {
        await OnUi(theme, ui =>
        {
            var legend = new Legend { Value = new StatusDistribution(21, 18, 6, 17, 38) };
            ui.Show(legend);
            var items = legend.GetVisualDescendants().OfType<LegendItem>().ToList();
            Assert.Equal(["Aprovadas 21%", "Revisadas 18%", "Traduzidas 6%", "IA 17%", "Pendentes 38%"], items.Select(i => i.Content));
            string[] tokens = ["Vnf.SegApproved", "Vnf.SegReviewed", "Vnf.SegTranslated", "Vnf.SegAi", "Vnf.SegPending"];
            for (var i = 0; i < items.Count; i++)
            {
                Assert.Equal(Token(legend, tokens[i]), ColorOf(Part<Border>(items[i], "PART_Swatch").Background));
                Assert.Equal(8, Part<Border>(items[i], "PART_Swatch").Bounds.Width);
            }
        });
    }

    [Fact]
    public async Task Legend_aceita_entradas_de_sinais_do_qa_e_troca_com_o_valor()
    {
        await OnUi("Dark", ui =>
        {
            var legend = new Legend { ItemsSource = new[] { new LegendEntry("Erros 2", LegendTone.Error), new LegendEntry("Avisos 37", LegendTone.Warning) } };
            ui.Show(legend);
            var items = legend.GetVisualDescendants().OfType<LegendItem>().ToList();
            Assert.Equal(Token(legend, "Vnf.SignalErrorFg"), ColorOf(Part<Border>(items[0], "PART_Swatch").Background));
            Assert.Equal(Token(legend, "Vnf.SignalWarningFg"), ColorOf(Part<Border>(items[1], "PART_Swatch").Background));

            legend.Value = new StatusDistribution(0, 0, 0, 0, 100);
            ui.Settle();
            Assert.Equal(5, legend.GetVisualDescendants().OfType<LegendItem>().Count());
        });
    }

    // ---------------------------------------------------------------- StatCard e PageHeader

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task StatCard_mostra_titulo_numero_apoio_visualizacao_e_rodape(string theme)
    {
        await OnUi(theme, ui =>
        {
            var full = new StatCard { Title = "Linhas", Value = "48.211", Description = "39% concluídas", Footer = "Continuar no editor", Content = new Border { Height = 8 } };
            var bare = new StatCard { Title = "Conferência", Value = "39 problemas" };
            ui.Show(new StackPanel { Width = 320, Children = { full, bare } });
            Assert.Equal("48.211", Part<TextBlock>(full, "PART_Value").Text);
            Assert.Equal(28, Part<TextBlock>(full, "PART_Value").FontSize);
            Assert.Equal(FontWeight.Bold, Part<TextBlock>(full, "PART_Value").FontWeight);
            Assert.True(Part<Border>(full, "PART_Footer").IsVisible);
            Assert.Equal(40, Part<Border>(full, "PART_Footer").Bounds.Height);
            Assert.False(Part<Border>(bare, "PART_Footer").IsVisible);
            Assert.Equal(Token(full, "Vnf.StrokeSubtle"), ColorOf(Part<Border>(full, "PART_Footer").BorderBrush));
            Assert.Equal(320, full.Bounds.Width);
        });
    }

    [Fact]
    public async Task PageHeader_mostra_migalha_titulo_e_acoes_a_direita()
    {
        await OnUi("Dark", ui =>
        {
            var withActions = new PageHeader { Breadcrumb = "D:\\Traducoes\\minha-vn", Title = "Minha VN", Content = new Button { Content = "Abrir pasta" } };
            var bare = new PageHeader { Title = "Sem migalha" };
            ui.Show(new StackPanel { Width = 800, Children = { withActions, bare } });
            Assert.True(Part<TextBlock>(withActions, "PART_Breadcrumb").IsVisible);
            Assert.False(Part<TextBlock>(bare, "PART_Breadcrumb").IsVisible);
            Assert.Equal("Minha VN", Part<TextBlock>(withActions, "PART_Title").Text);
            Assert.Equal(22, Part<TextBlock>(withActions, "PART_Title").FontSize);

            var button = withActions.GetVisualDescendants().OfType<Button>().Single();
            var title = Part<TextBlock>(withActions, "PART_Title");
            Assert.True(button.TranslatePoint(default, withActions)!.Value.X > title.TranslatePoint(default, withActions)!.Value.X);
            Assert.Equal(800, button.TranslatePoint(new Point(button.Bounds.Width, 0), withActions)!.Value.X, 1);
        });
    }
}
