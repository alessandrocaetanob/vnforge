using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Sobre o que a IA ficou em dúvida ao traduzir uma linha. Os valores gravados no JSON são estes nomes e entram também no schema do pedido de tradução
/// (<c>ClaudeTranslationProvider.Schema</c>): mudar um nome invalida os arquivos <c>text/*.json</c> e a resposta do modelo.
/// </summary>
[JsonConverter(typeof(StrictEnumConverter<DoubtKind>))]
public enum DoubtKind
{
    /// <summary>Jogo de palavras que não passa para o português.</summary>
    [JsonStringEnumMemberName("pun")]
    Pun,

    /// <summary>O japonês omite o sujeito e a IA escolheu quem fala ou de quem se fala.</summary>
    [JsonStringEnumMemberName("hidden-subject")]
    HiddenSubject,

    /// <summary>O gênero de quem fala ou de quem é citado não está claro.</summary>
    [JsonStringEnumMemberName("uncertain-gender")]
    UncertainGender,

    /// <summary>Referência a algo da cultura japonesa que o leitor brasileiro pode não conhecer.</summary>
    [JsonStringEnumMemberName("cultural-reference")]
    CulturalReference,

    /// <summary>O tom da fala (ironia, formalidade, carinho) é ambíguo.</summary>
    [JsonStringEnumMemberName("tone")]
    Tone,
}
