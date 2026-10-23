namespace VNForge.Core.Text;

/// <summary>
/// Converte o texto traduzido nos bytes que o jogo espera (ex.: Shift-JIS). É o que o
/// <see cref="Engines.IScriptFormat.Rebuild"/> usa para gravar cada <see cref="TextUnit.Translation"/>.
/// </summary>
/// <remarks>
/// <para>
/// O codificador recebe só texto exibível, sem marcadores de controle (<c>\n</c>, <c>\p</c>, <c>{VAR:...}</c>). A engine parte a
/// tradução nos marcadores, codifica cada trecho de texto e concatena os bytes de controle do jogo entre eles. Assim os bytes de
/// controle nunca passam pelo codificador, o que importa quando um caractere de texto colide com o prefixo de controle (no
/// <c>cp1252</c>, <c>ï</c> é 0xEF, o prefixo dos códigos de controle do YU-RIS; ver doc 06).
/// </para>
/// <para>
/// Nenhuma estratégia substitui um caractere em silêncio: o fallback padrão do .NET para o CP932 troca <c>ação</c> por <c>acao</c>
/// sem avisar, e isso quebraria o QA. Quem implementa usa um fallback que lança exceção.
/// </para>
/// A E2.2 traz a estratégia Shift-JIS pura (<see cref="ShiftJisTextEncoder"/>); <c>fold</c>, <c>remap</c> e <c>cp1252</c> entram no E5, escolhidas por <see cref="TextEncoderRegistry"/>.
/// </remarks>
public interface ITextEncoder
{
    /// <summary>Identificador estável da estratégia, em minúsculas (ex.: <c>"sjis"</c>).</summary>
    string Id { get; }

    /// <summary>Codifica o texto nos bytes do jogo, sem terminador e sem BOM. Nunca substitui nem descarta caractere.</summary>
    /// <param name="text">Texto exibível, sem marcadores de controle.</param>
    /// <exception cref="EncoderFallbackException">O texto tem caractere que a estratégia não representa.</exception>
    byte[] Encode(string text);

    /// <summary>
    /// Diz quais caracteres <see cref="Encode"/> recusaria, sem lançar exceção e sem efeito colateral. É o que a regra de QA
    /// "caractere não codificável" usa.
    /// </summary>
    /// <param name="text">Texto exibível, sem marcadores de controle.</param>
    /// <returns>Índices (posição em <c>char</c> do UTF-16) dos caracteres não representáveis, em ordem crescente; um par substituto conta uma vez, no índice do primeiro <c>char</c>. Vazio quando tudo é representável.</returns>
    IReadOnlyList<int> FindUnencodable(string text);
}
