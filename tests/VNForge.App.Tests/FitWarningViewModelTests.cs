using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class FitWarningViewModelTests
{
    private static readonly FormattingProfile Caixa = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) };

    private static readonly TextUnit Linha = new("yst00001:000000", TextUnitKind.Dialogue, null, @"原文\p", null, TranslationStatus.Untranslated);

    private static string Palavras(int quantas) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", quantas));

    private sealed class Cenario
    {
        public string Traducao { get; set; }

        public List<string> Divididos { get; } = [];

        public List<string> Encurtados { get; } = [];

        public FitWarningViewModel Aviso { get; }

        public Cenario(string traducao, ILineShortener? encurtador)
        {
            Traducao = traducao;
            Aviso = new FitWarningViewModel(
                Linha,
                Caixa,
                encurtador,
                () => Traducao,
                texto =>
                {
                    Divididos.Add(texto);
                    Traducao = texto;
                    return Task.CompletedTask;
                },
                texto =>
                {
                    Encurtados.Add(texto);
                    Traducao = texto;
                    return Task.CompletedTask;
                });
            Aviso.Update();
        }
    }

    private sealed class Encurtador(Func<string, CancellationToken, Task<string>> resposta) : ILineShortener
    {
        public int Chamadas { get; private set; }

        public Task<string> ShortenAsync(TextUnit unit, string current, WrapRule box, CancellationToken ct)
        {
            Chamadas++;
            return resposta(current, ct);
        }
    }

    [Fact]
    public void Texto_que_cabe_nao_mostra_o_aviso()
    {
        var cenario = new Cenario(Palavras(12), new Encurtador((_, _) => Task.FromResult("x")));

        Assert.False(cenario.Aviso.IsVisible);
        Assert.False(cenario.Aviso.SplitCommand.CanExecute(null));
        Assert.False(cenario.Aviso.ShortenCommand.CanExecute(null));
    }

    [Fact]
    public void Texto_vazio_ou_com_marcador_invalido_nao_mostra_o_aviso()
    {
        Assert.False(new Cenario(string.Empty, null).Aviso.IsVisible);
        Assert.False(new Cenario(@"texto \x invalido", null).Aviso.IsVisible);
    }

    [Fact]
    public async Task Falha_de_rede_vira_mensagem_em_portugues()
    {
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => Task.FromException<string>(new HttpRequestException("boom"))));

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Contains("Falha de rede", cenario.Aviso.Error);
        Assert.Empty(cenario.Encurtados);
    }

    [Fact]
    public async Task Cancelar_interrompe_o_pedido_sem_mostrar_erro()
    {
        var iniciado = new TaskCompletionSource();
        var cenario = new Cenario(Palavras(16), new Encurtador(async (_, ct) =>
        {
            iniciado.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return string.Empty;
        }));

        var pedido = cenario.Aviso.ShortenCommand.ExecuteAsync(null);
        await iniciado.Task;
        cenario.Aviso.Cancel();
        await pedido;

        Assert.Null(cenario.Aviso.Error);
        Assert.False(cenario.Aviso.IsBusy);
        Assert.Empty(cenario.Encurtados);
    }

    [Fact]
    public async Task Sem_encurtador_so_a_divisao_fica_disponivel()
    {
        var cenario = new Cenario(Palavras(16), null);

        Assert.False(cenario.Aviso.HasShortener);
        Assert.False(cenario.Aviso.ShortenCommand.CanExecute(null));
        Assert.True(cenario.Aviso.SplitCommand.CanExecute(null));
        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);
        Assert.Empty(cenario.Encurtados);
    }

    [Fact]
    public async Task Atualizar_limpa_o_erro_do_pedido_anterior()
    {
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => Task.FromResult(Palavras(14))));
        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);
        Assert.True(cenario.Aviso.HasError);

        cenario.Traducao = Palavras(20);
        cenario.Aviso.Update();

        Assert.False(cenario.Aviso.HasError);
        Assert.Equal("Passa 2 linhas: a caixa do jogo tem 3 linhas.", cenario.Aviso.Message);
    }
}
