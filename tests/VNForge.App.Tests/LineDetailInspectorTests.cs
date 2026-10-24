using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os dados novos do inspetor do Editor: cabeçalho, medidas, estado dos marcadores, cartão da IA e contexto.</summary>
public sealed class LineDetailInspectorTests : IDisposable
{
    private const string Perfil = """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""";
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Agora);
    private readonly string raiz;

    public LineDetailInspectorTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Machine);
        ProjectFolder.Open(raiz).SaveText("yst00013", [Fala("美咲", "「ふふっ」\\p", "Oi.\\p")]);
    }

    public void Dispose() => pasta.Dispose();

    private LineDetailViewModel Abrir(TextUnit unidade, LineContext? contexto = null, ILineSuggester? sugestor = null)
    {
        var vm = new LineDetailViewModel(unidade, "yst00013", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorContador(), relogio, acao => acao())
        {
            Context = contexto,
            Suggester = sugestor,
        };
        vm.AttachPreview(FormattingProfileSerializer.Parse(Perfil));
        return vm;
    }

    private static TextUnit Fala(string? falante, string original, string? traducao, TranslationStatus status = TranslationStatus.Machine, string id = "yst00013:0043") =>
        new(id, falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    [Fact]
    public void Cabecalho_traz_o_id_completo_o_tipo_e_o_par_do_falante()
    {
        using var dialogo = Abrir(Fala("健太", "「……いいのか？」\\p", "…Tem certeza?\\p"), new LineContext("Kenta", "seco", []));
        using var narracao = Abrir(Fala(null, "雨が降っている。\\p", "Chove.\\p"));

        Assert.Equal("yst00013:0043", dialogo.FullId);
        Assert.Equal("Diálogo", dialogo.KindLabel);
        Assert.True(dialogo.HasSpeaker);
        Assert.Equal("健太 → Kenta", dialogo.SpeakerPair);
        Assert.Equal("Narração", narracao.KindLabel);
        Assert.False(narracao.HasSpeaker);
        Assert.Equal("Narração", narracao.SpeakerPair);
    }

    [Fact]
    public void Falante_sem_cadastro_aparece_so_com_o_nome_original()
    {
        using var vm = Abrir(Fala("先生", "「静かに」\\p", null, TranslationStatus.Untranslated));

        Assert.Equal("先生", vm.SpeakerPair);
    }

    [Fact]
    public void Medidas_contam_caracteres_sem_marcadores_e_a_caixa_em_linhas_e_colunas()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Lá fora, a chuva não parou desde cedo.\\p"));

        Assert.Equal("38 caracteres", vm.CharsText);
        Assert.Equal("1 de 3 linhas · 38/40 colunas", vm.FitText);
    }

    [Fact]
    public void Medidas_acompanham_a_edicao_da_traducao()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Oi.\\p"));

        vm.Translation = "Hehe. Quer dividir o meu, então? Eu cabo aqui com você, sem problema algum.\\p";

        Assert.Equal("2 de 3 linhas · 40/40 colunas", vm.FitText);
        Assert.Equal("75 caracteres", vm.CharsText);
    }

    [Fact]
    public void Sem_traducao_as_medidas_dizem_isso()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", null, TranslationStatus.Untranslated));

        Assert.Equal("0 caracteres", vm.CharsText);
        Assert.Equal("Sem tradução", vm.FitText);
        Assert.Equal(string.Empty, vm.MarkerStateText);
    }

    [Fact]
    public void Marcadores_iguais_ao_original_aparecem_como_certos()
    {
        using var vm = Abrir(Fala("健太", "「……いいのか？\\n濡れるぞ」\\p", "…Tem certeza?\\nVocê vai se molhar.\\p"));

        Assert.True(vm.MarkersMatch);
        Assert.Equal("Marcadores iguais ao original", vm.MarkerStateText);
    }

    [Fact]
    public void Marcador_faltando_ou_a_mais_vira_diferente_do_original()
    {
        using var vm = Abrir(Fala("健太", "「……いいのか？\\n濡れるぞ」\\p", "…Tem certeza? Você vai se molhar.\\p"));

        Assert.False(vm.MarkersMatch);
        Assert.Equal("Marcadores diferentes do original", vm.MarkerStateText);

        vm.Translation = "…Tem certeza?\\nVocê vai se molhar.\\p\\p";
        Assert.False(vm.MarkersMatch);
        Assert.Equal("Marcadores diferentes do original", vm.MarkerStateText);

        vm.Translation = "…Tem certeza?\\nVocê vai se molhar.\\p";
        Assert.True(vm.MarkersMatch);
    }

    [Fact]
    public async Task Sugestao_do_Claude_mostra_modelo_esforco_e_ha_quanto_tempo()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", null, TranslationStatus.Untranslated), sugestor: new SugestorFixo(new LineSuggestion("Hehe.\\p", false, "claude-opus-5-5", "baixo")));

        await vm.SuggestCommand.ExecuteAsync(null);
        Assert.Equal("Claude Opus 5.5", vm.SuggestionModel);
        Assert.Equal("· esforço baixo · agora", vm.SuggestionDetails);

        relogio.Advance(TimeSpan.FromMinutes(2));
        vm.RefreshSuggestionAge();
        Assert.Equal("· esforço baixo · há 2 min", vm.SuggestionDetails);
    }

    [Fact]
    public async Task Sugestao_do_provedor_falso_se_apresenta_como_de_teste()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", null, TranslationStatus.Untranslated), sugestor: new LineSuggester(() => null));

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("Provedor de teste", vm.SuggestionModel);
        Assert.Equal("· sem chave de API · agora", vm.SuggestionDetails);
    }

    [Fact]
    public async Task Usar_a_sugestao_troca_a_traducao_e_apaga_o_cartao()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Oi.\\p"), sugestor: new SugestorFixo(new LineSuggestion("Hehe.\\p", false, "claude-opus-5-5", "baixo")));
        await vm.SuggestCommand.ExecuteAsync(null);

        vm.AcceptSuggestionCommand.Execute(null);

        Assert.Equal("Hehe.\\p", vm.Translation);
        Assert.False(vm.HasSuggestion);
        Assert.Equal(string.Empty, vm.SuggestionModel);
    }

    private static EditorContext Cadastro() => new(
        new CharacterBook([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "fala informal e animada")]),
        [new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("傘", "guarda-chuva"), new GlossaryTerm("校門", "portão da escola")]);

    [Fact]
    public void Contexto_traz_o_personagem_com_a_voz_e_os_termos_do_glossario_na_linha()
    {
        var unidade = Fala("美咲", "「今日も遅刻ギリギリだね」\\p", "Quase atrasado de novo.\\p");
        using var vm = Abrir(unidade, Cadastro().For(unidade.Speaker, unidade.Source));

        Assert.Equal("Misaki", vm.SpeakerTarget);
        Assert.Equal("fala informal e animada", vm.Voice);
        Assert.True(vm.HasVoice);
        Assert.Equal("Glossário: 遅刻 → atraso", vm.GlossaryText);
    }

    [Fact]
    public void Contexto_junta_varios_termos_e_avisa_quando_nao_ha_nenhum()
    {
        var com = Fala(null, "校門の前で、傘を持って立っていた。\\p", null, TranslationStatus.Untranslated);
        var sem = Fala(null, "雨が降っている。\\p", null, TranslationStatus.Untranslated);
        using var varios = Abrir(com, Cadastro().For(com.Speaker, com.Source));
        using var nenhum = Abrir(sem, Cadastro().For(sem.Speaker, sem.Source));

        Assert.Equal("Glossário: 傘 → guarda-chuva · 校門 → portão da escola", varios.GlossaryText);
        Assert.Equal("Nenhum termo do glossário nesta linha.", nenhum.GlossaryText);
    }

    [Fact]
    public void Sem_contexto_carregado_o_painel_continua_funcionando()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", null, TranslationStatus.Untranslated));

        Assert.Null(vm.SpeakerTarget);
        Assert.False(vm.HasVoice);
        Assert.Equal("Nenhum termo do glossário nesta linha.", vm.GlossaryText);
    }

    [Fact]
    public void Aprovar_pelo_botao_largo_grava_e_vai_para_a_proxima_linha()
    {
        var nav = new NavegadorContador();
        var unidade = Fala("美咲", "「ふふっ」\\p", "Hehe.\\p");
        ProjectFolder.Open(raiz).SaveText("yst00013", [unidade]);
        using var vm = new LineDetailViewModel(unidade, "yst00013", new LineEditor(ProjectFolder.Open(raiz), relogio), nav, relogio, acao => acao());

        vm.ApproveAndNextCommand.Execute(null);

        Assert.Equal(1, nav.Proximas);
        Assert.Equal(TranslationStatus.Approved, vm.Status);
    }

    private sealed class SugestorFixo(LineSuggestion resposta) : ILineSuggester
    {
        public Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct) => Task.FromResult(resposta);
    }

    private sealed class NavegadorContador : ILineNavigator
    {
        public int Proximas { get; private set; }

        public void SelectNext() => Proximas++;

        public void SelectPrevious()
        {
        }
    }
}
