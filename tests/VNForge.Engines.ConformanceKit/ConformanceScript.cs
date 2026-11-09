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
    /// Marcadores de controle (quebra de linha, espera, troca de variável...) que aparecem em <see cref="Source"/> e em
    /// <see cref="Translation"/> e que a tradução precisa manter depois de gravada e relida.
    /// </summary>
    public IReadOnlyList<string> Markers { get; init; } = [];
}
