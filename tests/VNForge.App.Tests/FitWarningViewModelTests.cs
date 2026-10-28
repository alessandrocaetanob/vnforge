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
    public void Texto_que_nao_cabe_mostra_o_titulo_quanto_passa_e_os_botoes()
    {
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => Task.FromResult("x")));

        Assert.True(cenario.Aviso.IsVisible);
        Assert.Equal("Não cabe na caixa do jogo", FitWarningViewModel.TitleText);
        Assert.Equal("Passa 1 linha: a caixa do jogo tem 3 linhas.", cenario.Aviso.Message);
        Assert.Equal("Dividir em 2 páginas", cenario.Aviso.SplitText);
        Assert.True(cenario.Aviso.SplitCommand.CanExecute(null));
        Assert.True(cenario.Aviso.ShortenCommand.CanExecute(null));
    }

    [Fact]
    public void Passar_mais_de_uma_linha_usa_o_plural_e_o_numero_de_paginas_certo()
    {
        var cenario = new Cenario(Palavras(28), null);

        Assert.Equal("Passa 4 linhas: a caixa do jogo tem 3 linhas.", cenario.Aviso.Message);
        Assert.Equal("Dividir em 3 páginas", cenario.Aviso.SplitText);
    }

    [Fact]
    public async Task Dividir_entrega_o_texto_com_a_virada_de_pagina_e_o_aviso_some()
    {
        var cenario = new Cenario(Palavras(16), null);

        await cenario.Aviso.SplitCommand.ExecuteAsync(null);
        cenario.Aviso.Update();

        Assert.Equal($@"{Palavras(12)}\p{Palavras(4)}", Assert.Single(cenario.Divididos));
        Assert.False(cenario.Aviso.IsVisible);
    }

    [Fact]
    public async Task Encurtar_entrega_a_versao_que_cabe()
    {
        var encurtador = new Encurtador((atual, _) => Task.FromResult("Cabe bem."));
        var cenario = new Cenario(Palavras(16), encurtador);

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Equal("Cabe bem.", Assert.Single(cenario.Encurtados));
        Assert.Null(cenario.Aviso.Error);
        Assert.False(cenario.Aviso.IsBusy);
        Assert.Equal(1, encurtador.Chamadas);
    }

    [Fact]
    public async Task Encurtar_pede_a_traducao_de_hoje()
    {
        string? recebida = null;
        var cenario = new Cenario(Palavras(16), new Encurtador((atual, _) =>
        {
            recebida = atual;
            return Task.FromResult("Cabe.");
        }));

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Equal(Palavras(16), recebida);
    }

    [Fact]
    public async Task Versao_que_ainda_nao_cabe_nao_e_entregue_e_o_aviso_diz_por_que()
    {
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => Task.FromResult(Palavras(14))));

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Empty(cenario.Encurtados);
        Assert.Contains("ainda não cabe", cenario.Aviso.Error);
        Assert.True(cenario.Aviso.HasError);
        Assert.Equal(Palavras(16), cenario.Traducao);
    }

    [Fact]
    public async Task Recusa_da_ia_aparece_como_erro_e_a_traducao_nao_muda()
    {
        var recusa = new LineSuggester(() => new ReescritaFixa(refused: true));
        var cenario = new Cenario(Palavras(16), recusa);

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Empty(cenario.Encurtados);
        Assert.Contains("recusou", cenario.Aviso.Error);
        Assert.False(cenario.Aviso.IsBusy);
    }

    [Fact]
    public async Task Provedor_falso_devolve_uma_versao_curta_e_ela_e_entregue()
    {
        var falso = new ReescritaFixa(@"Versão curta.\p");
        var cenario = new Cenario(Palavras(16), new LineSuggester(() => falso));

        await cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Equal(@"Versão curta.\p", Assert.Single(cenario.Encurtados));
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
    public async Task Fica_ocupado_enquanto_a_ia_responde_e_nao_aceita_outro_pedido()
    {
        var resposta = new TaskCompletionSource<string>();
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => resposta.Task));

        var pedido = cenario.Aviso.ShortenCommand.ExecuteAsync(null);

        Assert.True(cenario.Aviso.IsBusy);
        Assert.False(cenario.Aviso.ShortenCommand.CanExecute(null));
        Assert.False(cenario.Aviso.SplitCommand.CanExecute(null));

        resposta.SetResult("Cabe.");
        await pedido;

        Assert.False(cenario.Aviso.IsBusy);
        Assert.Equal("Cabe.", Assert.Single(cenario.Encurtados));
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
    public async Task Resposta_que_chega_depois_do_cancelamento_nao_e_entregue()
    {
        var resposta = new TaskCompletionSource<string>();
        var cenario = new Cenario(Palavras(16), new Encurtador((_, _) => resposta.Task));

        var pedido = cenario.Aviso.ShortenCommand.ExecuteAsync(null);
        cenario.Aviso.Cancel();
        resposta.SetResult("Cabe.");
        await pedido;

        Assert.Empty(cenario.Encurtados);
        Assert.Null(cenario.Aviso.Error);
        Assert.False(cenario.Aviso.IsBusy);
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
