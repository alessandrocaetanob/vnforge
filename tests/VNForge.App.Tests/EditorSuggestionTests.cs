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
    public async Task A_grade_marca_so_as_linhas_com_sugestao_e_a_dica_diz_o_tipo()
    {
        var vm = await Abrir();

        Assert.Equal([true, false, true, true], vm.Lines.Select(l => l.HasEditorSuggestion));
        Assert.Equal("Sugestão do editor: fluidez", vm.Lines[0].EditorSuggestionTip);
        Assert.Equal("Sugestão do editor: voz", vm.Lines[2].EditorSuggestionTip);
        Assert.Null(vm.Lines[1].EditorSuggestionTip);
        Assert.Equal(3, vm.EditorSuggestionLines);
    }

    [Fact]
    public async Task O_inspetor_mostra_o_cartao_com_o_tipo_a_posicao_na_cena_a_traducao_de_hoje_a_proposta_e_o_motivo()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;

        Assert.True(detail.HasEditorSuggestion);
        Assert.Equal("Passada de editor", detail.EditorSuggestionTitle);
        Assert.Equal("voz · 2 de 3", detail.EditorSuggestionChip);
        Assert.Equal("Passada de editor · voz", detail.EditorSuggestionReviewTitle);
        Assert.Equal("2 de 3 na cena", detail.EditorSuggestionReviewChip);
        Assert.Equal("Aham.", detail.EditorSuggestionCurrent);
        Assert.Equal("Uhum.", detail.EditorSuggestionProposed);
        Assert.Equal("Soa mais natural.", detail.EditorSuggestionReason);
    }

    [Fact]
    public async Task Linha_sem_sugestao_nao_tem_cartao()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[1];

        Assert.False(vm.Detail!.HasEditorSuggestion);
        Assert.Equal(string.Empty, vm.Detail.EditorSuggestionChip);
    }

    [Fact]
    public async Task Abrir_a_linha_nao_muda_a_traducao_nem_o_arquivo_de_texto()
    {
        var antes = File.ReadAllBytes(ProjectFolder.Open(raiz).TextPath("yst00001"));
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[0];
        vm.FlushPendingEdits();

        Assert.Equal(antes, File.ReadAllBytes(ProjectFolder.Open(raiz).TextPath("yst00001")));
        Assert.Equal(3, NoArquivo().Count);
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
    public async Task Ignorar_tira_a_sugestao_e_deixa_a_traducao_como_estava()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;

        detail.IgnoreEditorSuggestionCommand.Execute(null);

        Assert.Equal("Aham.\\p", Gravada(2).Translation);
        Assert.Equal(TranslationStatus.Machine, Gravada(2).Status);
        Assert.False(detail.HasEditorSuggestion);
        Assert.False(vm.Lines[2].HasEditorSuggestion);
        Assert.Equal(["yst00001:000000", "yst00001:000003"], NoArquivo().Select(s => s.UnitId));
    }

    [Fact]
    public async Task Depois_de_aceitar_uma_a_posicao_das_outras_conta_so_as_que_restam()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        vm.Detail!.AcceptEditorSuggestionCommand.Execute(null);

        vm.SelectedLine = vm.Lines[3];

        Assert.Equal("consistência · 2 de 2", vm.Detail!.EditorSuggestionChip);
    }

    [Fact]
    public async Task Editar_a_traducao_a_mao_aposenta_a_sugestao_na_grade_no_inspetor_e_no_arquivo()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;

        detail.Translation = "Hum.\\p";
        detail.Flush();

        Assert.False(detail.HasEditorSuggestion);
        Assert.False(vm.Lines[2].HasEditorSuggestion);
        Assert.Equal(["yst00001:000000", "yst00001:000003"], NoArquivo().Select(s => s.UnitId));
    }

    [Fact]
    public async Task Aprovar_a_linha_e_dizer_que_esta_boa_e_encerra_a_sugestao()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];

        vm.Detail!.ApproveCommand.Execute(null);

        Assert.False(vm.Detail.HasEditorSuggestion);
        Assert.False(vm.Lines[3].HasEditorSuggestion);
        Assert.DoesNotContain(NoArquivo(), s => s.UnitId == "yst00001:000003");
    }

    [Fact]
    public async Task Sugestao_de_uma_traducao_antiga_nao_aparece_ao_abrir_o_projeto()
    {
        Salvar(Sugestao(1, "Texto de antes", "Texto novo", EditorSuggestionKind.Fluency));

        var vm = await Abrir();

        Assert.All(vm.Lines, l => Assert.False(l.HasEditorSuggestion));
        Assert.Equal(0, vm.EditorSuggestionLines);
    }

    [Fact]
    public async Task Atualizar_depois_da_passada_mostra_as_sugestoes_novas_sem_trocar_de_linha()
    {
        Salvar();
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[1];
        Assert.False(vm.Detail!.HasEditorSuggestion);

        Salvar(Sugestao(1, "Chovia.\\p", "Chovia muito.\\p", EditorSuggestionKind.Fluency));
        vm.RefreshEditorSuggestions();

        Assert.True(vm.Lines[1].HasEditorSuggestion);
        Assert.True(vm.Detail!.HasEditorSuggestion);
        Assert.Equal("fluidez · 1 de 1", vm.Detail.EditorSuggestionChip);
        Assert.Same(vm.Lines[1], vm.SelectedLine);
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

    [Fact]
    public async Task No_modo_revisao_o_cartao_acompanha_a_fala_e_aceitar_vale_como_na_grade()
    {
        var vm = await Abrir();

        vm.StartReview();
        var review = vm.Review!;
        Assert.Equal("yst00001:000000", review.Detail!.Id);
        Assert.True(review.Detail.HasEditorSuggestion);

        review.Detail.AcceptEditorSuggestionCommand.Execute(null);

        Assert.Equal("Vocês dois, cinco minutos atrasados.\\p", Gravada(0).Translation);
        Assert.False(vm.Lines[0].HasEditorSuggestion);
    }
}
