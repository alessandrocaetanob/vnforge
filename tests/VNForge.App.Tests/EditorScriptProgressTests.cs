using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O percentual da cena na lista de Cenas e o rodapé do Editor acompanham o que se grava, sem reler o projeto (issue #350).</summary>
public sealed class EditorScriptProgressTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorScriptProgressTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Translated),
            Fala(1, 1, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "「うん」\\p", "Aham.\\p", TranslationStatus.Translated),
            Fala(1, 3, "「うん」\\p", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "「うん」\\p", "Sim.\\p", TranslationStatus.Approved),
            Fala(2, 1, "「さようなら」\\p", null, TranslationStatus.Untranslated),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private static ScriptItemViewModel Cena(EditorViewModel vm, string chave) => vm.Scripts.Single(s => s.Key == chave);
}
