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
    public async Task O_bloco_do_inspetor_compacto_so_aparece_com_algo_a_mostrar()
    {
        using var largo = Abrir(new ServicoFalso(Resposta));
        Assert.True(largo.ShowExplainBlock, "no inspetor largo o bloco leva o botão e o preço");

        using var vm = Abrir(new ServicoFalso(new LineExplanation([], ClaudeModels.Haiku, FromCache: false)));
        var avisados = new List<string?>();
        vm.PropertyChanged += (_, e) => avisados.Add(e.PropertyName);
        vm.ShowEditorSuggestion(new EditorSuggestion("yst00001", "yst00001:000000", EditorSuggestionKind.Fluency, "Hoje chove.\\p", "Hoje está chovendo.\\p", "Soa melhor.", relogio.GetUtcNow()));
        Assert.True(vm.IsCompactInspector);
        Assert.Contains(nameof(LineDetailViewModel.ShowExplainBlock), avisados);
        Assert.False(vm.ShowExplainBlock, "compacto e fechado: o botão mora na linha do Original e o bloco fica fora da pilha");

        // Aberto, sem partes e com o aviso de que não há japonês: há o que mostrar.
        await vm.ToggleExplainCommand.ExecuteAsync(null);
        Assert.True(vm.IsExplainOpen);
        Assert.Empty(vm.ExplainParts);
        Assert.True(vm.HasExplainNotice);
        Assert.True(vm.ShowExplainBlock);

        // Fechar limpa o aviso e o bloco sai de novo.
        await vm.ToggleExplainCommand.ExecuteAsync(null);
        Assert.False(vm.IsExplainOpen);
        Assert.False(vm.ShowExplainBlock);

        // Uma falha também aparece; "aberto sem partes" sem pedido, erro nem aviso não tem filho visível e fica oculto.
        var falho = new ServicoFalso(erro: new InvalidOperationException("sem rede"));
        using var comErro = Abrir(falho);
        comErro.ShowEditorSuggestion(new EditorSuggestion("yst00001", "yst00001:000000", EditorSuggestionKind.Fluency, "Hoje chove.\\p", "Hoje está chovendo.\\p", "Soa melhor.", relogio.GetUtcNow()));
        await comErro.ToggleExplainCommand.ExecuteAsync(null);
        Assert.True(comErro.HasExplainError);
        Assert.True(comErro.ShowExplainBlock);
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

        Assert.Null(vm.ExplainError);
        Assert.True(vm.IsExplainOpen);
        Assert.Equal(2, vm.ExplainParts.Count);
        Assert.Equal(2, servico.Pedidos);

        await vm.ToggleExplainCommand.ExecuteAsync(null);

        Assert.False(vm.IsExplainOpen);
    }

    [Fact]
    public async Task Servico_do_app_sem_chave_avisa_e_com_chave_usa_o_explicador_do_projeto()
    {
        string? chave = null;
        var criados = new List<(string Pasta, string Chave)>();
        var cotacao = new ExchangeRate(5.40m);
        var servico = new LineExplanations(() => chave, () => cotacao, (pastaProjeto, k) =>
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
        Assert.Equal("menos de R$ 0,01 (cotação de exemplo)", servico.PriceText);

        cotacao = new ExchangeRate(5.40m, new DateOnly(2026, 10, 25));

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
