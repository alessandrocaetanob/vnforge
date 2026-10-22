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
/// <see cref="Engines.ScriptFile.UnitId"/> (chave do script mais a ordem da unidade no arquivo), nunca com dados que
/// mudem entre execuções. A re-extração (E3.5) casa as traduções salvas pelo <c>Id</c> e confere se o <see cref="Source"/> continua o mesmo.
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

    /// <summary>Observações livres do tradutor ou do revisor. Vazio quando não há.</summary>
    public string Notes { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>Avisos do QA que o revisor marcou como "ok" (ids de regra). Nunca nulo.</summary>
    public IReadOnlyList<string> Flags { get; init => field = value ?? []; } = [];

    /// <summary>
    /// Dado opaco da engine, gravado na extração e devolvido sem alteração à reconstrução, para reinjetar sem reparsear o script
    /// (ex.: <c>{ "command": 812, "args": [0] }</c>). O Core nunca interpreta o conteúdo; só o preserva.
    /// </summary>
    /// <remarks>
    /// O valor atribuído é clonado, então não depende do <see cref="JsonDocument"/> de origem. É um <see cref="JsonElement"/> (qualquer JSON) porque o <c>text/*.json</c> fica legível em diff, o valor é imutável e
    /// independente do documento de origem, e cada engine escolhe o próprio formato sem criar tipos no Core. Uma string única ou base64
    /// esconderiam a estrutura no diff. A engine valida o que lê: locator ausente ou incompatível com o script vira <see cref="InvalidDataException"/> na reconstrução.
    /// </remarks>
    public JsonElement? Locator { get; init => field = value?.Clone(); }

    /// <summary>Verdadeiro quando há uma tradução utilizável: <see cref="Translation"/> não vazia e <see cref="Status"/> diferente de <see cref="TranslationStatus.Untranslated"/>.</summary>
    [JsonIgnore]
    public bool HasTranslation => Status != TranslationStatus.Untranslated && !string.IsNullOrEmpty(Translation);

    /// <summary>
    /// Verdadeiro quando a reconstrução precisa gravar texto novo: há tradução (<see cref="HasTranslation"/>) e ela difere do
    /// <see cref="Source"/> (comparação ordinal). Quando falso, a engine copia o trecho original cru, byte a byte (regra 3 do <c>CLAUDE.md</c>).
    /// </summary>
    [JsonIgnore]
    public bool NeedsRewrite => HasTranslation && !string.Equals(Translation, Source, StringComparison.Ordinal);
}
