using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O menu "Ignorar esta palavra no projeto" na palavra sublinhada do campo da tradução (#511).</summary>
public sealed class SpellingMenuHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>, IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Agora);

    public void Dispose() => pasta.Dispose();

    private (LineDetailViewModel Vm, string Raiz) Abrir()
    {
        _ = SpellChecker.Portuguese;
        var raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);
        var unidade = ProjectFolder.Open(raiz).LoadText("yst00001")[0];
        var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NoNavigator(), relogio, acao => acao())
        {
            ProjectRoot = raiz,
            SpellWords = ProjectDictionary.Empty,
        };
        vm.FieldText = "Eu esquesi a chave.";
        return (vm, raiz);
    }

    [Fact]
    public async Task O_menu_da_palavra_marcada_oferece_ignorar_no_projeto_e_o_item_grava()
    {
        var (vm, raiz) = Abrir();

        await fixture.Session.Dispatch(
            () =>
            {
                var menu = SpellingMenu.Create(vm, 5);

                var item = Assert.IsType<MenuItem>(Assert.Single(menu!.Items));
                Assert.Equal("Ignorar esta palavra no projeto", item.Header);
                Assert.True(item.Command!.CanExecute(item.CommandParameter));
                item.Command.Execute(item.CommandParameter);
                Assert.Empty(vm.Misspellings);
                Assert.Equal([new IgnoredWord("esquesi")], ProjectFolder.Open(raiz).LoadIgnoredWords());
                Assert.Null(SpellingMenu.Create(vm, 5));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Fora_da_palavra_marcada_nao_ha_menu()
    {
        var (vm, _) = Abrir();

        await fixture.Session.Dispatch(
            () =>
            {
                Assert.Null(SpellingMenu.Create(vm, 0));
                Assert.Null(SpellingMenu.Create(vm, 15));
                Assert.Null(SpellingMenu.Create(null, 5));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Menu_de_contexto_do_campo_sobre_a_palavra_abre_o_menu_do_corretor_e_fora_dela_segue_o_padrao()
    {
        var (vm, _) = Abrir();

        await fixture.Session.Dispatch(
            () =>
            {
                var view = new LineDetailView { DataContext = vm };
                var window = new Window { Width = 900, Height = 800, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var editor = view.GetVisualDescendants().OfType<TextEditor>().Single();

                editor.TextArea.Caret.Offset = 0;
                var fora = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                editor.RaiseEvent(fora);
                Assert.False(fora.Handled);
                Assert.Null(view.SpellingFlyout);

                editor.TextArea.Caret.Offset = 5;
                var dentro = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                editor.RaiseEvent(dentro);
                Assert.True(dentro.Handled);
                var item = Assert.IsType<MenuItem>(Assert.Single(view.SpellingFlyout!.Items));
                Assert.Equal(SpellingMenu.IgnoreHeader, item.Header);

                window.Close();
            },
            CancellationToken.None);
    }

    private static (Window Window, LineDetailView View, TextEditor Editor) Show(LineDetailViewModel vm)
    {
        var view = new LineDetailView { DataContext = vm };
        var window = new Window { Width = 900, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, view.GetVisualDescendants().OfType<TextEditor>().Single());
    }

    // O ponto da janela onde o texto do offset aparece, com a rolagem do editor já descontada.
    private static Point PointAt(Window window, TextEditor editor, int offset)
    {
        var textView = editor.TextArea.TextView;
        var location = editor.Document.GetLocation(offset);
        var inDocument = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineMiddle);
        return textView.TranslatePoint(inDocument - textView.ScrollOffset, window)!.Value;
    }

    private static void RightClick(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Botao_direito_sobre_a_palavra_abre_o_menu_e_fora_dela_o_menu_padrao_segue()
    {
        var (vm, _) = Abrir();

        await fixture.Session.Dispatch(
            () =>
            {
                var (window, view, editor) = Show(vm);

                RightClick(window, PointAt(window, editor, 6));
                Assert.True(view.SpellingFlyout?.IsOpen);
                view.SpellingFlyout!.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(view.SpellingFlyout);

                RightClick(window, PointAt(window, editor, 13));
                Assert.Null(view.SpellingFlyout);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Botao_direito_no_espaco_vazio_depois_da_palavra_no_fim_da_linha_nao_abre_o_menu()
    {
        var (vm, _) = Abrir();
        vm.FieldText = "Eu esquesi";

        await fixture.Session.Dispatch(
            () =>
            {
                var (window, view, editor) = Show(vm);
                var fimDaPalavra = PointAt(window, editor, 10);

                RightClick(window, fimDaPalavra + new Point(150, 0));
                Assert.Null(view.SpellingFlyout);

                RightClick(window, PointAt(window, editor, 9));
                Assert.NotNull(view.SpellingFlyout);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Botao_direito_com_o_campo_rolado_acha_a_palavra_sob_o_ponteiro()
    {
        var (vm, _) = Abrir();
        vm.FieldText = string.Concat(Enumerable.Repeat("linha de enchimento\n", 12)) + "Eu esquesi a chave.";

        await fixture.Session.Dispatch(
            () =>
            {
                var (window, view, editor) = Show(vm);
                editor.MaxHeight = 70;
                window.UpdateLayout();
                editor.ScrollToEnd();
                window.UpdateLayout();
                Assert.True(editor.TextArea.TextView.ScrollOffset.Y > 0);
                var offset = vm.FieldText.IndexOf("esquesi", StringComparison.Ordinal) + 3;

                RightClick(window, PointAt(window, editor, offset));

                Assert.True(view.SpellingFlyout?.IsOpen);
                window.Close();
            },
            CancellationToken.None);
    }

    private sealed class NoNavigator : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }
}
