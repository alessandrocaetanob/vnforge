using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class LineDetailSpellingTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Agora);
    private readonly string raiz;

    public LineDetailSpellingTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);
        _ = SpellChecker.Portuguese;
    }

    public void Dispose() => pasta.Dispose();

    private LineDetailViewModel Abrir(ProjectDictionary? palavras)
    {
        var unidade = ProjectFolder.Open(raiz).LoadText("yst00001")[0];
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new FakeNavigator(), relogio, acao => acao()) { SpellWords = palavras };
    }

    [Fact]
    public void Sem_dicionario_do_projeto_o_painel_nao_confere()
    {
        using var vm = Abrir(null);

        vm.FieldText = "Eu esquesi a chave.";

        Assert.Empty(vm.Misspellings);
    }

    [Fact]
    public void Editar_o_campo_atualiza_os_trechos_no_indice_do_texto_do_campo()
    {
        using var vm = Abrir(ProjectDictionary.Empty);

        vm.FieldText = "Eu esquesi a chave.";
        Assert.Equal([new QaSpan(3, 7, "esquesi")], vm.Misspellings);

        vm.FieldText = "Eu esqueci a chave.";
        Assert.Empty(vm.Misspellings);
    }

    [Fact]
    public void Nome_do_glossario_nao_e_sublinhado()
    {
        var palavras = ProjectDictionary.Create([], [new CharacterEntry("花子", "Hanakoi")]);
        using var vm = Abrir(palavras);

        vm.FieldText = "Hanakoi esquesi tudo.";

        Assert.Equal(["esquesi"], vm.Misspellings.Select(s => s.Text));
    }

    [Fact]
    public void Trocar_o_dicionario_do_projeto_refaz_os_trechos()
    {
        using var vm = Abrir(ProjectDictionary.Empty);
        vm.FieldText = "Zorgblat chegou cedo.";
        Assert.Single(vm.Misspellings);

        vm.SpellWords = ProjectDictionary.Create([new GlossaryTerm("ゾーグ", "Zorgblat")], []);

        Assert.Empty(vm.Misspellings);
    }

    [Fact]
    public void Misspellings_so_notifica_quando_os_trechos_mudam()
    {
        using var vm = Abrir(ProjectDictionary.Empty);
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.FieldText = "Eu esquesi a chave.";
        var primeiro = avisos.Count(n => n == nameof(LineDetailViewModel.Misspellings));
        vm.SpellWords = ProjectDictionary.Empty;

        Assert.Equal(1, primeiro);
        Assert.Equal(1, avisos.Count(n => n == nameof(LineDetailViewModel.Misspellings)));
    }

    [Fact]
    public void Marcador_no_campo_nao_vira_erro()
    {
        using var vm = Abrir(ProjectDictionary.Empty);

        vm.FieldText = @"Oi, {name}! \cOlá\c";

        Assert.Empty(vm.Misspellings);
    }

    [Fact]
    public void O_sublinhado_poupa_a_palavra_em_que_o_cursor_esta()
    {
        var renderer = new SpellingUnderlineRenderer { Spans = [new QaSpan(3, 7, "esquesi"), new QaSpan(12, 4, "xzzq")] };

        Assert.Equal(2, renderer.Visible.Count());

        renderer.Caret = 10;
        Assert.Equal(["xzzq"], renderer.Visible.Select(s => s.Text));

        renderer.Caret = 3;
        Assert.Equal(2, renderer.Visible.Count());

        renderer.Caret = null;
        Assert.Equal(2, renderer.Visible.Count());
    }

    private sealed class FakeNavigator : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }
}
