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

    private LineDetailViewModel Abrir(ProjectDictionary? palavras, string? projectRoot = null, Action? aviso = null)
    {
        var unidade = ProjectFolder.Open(raiz).LoadText("yst00001")[0];
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new FakeNavigator(), relogio, acao => acao()) { ProjectRoot = projectRoot, WordIgnored = aviso, SpellWords = palavras };
    }

    [Fact]
    public void Arquivo_de_palavras_ignoradas_invalido_deixa_a_palavra_marcada_e_mostra_o_motivo()
    {
        File.WriteAllText(ProjectFolder.Open(raiz).IgnoredWordsPath, "isto não é json");
        var avisos = 0;
        using var vm = Abrir(ProjectDictionary.Empty, raiz, () => avisos++);
        vm.FieldText = "Eu esquesi a chave.";

        vm.IgnoreWordCommand.Execute("esquesi");

        Assert.Single(vm.Misspellings);
        Assert.Equal(0, avisos);
        Assert.Contains("esquesi", vm.ErrorMessage);
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
