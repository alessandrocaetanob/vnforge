using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Gravar uma linha ou mudar o glossário e o estilo refaz os avisos do Editor sem fechar e abrir a app (issue #372).</summary>
public sealed class EditorRecheckTests : IDisposable
{
    private const string Script = "yst00001";

    private static int writes;

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;
    private readonly ProjectFolder projeto;

    public EditorRecheckTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        projeto = ProjectFolder.Open(raiz);
        projeto.SaveText(Script,
        [
            Fala(0, "そう言いながら亀島は…", "Ele olhou para Kamejima.", TranslationStatus.Machine),
            Fala(1, "おはよう", "Bom dia", TranslationStatus.Translated),
            Fala(2, "おはよう", "Bom dia", TranslationStatus.Translated),
        ]);
        projeto.SaveCharacters([new CharacterEntry("亀島", "Kameshima", CharacterGender.Male)]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string original, string? traducao, TranslationStatus status) =>
        new($"{Script}:{n:D6}", TextUnitKind.Dialogue, null, original, traducao, status);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    // O contexto confere a data de gravação e o tamanho; dois arquivos gravados no mesmo instante e do mesmo tamanho não contariam como mudança.
    private static void Distinguir(string arquivo) =>
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref writes)));

    private static bool Tem(LineViewModel linha, string regra) => linha.Entry.Issues.Any(i => i.RuleId == regra);

    [Fact]
    public async Task Ignorar_a_palavra_no_painel_tira_o_aviso_de_ortografia_das_outras_falas_depois_da_reconferencia()
    {
        _ = VNForge.Core.Spelling.SpellChecker.Portuguese;
        projeto.SaveText(Script,
        [
            Fala(0, "そう言いながら亀島は…", "Ele olhou para Kameshima.", TranslationStatus.Translated),
            Fala(1, "おはよう", "Bom dia esquesi", TranslationStatus.Translated),
            Fala(2, "こんにちは", "Boa tarde esquesi", TranslationStatus.Translated),
        ]);
        var vm = await Abrir();
        Assert.True(Tem(vm.Lines[1], "ortografia"));
        Assert.True(Tem(vm.Lines[2], "ortografia"));
        vm.Detail!.FieldText = "Eu esquesi a chave.";
        Assert.Single(vm.Detail.Misspellings);

        vm.Detail.IgnoreWordCommand.Execute("esquesi");
        await vm.Rechecking;

        Assert.Empty(vm.Detail.Misspellings);
        Assert.False(Tem(vm.Lines[1], "ortografia"));
        Assert.False(Tem(vm.Lines[2], "ortografia"));
        Assert.Equal([new VNForge.Core.Spelling.IgnoredWord("esquesi")], projeto.LoadIgnoredWords());
    }

    [Fact]
    public async Task Duas_linhas_com_o_mesmo_original_e_traducoes_diferentes_ganham_a_inconsistencia_depois_da_gravacao()
    {
        var vm = await Abrir();
        Assert.False(Tem(vm.Lines[2], "traducao-inconsistente"));
        vm.SelectedLine = vm.Lines[2];

        vm.Detail!.Translation = "Olá";
        vm.FlushPendingEdits();
        Assert.False(Tem(vm.Lines[2], "traducao-inconsistente"));
        relogio.Advance(EditorViewModel.RecheckDelay);
        await vm.Rechecking;

        Assert.True(Tem(vm.Lines[2], "traducao-inconsistente"));
        Assert.False(Tem(vm.Lines[1], "traducao-inconsistente"));

        vm.Detail!.Translation = "Bom dia";
        vm.FlushPendingEdits();
        relogio.Advance(EditorViewModel.RecheckDelay);
        await vm.Rechecking;

        Assert.False(Tem(vm.Lines[2], "traducao-inconsistente"));
    }

    [Fact]
    public async Task A_reconferencia_de_um_projeto_trocado_nao_mexe_nas_linhas_novas()
    {
        var vm = await Abrir();
        projeto.SaveCharacters([new CharacterEntry("亀島", "Kamejima", CharacterGender.Male)]);
        Distinguir(projeto.CharactersPath);

        var reconferencia = vm.RecheckAsync();
        vm.Project = null;
        await reconferencia;

        Assert.Empty(vm.Lines);
    }
}
