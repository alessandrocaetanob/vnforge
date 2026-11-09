using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.Core.Images;

namespace VNForge.AI.Images;

/// <summary>
/// O <see cref="IImageTextReader"/> sobre o Claude com visão (Haiku por padrão): manda a imagem como bloco base64 e pede por
/// <c>output_config.format</c> um JSON <c>{ hasText, text, translation }</c> com a transcrição do texto em japonês e uma tradução sugerida. Recusa (<c>stop_reason: "refusal"</c>) vira
/// <see cref="ImageTextReading.Refused"/>; resposta cortada ou fora do formato é <see cref="ImageReadFailedException"/> (que traz o uso da chamada e a <see cref="ClaudeResponseException"/> por dentro).
/// </summary>
public sealed class ClaudeImageTextReader(IClaudeMessageClient client, ClaudeOptions? options = null) : IImageTextReader
{
    internal const string Schema = """
        {"type":"object","properties":{"hasText":{"type":"boolean"},"text":{"type":"string"},"translation":{"type":"string"}},"required":["hasText","text","translation"],"additionalProperties":false}
        """;

    private const string System = """
        Você ajuda a traduzir visual novels do japonês para o português do Brasil.
        Você recebe uma imagem tirada dos arquivos do jogo (menu, título, botão, aviso, cartaz, tela de sistema).
        Diga em "hasText" se ela tem texto em japonês (hiragana, katakana ou kanji) e, em "text", transcreva esse texto exatamente como está escrito,
        na ordem de leitura, uma linha da imagem por linha do texto. Não descreva a imagem.
        Em "translation", dê uma sugestão curta de tradução desse texto para o português do Brasil, numa linha só, como ele ficaria na imagem; vazia quando não há texto.
        Imagem sem texto, só com números, só com letras latinas ou só com enfeites não tem texto em japonês: devolva "hasText" falso, "text" e "translation" vazios.
        Responda apenas com o JSON pedido.
        """;

    private readonly ClaudeOptions _options = options ?? new ClaudeOptions { Model = ClaudeModels.Haiku, Effort = ClaudeEffort.Low, MaxTokens = 2000 };

    public async Task<ImageTextReading> ReadAsync(ImageToRead image, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        ct.ThrowIfCancellationRequested();

        var request = new ClaudeRequest(
            _options.Model,
            _options.Effort,
            _options.MaxTokens,
            System,
            $"Transcreva o texto em japonês desta imagem (arquivo: {image.Name}).",
            Schema,
            Image: new ClaudeImage(image.MediaType, image.Data));
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        var input = reply.Usage is { } usage ? usage.InputTokens + usage.CacheReadInputTokens + usage.CacheCreationInputTokens : 0;
        var output = reply.Usage?.OutputTokens ?? 0;
        try
        {
            return reply.StopReason switch
            {
                "refusal" => new ImageTextReading(string.Empty, Refused: true, input, output),
                "max_tokens" => throw new ClaudeResponseException("A IA não terminou de ler a imagem. Tente de novo.", detail: "stop_reason: max_tokens"),
                _ => Read(reply.Text, input, output),
            };
        }
        catch (ClaudeResponseException e)
        {
            // A API cobrou a chamada mesmo com a resposta inútil; o uso segue na exceção para entrar na conta do gasto.
            throw new ImageReadFailedException(e.Message, input, output, e);
        }
    }

    private static ImageTextReading Read(string json, long input, long output)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var hasText = document.RootElement.GetProperty("hasText").GetBoolean();
            var text = document.RootElement.GetProperty("text").GetString() ?? throw Incomplete("Resposta com o campo 'text' nulo.");
            var translation = document.RootElement.TryGetProperty("translation", out var suggested) ? suggested.GetString() : null;
            return hasText
                ? new ImageTextReading(text.Trim(), InputTokens: input, OutputTokens: output, Translation: translation?.Trim() ?? string.Empty)
                : new ImageTextReading(string.Empty, InputTokens: input, OutputTokens: output);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Incomplete("A resposta não é o JSON esperado.", e);
        }
    }

    private static ClaudeResponseException Incomplete(string detail, Exception? inner = null) =>
        new("A resposta da IA veio incompleta ou fora do formato. Tente de novo.", inner, detail);
}
