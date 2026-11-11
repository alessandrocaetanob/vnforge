using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class EditorDetailTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorDetailTests() =>
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

    private string ProjetoComTilJapones(string subpasta, bool pontuacaoDoTeclado)
    {
        var projeto = ProjectFolder.Create(Path.Combine(pasta.Path, subpasta), new ProjectConfig(
            "Real",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default)
        {
            Style = StyleConfig.Default with { KeyboardPunctuation = pontuacaoDoTeclado },
        });
        projeto.SaveText("story04", [new TextUnit("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine)]);
        return projeto.Root;
    }

    private async Task<EditorViewModel> AbrirProjeto(string raizDoProjeto)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Real", raizDoProjeto);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Trocar_de_linha_descarta_o_painel_anterior_sem_deixar_gravacao_agendada()
    {
        var vm = await Abrir();
        var anterior = vm.Detail!;
        anterior.Translation = "Pendente";

        vm.SelectedLine = vm.Lines[1];
        anterior.Translation = "Depois de descartado";
        relogio.Advance(TimeSpan.FromSeconds(5));

        Assert.NotSame(anterior, vm.Detail);
        Assert.Equal("Pendente", Gravada(0).Translation);
    }

    [Fact]
    public async Task Aprovar_a_ultima_linha_mantem_a_selecao_e_o_painel()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[^1];
        vm.Detail!.Translation = "Fim.";

        vm.Detail.ApproveAndNextCommand.Execute(null);

        Assert.Equal("yst00001:000002", vm.SelectedLine?.Id);
        Assert.Equal(TranslationStatus.Approved, vm.Detail?.Status);
    }

    [Fact]
    public async Task Gravacao_que_falha_mantem_a_selecao_e_o_painel_com_a_mensagem()
    {
        var vm = await Abrir();
        var primeira = vm.SelectedLine;
        var painel = vm.Detail!;
        painel.Translation = "Não vai gravar";
        File.Delete(ProjectFolder.Open(raiz).TextPath("yst00001"));

        vm.SelectedLine = vm.Lines[1];

        Assert.Same(primeira, vm.SelectedLine);
        Assert.Same(painel, vm.Detail);
        Assert.NotNull(painel.ErrorMessage);
        Assert.Equal("Não vai gravar", painel.Translation);
    }

    [Fact]
    public async Task Trocar_de_projeto_grava_e_descarta_o_painel()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Antes de trocar";

        vm.Project = EditorFixtures.Summary("Outro", Path.Combine(pasta.Path, "inexistente"));
        await vm.Loading;

        Assert.Null(vm.Detail);
        Assert.Null(vm.SelectedLine);
        Assert.Equal("Antes de trocar", Gravada(0).Translation);
    }
}
