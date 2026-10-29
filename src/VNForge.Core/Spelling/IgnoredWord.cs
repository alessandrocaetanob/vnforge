using System.Text.Json.Serialization;

namespace VNForge.Core.Spelling;

/// <summary>
/// Uma palavra que o revisor mandou o corretor ignorar no projeto inteiro (#511): nome próprio ou termo do jogo que o dicionário pt-BR não conhece.
/// Fica em <c>ignored-words.json</c>, uma entrada por palavra; a comparação ignora maiúsculas.
/// </summary>
/// <param name="Word">A palavra como o revisor a viu marcada.</param>
public sealed record IgnoredWord([property: JsonRequired] string Word);
