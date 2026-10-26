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

    [Fact]
    public async Task Aprovar_uma_linha_muda_o_percentual_da_cena_e_o_rodape_sem_reler_o_projeto()
    {
        var vm = await Abrir();
        var cena = Cena(vm, "yst00001");
        Assert.Equal(0, cena.CompletedPercent);
        Assert.Equal(0, vm.Distribution!.Approved);
        var avisadas = new List<string?>();
        vm.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);
        var avisadasDaCena = new List<string?>();
        cena.PropertyChanged += (_, e) => avisadasDaCena.Add(e.PropertyName);

        vm.SelectedLine = vm.Lines[0];
        vm.Detail!.ApproveCommand.Execute(null);

        Assert.Same(cena, vm.SelectedScript);
        Assert.Same(cena, Cena(vm, "yst00001"));
        Assert.Equal(25, cena.CompletedPercent);
        Assert.Equal("25%", cena.PercentLabel);
        Assert.Equal(25, vm.Distribution!.Approved);
        Assert.Equal(25, vm.Distribution.Translated);
        Assert.Equal(50, vm.Distribution.Pending);
        Assert.Contains("Aprovadas 25%", vm.DistributionDescription);
        Assert.Contains(nameof(EditorViewModel.Distribution), avisadas);
        Assert.Contains(nameof(EditorViewModel.DistributionDescription), avisadas);
        Assert.Contains(nameof(ScriptItemViewModel.CompletedPercent), avisadasDaCena);
        Assert.Contains(nameof(ScriptItemViewModel.PercentLabel), avisadasDaCena);
        Assert.Contains(nameof(ScriptItemViewModel.IsComplete), avisadasDaCena);
        Assert.Contains(nameof(ScriptItemViewModel.Distribution), avisadasDaCena);
        Assert.Contains(nameof(ScriptItemViewModel.DistributionDescription), avisadasDaCena);
        Assert.Contains(nameof(ScriptItemViewModel.Tooltip), avisadasDaCena);
    }

    [Fact]
    public async Task Aprovar_todas_as_linhas_leva_a_cena_a_cem_por_cento()
    {
        var vm = await Abrir();
        var cena = Cena(vm, "yst00001");

        foreach (var linha in vm.Lines.ToList())
        {
            vm.SelectedLine = linha;
            vm.Detail!.Translation = string.IsNullOrEmpty(vm.Detail.Translation) ? "Texto." : vm.Detail.Translation;
            vm.Detail.ApproveCommand.Execute(null);
        }

        Assert.Equal(100, cena.CompletedPercent);
        Assert.True(cena.IsComplete);
        Assert.Equal(100, vm.Distribution!.Approved);
    }

    [Fact]
    public async Task Gravar_a_traducao_a_mao_atualiza_o_rodape_da_cena()
    {
        var vm = await Abrir();
        var cena = Cena(vm, "yst00001");
        Assert.Equal(50, vm.Distribution!.Pending);

        vm.SelectedLine = vm.Lines[1];
        vm.Detail!.Translation = "Chove.";
        Assert.True(vm.FlushPendingEdits());

        Assert.Equal(25, vm.Distribution!.Pending);
        Assert.Equal(75, vm.Distribution.Translated);
        Assert.Contains("traduzidas 75%", vm.DistributionDescription);
        Assert.Equal(75, cena.Distribution.Translated);
    }

    [Fact]
    public async Task O_percentual_do_script_aberto_acompanha_a_edicao_de_uma_linha_ja_aprovada()
    {
        var vm = await Abrir();
        vm.SelectedScript = Cena(vm, "yst00002");
        var cena = Cena(vm, "yst00002");
        Assert.Equal(50, cena.CompletedPercent);

        vm.SelectedLine = vm.Lines[0];
        vm.Detail!.Translation = "Pois não.";
        Assert.True(vm.FlushPendingEdits());

        Assert.Equal(0, cena.CompletedPercent);
        Assert.Equal("0%", cena.PercentLabel);
        Assert.False(cena.IsComplete);
    }

    [Fact]
    public async Task Usar_em_todos_atualiza_o_percentual_das_outras_cenas_tocadas()
    {
        var vm = await Abrir();
        var primeira = Cena(vm, "yst00001");
        var segunda = Cena(vm, "yst00002");
        Assert.Equal(50, segunda.CompletedPercent);
        Assert.Equal(50, vm.Distribution!.Pending);

        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        vm.Detail.UseForAllCommand.Execute(null);
        vm.Detail.ConfirmUseForAllCommand.Execute(null);

        Assert.Equal("Tá bom.\\p", ProjectFolder.Open(raiz).LoadText("yst00002")[0].Translation);
        Assert.Equal(0, segunda.CompletedPercent);
        Assert.Equal("0%", segunda.PercentLabel);
        Assert.Equal(0, segunda.Distribution.Approved);
        Assert.Equal(50, segunda.Distribution.Translated);
        Assert.Equal("IA 0%", segunda.Tooltip);
        Assert.Same(primeira, vm.SelectedScript);
        Assert.Equal(25, vm.Distribution!.Pending);
        Assert.Equal(75, vm.Distribution.Translated);
    }

    [Fact]
    public async Task Reler_o_projeto_da_o_mesmo_percentual_que_a_atualizacao_ao_vivo()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        vm.Detail!.ApproveCommand.Execute(null);
        var aoVivo = Cena(vm, "yst00001").CompletedPercent;

        var outro = await Abrir();

        Assert.Equal(aoVivo, Cena(outro, "yst00001").CompletedPercent);
    }
}
