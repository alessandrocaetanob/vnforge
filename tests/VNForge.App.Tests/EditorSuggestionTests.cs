using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A sugestão da segunda passada de editor (#532) no Editor: o ícone na grade, o cartão do inspetor, aceitar, ignorar e o fim dela quando a linha muda.</summary>
public sealed class EditorSuggestionTests : IDisposable
{
    private static readonly DateTimeOffset Meio = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Meio);
    private readonly string raiz;

    public EditorSuggestionTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            Fala(0, "Os dois, cinco minutos atrasados.\\p", TranslationStatus.Machine),
            Fala(1, "Chovia.\\p", TranslationStatus.Machine),
            Fala(2, "Aham.\\p", TranslationStatus.Machine),
            Fala(3, "Ei.\\p", TranslationStatus.Machine),
        ]);
        Salvar(Sugestao(0, "Os dois, cinco minutos atrasados.\\p", "Vocês dois, cinco minutos atrasados.\\p", EditorSuggestionKind.Fluency),
            Sugestao(2, "Aham.\\p", "Uhum.\\p", EditorSuggestionKind.Voice),
            Sugestao(3, "Ei.\\p", "Ei, você.\\p", EditorSuggestionKind.Consistency));
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string traducao, TranslationStatus status) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}\\p", traducao, status);

    private static EditorSuggestion Sugestao(int n, string baseTexto, string proposta, EditorSuggestionKind tipo) =>
        new("yst00001", $"yst00001:{n:D6}", tipo, baseTexto, proposta, "Soa mais natural.", Meio);

    private void Salvar(params EditorSuggestion[] sugestoes) =>
        EditorSuggestionsFile.Save(ProjectFolder.Open(raiz), EditorSuggestionsFile.Empty.ReplacingScript("yst00001", sugestoes));

    private IReadOnlyList<EditorSuggestion> NoArquivo() => EditorSuggestionsFile.Load(ProjectFolder.Open(raiz)).Suggestions;

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Aceitar_grava_a_proposta_como_traducao_da_pessoa_e_tira_a_sugestao_da_grade_e_do_arquivo()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;

        detail.AcceptEditorSuggestionCommand.Execute(null);

        Assert.Equal("Vocês dois, cinco minutos atrasados.\\p", Gravada(0).Translation);
        Assert.Equal(TranslationStatus.Translated, Gravada(0).Status);
        Assert.False(detail.HasEditorSuggestion);
        Assert.False(vm.Lines[0].HasEditorSuggestion);
        Assert.Equal(["yst00001:000002", "yst00001:000003"], NoArquivo().Select(s => s.UnitId));
        Assert.Equal(2, vm.EditorSuggestionLines);
    }

    [Fact]
    public async Task Aceitar_com_texto_digitado_e_ainda_nao_gravado_fica_desligado_e_nao_joga_o_texto_fora()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;
        Assert.True(detail.AcceptEditorSuggestionCommand.CanExecute(null));

        detail.Translation = "Hum, pois é.\\p";

        Assert.False(detail.AcceptEditorSuggestionCommand.CanExecute(null));
        detail.AcceptEditorSuggestionCommand.Execute(null);
        Assert.Equal("Hum, pois é.\\p", detail.Translation);
        detail.Flush();
        Assert.Equal("Hum, pois é.\\p", Gravada(2).Translation);
        Assert.False(detail.HasEditorSuggestion);
    }

    [Fact]
    public async Task Outra_que_termina_depois_de_a_pessoa_ignorar_nao_traz_a_sugestao_de_volta()
    {
        var liberar = new TaskCompletionSource<EditorSuggestion?>();
        var vm = await AbrirComOutra((s, _) => liberar.Task);
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;
        var original = vm.Lines[0].EditorSuggestion!;
        var pedido = detail.OtherEditorSuggestionCommand.ExecuteAsync(null);

        detail.IgnoreEditorSuggestionCommand.Execute(null);
        liberar.SetResult(original with { Proposed = "Nova.\\p" });
        await pedido;

        Assert.False(vm.Lines[0].HasEditorSuggestion);
        Assert.DoesNotContain(NoArquivo(), s => s.UnitId == "yst00001:000000");
    }

    [Fact]
    public async Task Falha_ao_gravar_a_decisao_mostra_o_erro_e_a_sugestao_some_da_tela()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao())
        {
            EditorSuggestionsWriter = (_, _) => throw new IOException("disco cheio"),
        };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[2];

        vm.Detail!.IgnoreEditorSuggestionCommand.Execute(null);

        Assert.Contains("disco cheio", vm.ErrorMessage);
        Assert.False(vm.Lines[2].HasEditorSuggestion);
    }

    [Fact]
    public async Task O_leitor_trocado_nos_testes_substitui_o_arquivo()
    {
        var lido = new List<string>();
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao())
        {
            EditorSuggestionsReader = root =>
            {
                lido.Add(root);
                return EditorSuggestionsFile.Empty.ReplacingScript("yst00001", [Sugestao(1, "Chovia.\\p", "Chove.\\p", EditorSuggestionKind.Fluency)]);
            },
        };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;

        Assert.Equal([false, true, false, false], vm.Lines.Select(l => l.HasEditorSuggestion));
        Assert.NotEmpty(lido);
    }

    private async Task<EditorViewModel> AbrirComOutra(Func<EditorSuggestion, CancellationToken, Task<EditorSuggestion?>> outra)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao()) { EditorSuggestionAnother = outra };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Outra_troca_a_proposta_do_cartao_e_da_grade_sem_mexer_na_traducao()
    {
        var vm = await AbrirComOutra((s, _) => Task.FromResult<EditorSuggestion?>(s with { Proposed = "Vocês dois, atrasados.\\p", Reason = "Mais direto." }));
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;
        Assert.True(detail.OtherEditorSuggestionCommand.CanExecute(null));

        await detail.OtherEditorSuggestionCommand.ExecuteAsync(null);

        Assert.Equal("Vocês dois, atrasados.", detail.EditorSuggestionProposed);
        Assert.Equal("Mais direto.", detail.EditorSuggestionReason);
        Assert.Equal("Vocês dois, atrasados.\\p", vm.Lines[0].EditorSuggestion!.Proposed);
        Assert.Equal("Os dois, cinco minutos atrasados.\\p", Gravada(0).Translation);
        Assert.False(detail.IsAskingAnotherEditorSuggestion);
        Assert.Equal(string.Empty, detail.EditorSuggestionNote);
    }

    [Fact]
    public async Task Outra_sem_proposta_nova_avisa_e_mantem_a_de_antes()
    {
        var vm = await AbrirComOutra((_, _) => Task.FromResult<EditorSuggestion?>(null));
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;

        await detail.OtherEditorSuggestionCommand.ExecuteAsync(null);

        Assert.True(detail.HasEditorSuggestion);
        Assert.Equal("Vocês dois, cinco minutos atrasados.", detail.EditorSuggestionProposed);
        Assert.Equal("O editor não achou outra forma melhor. A sugestão de antes continua.", detail.EditorSuggestionNote);
    }

    [Fact]
    public async Task Outra_que_falha_mostra_o_motivo_em_portugues_e_a_traducao_nao_muda()
    {
        var vm = await AbrirComOutra((_, _) => throw new InvalidOperationException("Falta a chave da IA."));
        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;

        await detail.OtherEditorSuggestionCommand.ExecuteAsync(null);

        Assert.StartsWith("Não foi possível pedir outra sugestão:", detail.EditorSuggestionNote);
        Assert.EndsWith("A tradução não mudou.", detail.EditorSuggestionNote);
        Assert.True(detail.HasEditorSuggestion);
        Assert.Equal("Aham.\\p", Gravada(2).Translation);
    }
}
