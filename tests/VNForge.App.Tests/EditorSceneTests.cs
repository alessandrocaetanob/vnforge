using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Na cena" (a fala anterior e a seguinte) e "Usar em todos" (a mesma fala em outras linhas) no inspetor do Editor.</summary>
[Trait("Risco", "R2")]
public sealed class EditorSceneTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorSceneTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, null, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "健太", "「うん」\\p", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "「うん」\\p", "Sim.\\p", TranslationStatus.Machine),
            Fala(2, 1, "美咲", "「うん」", null, TranslationStatus.Untranslated),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private async Task<EditorViewModel> Abrir(Func<string, LineEditor?>? gravador = null)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao()) { EditorFactory = gravador };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    private sealed class NavegadorMudo : VNForge.App.Services.ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    private sealed class FalasIguais(int count, VNForge.App.Services.SameSourcePreview? preview = null, VNForge.App.Services.SameSourceOutcome? outcome = null, TaskCompletionSource<VNForge.App.Services.SameSourceOutcome>? gate = null) : VNForge.App.Services.ISameSourceLines
    {
        public int Count => count;

        public VNForge.App.Services.SameSourceBatch Batch { get; } = new();

        public CancellationToken Token { get; private set; }

        public VNForge.App.Services.SameSourcePreview Inspect(string translation) => preview ?? new(0, 0);

        public async Task<VNForge.App.Services.SameSourceOutcome> ApplyAsync(string translation)
        {
            if (!Batch.TryStart(out var token))
            {
                throw new InvalidOperationException("Já há um Usar em todos gravando.");
            }

            Token = token;
            VNForge.App.Services.SameSourceOutcome? feito = null;
            try
            {
                feito = await (gate?.Task ?? Task.FromResult(outcome ?? new(0, 0, 0, false)));
                return feito;
            }
            finally
            {
                Batch.Finish(feito);
            }
        }
    }

    [Fact]
    public async Task Trocar_de_projeto_no_meio_do_lote_cancela_o_lote()
    {
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var liberar = new ManualResetEventSlim();
        var projeto = ProjectFolder.Open(raiz);
        var segurar = false;
        var vm = await Abrir(_ => new LineEditor(projeto, relogio, (chave, unidades) =>
        {
            if (chave == "yst00001" && Volatile.Read(ref segurar))
            {
                entrou.TrySetResult();
                liberar.Wait(TimeSpan.FromSeconds(30));
            }

            projeto.SaveText(chave, unidades);
        }));
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        var detalhe = vm.Detail;
        await detalhe.UseForAllCommand.ExecuteAsync(null);
        Volatile.Write(ref segurar, true);
        var lote = detalhe.ConfirmUseForAllCommand.ExecuteAsync(null);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        vm.Project = EditorFixtures.Summary("Outro nome", raiz);
        liberar.Set();
        await lote;

        Assert.StartsWith("Cancelado.", detalhe.SameSourceResult);
        Assert.Equal("Sim.\\p", Gravada("yst00002", 0).Translation);
    }

    [Fact]
    public async Task Usar_em_todos_grava_a_traducao_nas_linhas_do_mesmo_original_e_em_nenhuma_outra()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);

        Assert.True(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("2 destas linhas já têm outra tradução (1 aprovada). Substituir todas?", vm.Detail.UseForAllConfirmText);
        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);

        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.False(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 3).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal("Bom dia.\\p", Gravada("yst00001", 0).Translation);
        Assert.Null(Gravada("yst00001", 1).Translation);
        Assert.Null(Gravada("yst00002", 1).Translation);
        Assert.Equal("Aplicada a 2 linhas.", vm.Detail.SameSourceResult);
        Assert.Null(vm.Detail.ErrorMessage);
    }

}
