using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Uma linha traduzível: o original, a tradução e onde ela mora no script. É o registro de <c>text/*.json</c>
/// (um arquivo por script) e o que as engines extraem e reinjetam. Serializar e ler passam por <see cref="TextUnitJson"/>.
/// </summary>
/// <remarks>
/// A igualdade do record compara <see cref="Flags"/> e <see cref="Locator"/> por referência; para comparar o
/// conteúdo, compare o JSON de <see cref="TextUnitJson.Serialize"/>.
/// </remarks>
/// <param name="Id">
/// Identificador único dentro do projeto e estável entre extrações do mesmo script: a engine o monta com
/// <see cref="Engines.ScriptFile.UnitId"/> (chave do script mais uma posição estável e única no script), nunca com dados que
/// mudem entre execuções. O id é opaco: ninguém o decodifica, o <see cref="Locator"/> é que localiza o trecho. A ordem das unidades na lista é a da extração (a do arquivo), não a dos ids. A re-extração (E3.5) casa as traduções salvas pelo <c>Id</c> e confere se o <see cref="Source"/> continua o mesmo.
/// </param>
/// <param name="Kind">Papel da linha no jogo.</param>
/// <param name="Speaker">Nome do falante, separado do texto quando a engine consegue; nulo se não há falante ou se ele não pode ser separado.</param>
/// <param name="Source">Texto original, com os códigos de controle do jogo convertidos em marcadores legíveis (<c>\n</c>, <c>\p</c>, <c>\c</c>, <c>{VAR:...}</c>).</param>
/// <param name="Translation">Tradução, com os mesmos marcadores do original; nula enquanto não há tradução.</param>
/// <param name="Status">Etapa do fluxo de tradução.</param>
public sealed record TextUnit(
    [property: JsonRequired] string Id,
    [property: JsonRequired] TextUnitKind Kind,
    string? Speaker,
    [property: JsonRequired] string Source,
    string? Translation,
    [property: JsonRequired] TranslationStatus Status)
{
    /// <summary>Quem produziu a tradução; nulo quando não há tradução ou ela é anterior ao registro de origem.</summary>
    public TranslationOrigin? Origin { get; init; }

    /// <summary>
    /// A dúvida que a IA registrou ao produzir a <see cref="Translation"/> (trocadilho, sujeito oculto, gênero incerto, referência cultural, tom); nula quando não há.
    /// Aprovar a linha ou reescrever a tradução a encerra. Fica fora do JSON quando nula.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TranslationDoubt? Doubt { get; init; }

    /// <summary>Observações livres do tradutor ou do revisor. Vazio quando não há; <c>null</c> no JSON é recusado.</summary>
    public string Notes { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>Avisos do QA que o revisor marcou como "ok" (ids de regra). Nunca nulo; <c>null</c> no JSON e elemento nulo na lista são recusados.</summary>
    public IReadOnlyList<string> Flags { get; init => field = value ?? []; } = [];

    /// <summary>
    /// O falante que quem traduz escolheu para esta linha, separado do <see cref="Speaker"/> extraído (que a re-extração reescreve): nulo segue o falante do jogo;
    /// vazio é "Narração" (a linha sai sem nome); qualquer outro valor é o nome que o build grava no lugar do extraído, pelo mesmo <see cref="SpeakerText.Resolve"/>
    /// (um nome de <c>characters.json</c> vai com a tradução do cadastro). Só vale nas linhas que o build grava (<see cref="NeedsRewrite"/>). As pontas são aparadas: só espaços viram vazio.
    /// A re-extração o preserva, como preserva a tradução. Fica fora do JSON quando nulo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChosenSpeaker { get; init => field = value?.Trim(); }

    /// <summary>O falante que vale para a linha: o <see cref="ChosenSpeaker"/> quando há (vazio é narração), senão o <see cref="Speaker"/> extraído.</summary>
    [JsonIgnore]
    public string? EffectiveSpeaker => ChosenSpeaker ?? Speaker;

    /// <summary>
    /// Original anterior, gravado pela re-extração quando o <see cref="Source"/> da linha mudou no jogo e ela já tinha tradução. É a marca de "revisar":
    /// a <see cref="Translation"/> antiga fica na linha, o <see cref="Status"/> volta para <see cref="TranslationStatus.Untranslated"/> (o build a ignora) e
    /// este campo guarda o original que ela traduzia. A re-extração o apaga quando a linha sai de <see cref="TranslationStatus.Untranslated"/>. Nulo quando não há o que revisar.
    /// </summary>
    public string? PreviousSource { get; init; }

    /// <summary>
    /// Dado opaco da engine, gravado na extração e devolvido sem alteração à reconstrução, para reinjetar sem reinterpretar os comandos do script
    /// (ex.: <c>{ "command": 812, "args": [0] }</c>). O Core nunca interpreta o conteúdo; só o preserva.
    /// </summary>
    /// <remarks>
    /// O valor atribuído é clonado, então não depende do <see cref="JsonDocument"/> de origem. É um <see cref="JsonElement"/> (qualquer JSON) porque o <c>text/*.json</c> fica legível em diff, o valor é imutável e
    /// independente do documento de origem, e cada engine escolhe o próprio formato sem criar tipos no Core. Uma string única ou base64
    /// esconderiam a estrutura no diff. A engine valida o que lê: locator ausente ou incompatível com o script vira <see cref="InvalidDataException"/> na reconstrução.
    /// </remarks>
    public JsonElement? Locator { get; init => field = value?.Clone(); }

    /// <summary>
    /// Verdadeiro quando há uma tradução utilizável: <see cref="Translation"/> não vazia e <see cref="Status"/> diferente de
    /// <see cref="TranslationStatus.Untranslated"/> e de <see cref="TranslationStatus.Keep"/> (a linha que fica como no original não tem tradução a gravar).
    /// </summary>
    [JsonIgnore]
    [MemberNotNullWhen(true, nameof(Translation))]
    public bool HasTranslation => Status is not (TranslationStatus.Untranslated or TranslationStatus.Keep) && !string.IsNullOrEmpty(Translation);

    /// <summary>
    /// Verdadeiro quando a reconstrução precisa gravar texto novo: há tradução (<see cref="HasTranslation"/>) e ela difere do
    /// <see cref="Source"/> (comparação ordinal). Quando falso, a engine copia o trecho original cru, byte a byte (regra 3 do <c>CLAUDE.md</c>).
    /// </summary>
    [JsonIgnore]
    public bool NeedsRewrite => HasTranslation && !string.Equals(Translation, Source, StringComparison.Ordinal);
}
