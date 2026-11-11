using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
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

    [Theory]
    [InlineData("manual", null, "Editada à mão (há 10 min).")]
    [InlineData("anthropic", "claude-opus-5-5", "Traduzida pela IA, Claude Opus 5.5 (há 10 min).")]
    [InlineData("anthropic", null, "Traduzida pela IA (há 10 min).")]
    [InlineData("anthropic", "", "Traduzida pela IA (há 10 min).")]
    public void Historico_diz_quem_traduziu_o_modelo_e_quando(string provedor, string? modelo, string esperado)
    {
        var unidade = Fala("美咲", "「ふふっ」\\p", "Oi.\\p") with { Origin = new TranslationOrigin(provedor, modelo, Agora.AddMinutes(-10)) };
        using var vm = Abrir(unidade);

        Assert.Equal(esperado, vm.HistoryText);
    }

    [Fact]
    public void Historico_sem_origem_avisa_que_nao_ha_registro()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Oi.\\p"));

        Assert.Equal("Sem registro de origem para esta tradução.", vm.HistoryText);
    }

    private const string PerfilSemQuebra = """{ "wrap": { "enabled": false, "maxWidth": 54, "maxLines": 3, "charWidth": "uniform" } }""";

    [Fact]
    public void Linha_que_o_build_nao_formata_diz_isso_em_vez_de_cabe()
    {
        var perfil = FormattingProfile.Default with { Kinds = [TextUnitKind.Narration], Wrap = new WrapRule(true, 40, 3, CharWidthMode.Uniform) };
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Oi.\\p"));

        vm.AttachPreview(perfil);

        Assert.Equal(LineDetailViewModel.NotFormattedText, vm.FitText);
        Assert.True(vm.FitIsNeutral);
        Assert.False(vm.FitIsShown);
    }

    [Fact]
    public async Task Dividir_em_paginas_nao_deixa_o_inspetor_dizer_que_faltou_uma_quebra()
    {
        // A divisão troca por \p a quebra do autor no ponto de corte: o \p a mais paga o \n que sumiu.
        using var vm = Abrir(Fala("健太", "一\\n二\\n三\\n四\\p", "um\\ndois\\ntres\\nquatro\\p"));
        Assert.True(vm.Fit!.IsVisible);

        await vm.Fit.SplitCommand.ExecuteAsync(null);

        Assert.Equal("um\\ndois\\ntres\\pquatro\\p", vm.Translation);
        Assert.Empty(vm.MissingMarkers);
        Assert.True(vm.MarkersMatch);
        Assert.Equal("Marcadores iguais ao original, com 1 página a mais", vm.MarkerStateText);
    }

    [Fact]
    public void Virada_de_pagina_a_mais_nao_e_marcador_diferente_e_o_inspetor_diz_quantas()
    {
        using var vm = Abrir(Fala("健太", "「……いいのか？\\n濡れるぞ」\\p", "…Tem certeza?\\nVocê vai se molhar.\\p"));

        vm.Translation = "…Tem certeza?\\nVocê vai se molhar.\\p\\p";
        Assert.True(vm.MarkersMatch);
        Assert.Equal("Marcadores iguais ao original, com 1 página a mais", vm.MarkerStateText);

        vm.Translation = "…Tem certeza?\\n\\p\\p\\p";
        Assert.True(vm.MarkersMatch);
        Assert.Equal("Marcadores iguais ao original, com 2 páginas a mais", vm.MarkerStateText);

        vm.Translation = "…Tem certeza?\\n\\c\\p\\p";
        Assert.False(vm.MarkersMatch);
        Assert.Equal("Falta ou sobra um marcador", vm.MarkerStateText);
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

    private static EditorContext Cadastro() => new(
        new CharacterBook([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "fala informal e animada")]),
        [new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("傘", "guarda-chuva"), new GlossaryTerm("校門", "portão da escola")]);

    [Fact]
    public void Traducao_trocada_por_fora_refaz_o_campo()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", null, TranslationStatus.Untranslated));

        vm.CopyOriginalCommand.Execute(null);

        Assert.Equal("「ふふっ」", vm.FieldText);
        Assert.Equal("「ふふっ」\\p", vm.Translation);
    }

    [Fact]
    public void Apagar_o_campo_e_digitar_de_novo_usa_o_fim_do_original()
    {
        using var vm = Abrir(Fala("美咲", "「ふふっ」\\p", "Oi."));

        vm.FieldText = string.Empty;
        vm.FieldText = "Olá";

        Assert.Equal("Olá\\p", vm.Translation);
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
