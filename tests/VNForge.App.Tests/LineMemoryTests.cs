using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O cartão da memória de tradução no inspetor (#537): sugestões de outros projetos, "Usar" no campo e o registro das aprovações.</summary>
public sealed class LineMemoryTests : IDisposable
{
    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineMemoryTests() => raiz = SampleProjects.Create(Path.Combine(pasta.Path, "atual"), "Amaoto no Hakoniwa", TranslationStatus.Machine);

    public void Dispose() => pasta.Dispose();

    private string Config => Path.Combine(pasta.Path, "config");

    private static TextUnit Aprovada(string id, string traducao) =>
        new(id, TextUnitKind.Dialogue, "美咲", Frase, traducao, TranslationStatus.Approved);

    private TranslationMemory Alimentar()
    {
        var memoria = new TranslationMemory(Config);
        memoria.Record(TranslationMemory.KeyOf(Path.Combine(pasta.Path, "estrela")), "Hoshizora no Kakera", Aprovada("a:000001", "Aizawa, Sakura. Vocês dois, cinco minutos atrasados."), relogio.GetUtcNow().AddDays(-56));
        memoria.Record(TranslationMemory.KeyOf(Path.Combine(pasta.Path, "verao")), "Natsu no Shirabe", Aprovada("b:000001", "Aizawa, Sakura. Os dois, cinco minutos atrasados."), relogio.GetUtcNow().AddDays(-38));
        return memoria;
    }

    private LineDetailViewModel Abrir(IMemorySuggestions? memoria, string? traducao = "Aizawa, os dois, atrasados.")
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", Frase, traducao, TranslationStatus.Machine);
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new Navegador(), relogio, acao => acao())
        {
            Memory = memoria,
            Scene = [new ContextLine("Kenta", "あ", null, false), new ContextLine("Ren", "い", null, true)],
        };
    }

    private ProjectMemory DoProjeto(TranslationMemory memoria) => new(memoria, raiz, "Amaoto no Hakoniwa", relogio);

    [Fact]
    public async Task Mostra_as_sugestoes_de_outros_projetos_com_percentual_e_origem()
    {
        using var vm = Abrir(DoProjeto(Alimentar()));

        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.True(vm.HasMemory);
        Assert.Equal("2 em 2 projetos", vm.MemorySummary);
        Assert.Equal(["100%", "100%"], vm.MemorySuggestions.Select(s => s.PercentText));
        Assert.All(vm.MemorySuggestions, s => Assert.True(s.IsExact));
        var primeira = vm.MemorySuggestions[0];
        Assert.Equal("Natsu no Shirabe · aprovada em 02/10", primeira.Origin);
    }

    [Fact]
    public async Task Correspondencia_aproximada_vem_com_percentual_abaixo_de_cem()
    {
        var memoria = new TranslationMemory(Config);
        memoria.Record("estrela", "Hoshizora no Kakera", Aprovada("a:000001", "Aizawa, os dois, cinco minutos atrasados.") with { Source = "「相沢、二人そろって五分遅刻だ」" }, relogio.GetUtcNow());
        using var vm = Abrir(DoProjeto(memoria));

        vm.ShowMemory();
        await vm.MemoryLoading;

        var sugestao = Assert.Single(vm.MemorySuggestions);
        Assert.InRange(sugestao.Percent, 80, 99);
        Assert.False(sugestao.IsExact);
        Assert.Equal("1 em 1 projeto", vm.MemorySummary);
    }

    [Fact]
    public async Task Usar_poe_o_texto_no_campo_sem_aprovar()
    {
        using var vm = Abrir(DoProjeto(Alimentar()));
        vm.ShowMemory();
        await vm.MemoryLoading;

        vm.UseMemoryCommand.Execute(vm.MemorySuggestions[1]);

        Assert.Equal("Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", vm.Translation);
        Assert.NotEqual(TranslationStatus.Approved, vm.Status);
    }

    [Fact]
    public async Task Sem_memoria_ou_sem_parecido_o_cartao_nao_aparece_e_a_cena_continua_aberta()
    {
        using var semMemoria = Abrir(null);
        semMemoria.ShowMemory();
        await semMemoria.MemoryLoading;
        using var vazia = Abrir(DoProjeto(new TranslationMemory(Config)));
        vazia.ShowMemory();
        await vazia.MemoryLoading;

        Assert.False(semMemoria.HasMemory);
        Assert.False(vazia.HasMemory);
        Assert.False(vazia.IsSceneCollapsed);
    }

    [Fact]
    public async Task Com_o_cartao_a_cena_fica_recolhida_e_o_cabecalho_abre()
    {
        using var vm = Abrir(DoProjeto(Alimentar()));
        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.True(vm.IsSceneCollapsed);
        Assert.Equal("2 falas", vm.SceneCountText);

        vm.ToggleSceneCommand.Execute(null);

        Assert.False(vm.IsSceneCollapsed);
    }

    [Fact]
    public async Task Traducao_igual_a_atual_nao_e_repetida_no_cartao()
    {
        using var vm = Abrir(DoProjeto(Alimentar()), "Aizawa, Sakura. Os dois, cinco minutos atrasados.");
        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.Equal(["Aizawa, Sakura. Vocês dois, cinco minutos atrasados."], vm.MemorySuggestions.Select(s => s.Target));
        Assert.Equal("1 em 1 projeto", vm.MemorySummary);
    }

    [Fact]
    public async Task Arquivo_da_memoria_ilegivel_vale_como_sem_sugestoes()
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, TranslationMemory.FileName), "lixo");
        using var vm = Abrir(DoProjeto(new TranslationMemory(Config)));

        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.False(vm.HasMemory);
        Assert.True(vm.HasMemoryProblem);
        Assert.True(vm.ShowMemoryCard);
        Assert.Contains(Path.Combine(Config, TranslationMemory.FileName), vm.MemoryProblem);
        Assert.Contains("não pôde ser lida", vm.MemoryProblem);
        Assert.Contains("O arquivo não foi alterado.", vm.MemoryProblem);
        Assert.Equal("lixo", File.ReadAllText(Path.Combine(Config, TranslationMemory.FileName)));
    }

    [Fact]
    public async Task Arquivo_legivel_sem_parecido_nao_mostra_o_cartao_nem_aviso()
    {
        using var vm = Abrir(DoProjeto(new TranslationMemory(Config)));

        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.False(vm.ShowMemoryCard);
        Assert.Null(vm.MemoryProblem);
    }

    [Fact]
    public async Task Abrir_o_projeto_leva_as_aprovadas_para_a_memoria_e_aprovar_uma_linha_a_registra()
    {
        var memoria = new TranslationMemory(Config);
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001", [Aprovada("yst00001:000000", "Já aprovada."), new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, "別の文", "Outra.", TranslationStatus.Translated)]);
        var editor = new EditorViewModel(clock: relogio, postToUi: acao => acao()) { Memory = memoria };

        editor.Project = EditorFixtures.Summary("Amaoto no Hakoniwa", raiz);
        await editor.Loading;
        await editor.MemoryIdle;

        Assert.Equal(["yst00001:000000"], memoria.All().Select(e => e.UnitId));

        new LineEditor(projeto, relogio).Approve("yst00001", "yst00001:000001");
        editor.Reload();
        await editor.Loading;
        await editor.MemoryIdle;

        Assert.Equal(2, memoria.Stats().Entries);
    }

    private sealed class Navegador : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    [Fact]
    public async Task Projeto_de_origem_com_o_mesmo_nome_do_atual_nao_aparece_no_cartao()
    {
        var memoria = new TranslationMemory(Config);
        memoria.Record(TranslationMemory.KeyOf(Path.Combine(pasta.Path, "antigo")), "Amaoto no Hakoniwa", Aprovada("a:000001", "Do projeto movido."), relogio.GetUtcNow());
        using var vm = Abrir(DoProjeto(memoria));

        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.False(vm.HasMemory);
    }

    [Fact]
    public void ProjectMemory_com_arquivo_ilegivel_nao_derruba_a_edicao()
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, TranslationMemory.FileName), """{"version":1,"entries":[{"target":"x"}]}""");
        var projeto = DoProjeto(new TranslationMemory(Config));

        Assert.Empty(projeto.Find(Frase));
        projeto.Record(Aprovada("a:000001", "x"));
        projeto.Sync([Aprovada("a:000001", "x")]);
        Assert.Contains(TranslationMemory.FileName, projeto.Problem);
    }

    [Fact]
    public async Task A_data_da_aprovacao_usa_o_fuso_do_relogio_injetado()
    {
        var memoria = new TranslationMemory(Config);
        memoria.Record(TranslationMemory.KeyOf(Path.Combine(pasta.Path, "estrela")), "Hoshizora no Kakera", Aprovada("a:000001", "Atrasados."), new DateTimeOffset(2026, 9, 15, 1, 30, 0, TimeSpan.Zero));
        var relogioBrasilia = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        relogioBrasilia.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("teste", TimeSpan.FromHours(-3), "teste", "teste"));
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", Frase, "Outra.", TranslationStatus.Machine);
        using var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogioBrasilia), new Navegador(), relogioBrasilia, acao => acao())
        {
            Memory = DoProjeto(memoria),
        };

        vm.ShowMemory();
        await vm.MemoryLoading;

        Assert.Equal("Hoshizora no Kakera · aprovada em 14/09", vm.MemorySuggestions.Single().Origin);
    }
}
