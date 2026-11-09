using VNForge.Core.Text;

namespace VNForge.Engines.ConformanceKit;

/// <summary>Um script sintético da engine e o que a extração dele deve devolver, na ordem do arquivo.</summary>
/// <param name="Name">Nome do script no jogo (ex.: <c>"yst00001.ybn"</c>); a extensão escolhe o <c>IScriptFormat</c> da engine.</param>
/// <param name="Content">Bytes do script, montados pela engine com os construtores sintéticos dos testes dela (regra 1 do <c>CLAUDE.md</c>: nada de arquivo de jogo real).</param>
/// <param name="Units">As unidades que <c>Extract</c> deve devolver.</param>
public sealed record ConformanceScript(string Name, byte[] Content, IReadOnlyList<ExpectedUnit> Units);

/// <summary>Uma unidade esperada da extração de um <see cref="ConformanceScript"/>.</summary>
/// <param name="Kind">Tipo da unidade.</param>
/// <param name="Speaker">Falante que a extração deve atribuir; nulo quando a unidade não tem falante.</param>
/// <param name="Source">Texto de origem, já sem o que a engine tira do script (marcador de falante, delimitadores).</param>
public sealed record ExpectedUnit(TextUnitKind Kind, string? Speaker, string Source)
{
    /// <summary>
    /// Tradução que a engine precisa gravar e reler sem perder nada. Nula quando a unidade não entra nos testes de tradução.
    /// Precisa ser diferente de <see cref="Source"/>, senão a reescrita mínima nem a toca, encodável pelo codificador do kit e ainda
    /// extraível: o kit relê o script reconstruído e espera achar a unidade, no mesmo id, com este texto. Se a engine só extrai certos textos
    /// (o YU-RIS pula argumento de comando sem japonês), escolha uma tradução que continue passando nesse filtro ou deixe a unidade sem tradução.
    /// </summary>
    public string? Translation { get; init; }

    /// <summary>
    /// Nome que o falante passa a ter depois da tradução (ex.: <c>"Misaki"</c> para <c>美咲</c>); só faz sentido numa unidade com <see cref="Speaker"/>.
    /// O kit grava de dois jeitos, trocando <see cref="TextUnit.Speaker"/> (o nome já resolvido pelo build) e preenchendo <see cref="TextUnit.ChosenSpeaker"/>,
    /// e espera reler este nome como falante nos dois. Precisa ser diferente de <see cref="Speaker"/> e encodável pelo codificador do kit. Nulo tira a unidade desses testes.
    /// </summary>
    public string? SpeakerTranslation { get; init; }

    /// <summary>
    /// Marcadores de controle (quebra de linha, espera, troca de variável...) que aparecem em <see cref="Source"/> e em
    /// <see cref="Translation"/> e que a tradução precisa manter depois de gravada e relida.
    /// </summary>
    public IReadOnlyList<string> Markers { get; init; } = [];
}

/// <summary>
/// Uma faixa contígua dos bytes de um script, no vocabulário do formato da engine (cabeçalho, instruções, um argumento, recursos...).
/// As faixas de <see cref="EngineConformanceTests"/>.<c>DescribeScript</c> têm que cobrir o script inteiro, sem buracos nem sobreposição.
/// </summary>
/// <remarks>
/// O kit compara as faixas do script original com as do reconstruído (regra 3 do <c>CLAUDE.md</c>): a faixa que não é declarada mutável sai
/// byte a byte igual. Os três marcadores abaixo dizem quando uma faixa pode mudar; sem nenhum, ela nunca muda.
/// </remarks>
/// <param name="Name">Nome da faixa, estável entre o original e o reconstruído (aparece na mensagem de falha).</param>
/// <param name="Bytes">Os bytes da faixa, como estão no arquivo.</param>
public sealed record ScriptRegion(string Name, ReadOnlyMemory<byte> Bytes)
{
    /// <summary>Posição, em <c>ConformanceScript.Units</c>, da unidade dona da faixa: ela pode mudar (com o mesmo tamanho) só quando essa unidade é reescrita.</summary>
    public int? Unit { get; init; }

    /// <summary>A faixa pode mudar (com o mesmo tamanho) quando qualquer unidade é reescrita, como um campo de tamanho no cabeçalho.</summary>
    public bool Shared { get; init; }

    /// <summary>
    /// A faixa só cresce no fim quando qualquer unidade é reescrita: o conteúdo original continua no começo, byte a byte. É o caso de uma área
    /// de textos em que a engine acrescenta o novo e deixa o antigo órfão.
    /// </summary>
    public bool Appends { get; init; }
}
