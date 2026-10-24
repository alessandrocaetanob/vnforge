using System.Net;
using Anthropic.Exceptions;
using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

public class ClaudeErrorMessagesTests
{
    private static readonly HttpRequestException Rede = new("connection reset");

    [Fact]
    public void Chave_recusada_manda_conferir_os_ajustes()
    {
        Assert.Contains("chave", ClaudeErrorMessages.Describe(new AnthropicUnauthorizedException(Rede) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
        Assert.Contains("Ajustes", ClaudeErrorMessages.Describe(new AnthropicForbiddenException(Rede) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
    }

    [Fact]
    public void Limite_de_uso_pede_para_tentar_de_novo()
    {
        Assert.Contains("limite", ClaudeErrorMessages.Describe(new AnthropicRateLimitException(Rede) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
    }

    [Fact]
    public void Erro_do_servidor_diz_que_o_servico_esta_indisponivel()
    {
        Assert.Contains("indisponível", ClaudeErrorMessages.Describe(new Anthropic5xxException(Rede) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
    }

    [Fact]
    public void Falha_de_rede_e_tempo_esgotado_dizem_para_conferir_a_conexao()
    {
        Assert.Contains("conexão", ClaudeErrorMessages.Describe(new AnthropicIOException("boom", Rede)));
        Assert.Contains("conexão", ClaudeErrorMessages.Describe(Rede));
        Assert.Contains("conexão", ClaudeErrorMessages.Describe(new TimeoutException()));
        Assert.Contains("conexão", ClaudeErrorMessages.Describe(new TaskCanceledException()));
    }

    [Fact]
    public void Recusa_do_modelo_e_resposta_invalida_usam_a_mensagem_em_portugues_do_proprio_erro()
    {
        Assert.Equal("O modelo recusou a linha.", ClaudeErrorMessages.Describe(new ClaudeRefusalException("O modelo recusou a linha.")));
        Assert.Equal("Cortada.", ClaudeErrorMessages.Describe(new ClaudeResponseException("Cortada.")));
    }

    [Fact]
    public void Outros_erros_viram_uma_frase_generica_sem_o_texto_em_ingles()
    {
        var texto = ClaudeErrorMessages.Describe(new AnthropicApiException("400 invalid_request_error: bad field", Rede) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" });

        Assert.DoesNotContain("invalid_request_error", texto);
        Assert.Contains("recusou o pedido", texto);

        var inesperado = ClaudeErrorMessages.Describe(new NullReferenceException("Object reference not set"));
        Assert.DoesNotContain("Object reference", inesperado);
        Assert.StartsWith("Erro inesperado", inesperado);
    }
}
