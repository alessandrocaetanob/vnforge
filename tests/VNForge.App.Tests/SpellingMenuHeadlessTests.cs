using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
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
