using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A linha da grade acompanha o que o painel de detalhe grava (issue #231).</summary>
public class EditorGridUpdateTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorGridUpdateTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);

    public void Dispose() => pasta.Dispose();

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public async Task Editar_atualiza_traducao_status_e_aviso_da_linha_da_grade()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        Assert.Equal("sem tradução", linha.TranslationLabel);
        Assert.True(linha.HasWarning);

        vm.Detail!.Translation = "Confirmar";
        Assert.True(vm.FlushPendingEdits());

        Assert.Same(linha, vm.Lines[0]);
        Assert.Equal("Confirmar", linha.TranslationLabel);
        Assert.Equal(TranslationStatus.Translated, linha.Status);
        Assert.False(linha.HasWarning);
        Assert.Equal(string.Empty, linha.IssueLabel);
    }

    [Fact]
    public async Task Aprovar_atualiza_o_status_da_linha_da_grade_e_grava_no_arquivo()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        vm.Detail!.Translation = "Confirmar";

        vm.Detail.ApproveCommand.Execute(null);

        Assert.Equal(TranslationStatus.Approved, linha.Status);
        Assert.Equal("Aprovada", linha.StatusLabel);
        Assert.Equal("Confirmar", linha.TranslationLabel);
        Assert.Equal(TranslationStatus.Approved, Gravada(0).Status);
        Assert.Equal("Confirmar", Gravada(0).Translation);
    }

    [Fact]
    public async Task Aceitar_sugestao_atualiza_a_linha_da_grade_quando_a_gravacao_agendada_dispara()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];

        await vm.Detail!.SuggestCommand.ExecuteAsync(null);
        Assert.True(vm.Detail.HasSuggestion);
        vm.Detail.AcceptSuggestionCommand.Execute(null);
        relogio.Advance(LineDetailViewModel.SaveDelay);

        Assert.NotEqual("sem tradução", linha.TranslationLabel);
        Assert.Equal(vm.Detail.Translation, linha.TranslationLabel);
        Assert.Equal(TranslationStatus.Translated, linha.Status);
        Assert.False(linha.HasWarning);
    }

    [Fact]
    public async Task A_linha_avisa_a_grade_das_propriedades_que_mudaram()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        var avisadas = new List<string?>();
        linha.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);

        vm.Detail!.Translation = "Confirmar";
        vm.FlushPendingEdits();

        Assert.Contains(nameof(LineViewModel.TranslationLabel), avisadas);
        Assert.Contains(nameof(LineViewModel.StatusLabel), avisadas);
        Assert.Contains(nameof(LineViewModel.HasWarning), avisadas);
    }

    [Fact]
    public async Task Os_contadores_dos_filtros_acompanham_a_edicao()
    {
        var vm = await Abrir();
        var pendentes = vm.Filters.Single(f => f.Filter == LineStatusFilter.Pending);
        var antes = pendentes.Count;

        vm.Detail!.Translation = "Confirmar";
        vm.FlushPendingEdits();

        Assert.Equal(antes - 1, pendentes.Count);
    }

    [Fact]
    public async Task Reabrir_o_projeto_mostra_a_linha_traduzida()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Confirmar";
        vm.FlushPendingEdits();

        var outro = await Abrir();

        Assert.Equal("Confirmar", outro.Lines[0].TranslationLabel);
        Assert.False(outro.Lines[0].HasWarning);
    }
}
