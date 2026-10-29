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
    public async Task Corrigir_o_nome_do_personagem_tira_o_aviso_da_grade_na_hora()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        Assert.True(Tem(linha, "personagem-diferente"));
        Assert.True(linha.HasWarning);
        var antes = vm.IssueCount;

        vm.Detail!.Translation = "Ele olhou para Kameshima.";
        Assert.True(vm.FlushPendingEdits());

        Assert.False(Tem(linha, "personagem-diferente"));
        Assert.False(linha.HasWarning);
        Assert.Equal(antes - 1, vm.IssueCount);
        Assert.Empty(vm.Detail.QaIssues);
    }

    [Fact]
    public async Task Gravar_um_nome_diferente_do_personagem_acende_o_aviso_da_grade_na_hora()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        vm.Detail!.Translation = "Ele olhou para Kameshima.";
        Assert.True(vm.FlushPendingEdits());
        Assert.False(Tem(linha, "personagem-diferente"));

        vm.Detail!.Translation = "Ele olhou para Kamejima.";
        Assert.True(vm.FlushPendingEdits());

        Assert.True(Tem(linha, "personagem-diferente"));
        Assert.True(linha.HasWarning);
        Assert.Single(vm.Detail.QaIssues);
    }

    [Fact]
    public async Task Trocar_o_nome_do_personagem_acende_o_aviso_nas_linhas_que_usam_o_nome_antigo()
    {
        var vm = await Abrir();
        Assert.True(Tem(vm.Lines[0], "personagem-diferente"));
        vm.Detail!.Translation = "Ele olhou para Kameshima.";
        vm.FlushPendingEdits();
        Assert.False(vm.Lines[0].HasWarning);

        projeto.SaveCharacters([new CharacterEntry("亀島", "Kamejima", CharacterGender.Male)]);
        Distinguir(projeto.CharactersPath);
        await vm.RecheckAsync();

        Assert.True(Tem(vm.Lines[0], "personagem-diferente"));
        Assert.True(vm.Lines[0].HasWarning);
        Assert.Equal(1, vm.IssueCount);
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Warnings).Count);
        Assert.Single(vm.Detail.QaIssues);
    }

    [Fact]
    public async Task O_sublinhado_do_painel_aberto_acompanha_o_glossario_depois_da_reconferencia()
    {
        _ = VNForge.Core.Spelling.SpellChecker.Portuguese;
        var vm = await Abrir();
        vm.Detail!.FieldText = "Zorgblat chegou cedo.";
        Assert.Equal(["Zorgblat"], vm.Detail.Misspellings.Select(s => s.Text));

        projeto.SaveCharacters([new CharacterEntry("亀島", "Zorgblat", CharacterGender.Male)]);
        Distinguir(projeto.CharactersPath);
        await vm.RecheckAsync();

        Assert.Empty(vm.Detail.Misspellings);
    }

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
    public async Task Gravar_varias_vezes_seguidas_reconfere_o_projeto_uma_vez_so_depois_da_espera()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        vm.Detail!.Translation = "Olá";
        vm.FlushPendingEdits();
        relogio.Advance(EditorViewModel.RecheckDelay / 2);
        vm.Detail!.Translation = "Oi";
        vm.FlushPendingEdits();
        relogio.Advance(EditorViewModel.RecheckDelay / 2);
        Assert.False(Tem(vm.Lines[2], "traducao-inconsistente"));

        relogio.Advance(EditorViewModel.RecheckDelay / 2);
        await vm.Rechecking;

        Assert.True(Tem(vm.Lines[2], "traducao-inconsistente"));
    }

    [Fact]
    public async Task Mudar_o_estilo_dos_honorificos_refaz_os_avisos_pelo_pedido_de_reconferencia()
    {
        projeto.SaveText(Script, [Fala(0, "美咲ちゃん", "Misaki-chan", TranslationStatus.Machine)]);
        var config = projeto.LoadConfig();
        projeto.SaveConfig(config with { Style = config.Style with { Honorifics = HonorificsPolicy.Keep } });
        var vm = await Abrir();
        Assert.False(vm.Lines[0].HasWarning);

        projeto.SaveConfig(config with { Style = config.Style with { Honorifics = HonorificsPolicy.Remove } });
        Distinguir(projeto.ConfigPath);
        vm.RequestRecheck();
        relogio.Advance(EditorViewModel.RecheckDelay);
        await vm.Rechecking;

        Assert.True(Tem(vm.Lines[0], "honorifico-fora-do-estilo"));
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
