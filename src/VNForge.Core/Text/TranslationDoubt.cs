using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>Uma dúvida que a IA registrou ao traduzir a linha: o tipo e uma frase que diz o que a deixou em dúvida.</summary>
/// <param name="Kind">Sobre o que é a dúvida.</param>
/// <param name="Note">A frase da IA, em português.</param>
public sealed record TranslationDoubt([property: JsonRequired] DoubtKind Kind, [property: JsonRequired] string Note)
{
    /// <summary>O nome do tipo para mostrar ao usuário.</summary>
    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        DoubtKind.Pun => "Trocadilho",
        DoubtKind.HiddenSubject => "Sujeito oculto",
        DoubtKind.UncertainGender => "Gênero incerto",
        DoubtKind.CulturalReference => "Referência cultural",
        DoubtKind.Tone => "Tom",
        _ => Kind.ToString(),
    };
}
