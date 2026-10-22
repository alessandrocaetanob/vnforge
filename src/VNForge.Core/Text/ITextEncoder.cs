namespace VNForge.Core.Text;

/// <summary>
/// Converte o texto traduzido nos bytes que o jogo espera (ex.: Shift-JIS). É o que o
/// <see cref="Engines.IScriptFormat.Rebuild"/> usa para gravar cada <see cref="TextUnit.Translation"/>.
/// </summary>
/// <remarks>
/// Esta é só a superfície mínima que a reconstrução de scripts precisa (E2.1). A E2.2 completa o contrato
/// (relatório de caracteres não representáveis e a estratégia Shift-JIS pura); <c>fold</c>, <c>remap</c> e
/// <c>cp1252</c> entram no E5.
/// </remarks>
public interface ITextEncoder
{
    /// <summary>Identificador estável da estratégia, em minúsculas (ex.: <c>"sjis"</c>).</summary>
    string Id { get; }

    /// <summary>Codifica o texto nos bytes do jogo, sem terminador e sem BOM.</summary>
    /// <param name="text">Texto já com os marcadores de controle convertidos de volta pela engine.</param>
    byte[] Encode(string text);
}
