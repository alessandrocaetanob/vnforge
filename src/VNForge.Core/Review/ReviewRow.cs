namespace VNForge.Core.Review;

/// <summary>
/// Uma linha do arquivo de revisão, em texto puro. Só <see cref="Translation"/>, <see cref="Status"/> e <see cref="Notes"/> voltam
/// para o projeto na importação; <see cref="Kind"/>, <see cref="Speaker"/> e <see cref="Source"/> existem para dar contexto a quem revisa.
/// </summary>
/// <param name="Line">Linha do arquivo lido (a primeira de dados é a 2); 0 numa linha que ainda não veio de um arquivo.</param>
/// <param name="Id"><see cref="Text.TextUnit.Id"/>, a chave que casa a linha com o projeto.</param>
/// <param name="Kind">Papel da linha, com o nome gravado no JSON (<c>dialogue</c>, <c>narration</c>...).</param>
/// <param name="Speaker">Falante; vazio quando não há.</param>
/// <param name="Source">Texto original.</param>
/// <param name="Translation">Tradução; vazio quando não há.</param>
/// <param name="Status">Etapa do fluxo, com o nome gravado no JSON (<c>machine</c>, <c>approved</c>...).</param>
/// <param name="Notes">Observações; vazio quando não há.</param>
public sealed record ReviewRow(int Line, string Id, string Kind, string Speaker, string Source, string Translation, string Status, string Notes);
