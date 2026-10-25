using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Explain;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Entender o japonês" no inspetor: fechado por padrão, pede só ao clique, não entra na tradução.</summary>
public sealed class LineExplainTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineExplainTests() => raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Machine);

    public void Dispose() => pasta.Dispose();

    private static readonly LineExplanation Resposta = new(
        [new ExplainedPart("今日は", "hoje (tema)"), new ExplainedPart("雨が", "a chuva (sujeito)")],
        ClaudeModels.Haiku,
        FromCache: false);

    private LineDetailViewModel Abrir(ILineExplanations? explicacoes)
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", "今日は雨が降っている。\\p", "Hoje chove.\\p", TranslationStatus.Machine);
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new Navegador(), relogio, acao => acao())
        {
            Explanations = explicacoes,
            ProjectRoot = raiz,
        };
    }

    [Fact]
    public void Comeca_fechado_sem_pedir_nada()
    {
        var servico = new ServicoFalso(Resposta);
        using var vm = Abrir(servico);

        Assert.False(vm.IsExplainOpen);
        Assert.Empty(vm.ExplainParts);
        Assert.Equal(0, servico.Pedidos);
        Assert.Equal("menos de R$ 0,01", vm.ExplainPriceText);
    }

    [Fact]
    public async Task Abrir_pede_a_explicacao_e_mostra_as_partes_do_original()
    {
        var servico = new ServicoFalso(Resposta);
        using var vm = Abrir(servico);

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.True(vm.IsExplainOpen);
        Assert.Equal([new ExplainPart("今日は", "hoje (tema)"), new ExplainPart("雨が", "a chuva (sujeito)")], vm.ExplainParts);
        Assert.Equal("Claude Haiku 5.5", vm.ExplainModel);
        Assert.Equal("今日は雨が降っている。\\p", servico.UltimoOriginal);
        Assert.Equal(raiz, servico.UltimaPasta);
        Assert.Null(vm.ExplainError);
    }

    [Fact]
    public async Task Fechar_e_abrir_de_novo_nao_pede_outra_vez()
    {
        var servico = new ServicoFalso(Resposta);
        using var vm = Abrir(servico);

        await vm.ToggleExplainCommand.ExecuteAsync(null);
        await vm.ToggleExplainCommand.ExecuteAsync(null);
        Assert.False(vm.IsExplainOpen);
        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.True(vm.IsExplainOpen);
        Assert.Equal(1, servico.Pedidos);
    }

    [Fact]
    public async Task A_explicacao_nao_mexe_na_traducao()
    {
        using var vm = Abrir(new ServicoFalso(Resposta));
        var antes = (vm.Translation, vm.FieldText, vm.Status);

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.Equal(antes, (vm.Translation, vm.FieldText, vm.Status));
        Assert.True(vm.Flush());
        Assert.Equal("tradução 0", ProjectFolder.Open(raiz).LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Recusa_do_modelo_vira_mensagem_em_portugues_e_deixa_pedir_de_novo()
    {
        var servico = new ServicoFalso(erro: new ClaudeRefusalException("A IA recusou explicar esta fala."));
        using var vm = Abrir(servico);

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.Equal("A IA recusou explicar esta fala.", vm.ExplainError);
        Assert.Empty(vm.ExplainParts);

        servico.Erro = null;
        servico.Resposta = Resposta;
        await vm.ToggleExplainCommand.ExecuteAsync(null);
        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.Null(vm.ExplainError);
        Assert.Equal(2, vm.ExplainParts.Count);
    }

    [Fact]
    public async Task Sem_chave_a_mensagem_manda_para_Ajustes()
    {
        var servico = new ServicoFalso(erro: new LineExplanationException("Para entender o japonês, informe a chave da IA em Ajustes."));
        using var vm = Abrir(servico);

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.Equal("Para entender o japonês, informe a chave da IA em Ajustes.", vm.ExplainError);
    }

    [Fact]
    public async Task Falha_de_rede_aparece_sem_detalhes_tecnicos()
    {
        using var vm = Abrir(new ServicoFalso(erro: new HttpRequestException("boom")));

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.Equal("Falha de rede ou tempo esgotado ao falar com a IA. Confira a conexão e tente de novo.", vm.ExplainError);
    }

    [Fact]
    public void Sem_servico_o_comando_fica_desligado()
    {
        using var vm = Abrir(null);

        Assert.False(vm.ToggleExplainCommand.CanExecute(null));
        Assert.False(vm.CanExplain);
    }

    [Fact]
    public async Task Servico_do_app_sem_chave_avisa_e_com_chave_usa_o_explicador_do_projeto()
    {
        string? chave = null;
        var criados = new List<(string Pasta, string Chave)>();
        var servico = new LineExplanations(() => chave, () => new ExchangeRate(5.40m), (pastaProjeto, k) =>
        {
            criados.Add((pastaProjeto, k));
            return new ExplicadorFalso(Resposta);
        });

        Assert.False(servico.IsAvailable);
        await Assert.ThrowsAsync<LineExplanationException>(() => servico.ExplainAsync(raiz, "x", CancellationToken.None));

        chave = "sk-ant-teste";
        var resposta = await servico.ExplainAsync(raiz, "x", CancellationToken.None);
        await servico.ExplainAsync(raiz, "y", CancellationToken.None);

        Assert.True(servico.IsAvailable);
        Assert.Equal(Resposta, resposta);
        Assert.Equal([(raiz, "sk-ant-teste")], criados);
        Assert.Equal("menos de R$ 0,01", servico.PriceText);
    }

    private sealed class ServicoFalso(LineExplanation? resposta = null, Exception? erro = null) : ILineExplanations
    {
        public LineExplanation? Resposta { get; set; } = resposta;

        public Exception? Erro { get; set; } = erro;

        public int Pedidos { get; private set; }

        public string? UltimoOriginal { get; private set; }

        public string? UltimaPasta { get; private set; }

        public bool IsAvailable => true;

        public string PriceText => "menos de R$ 0,01";

        public Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct)
        {
            Pedidos++;
            UltimaPasta = projectRoot;
            UltimoOriginal = source;
            return Erro is { } e ? Task.FromException<LineExplanation>(e) : Task.FromResult(Resposta!);
        }
    }

    private sealed class ExplicadorFalso(LineExplanation resposta) : ILineExplainer
    {
        public Task<LineExplanation> ExplainAsync(string source, CancellationToken ct) => Task.FromResult(resposta);
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
}
