namespace VNForge.Core.Qa;

/// <summary>
/// Uma regra de QA. Vê o projeto inteiro (<see cref="QaContext"/>), então serve tanto à regra por linha quanto à que compara linhas
/// entre si; para a regra por linha, derive de <see cref="UnitQaRule"/>. A regra devolve toda ocorrência: quem filtra por regra e
/// gravidade e quem silencia as linhas marcadas como "ok" é o <see cref="QaEngine"/>, nunca a regra.
/// </summary>
public interface IQaRule
{
    /// <summary>Id estável em minúsculas e hífens (<c>marcador-diferente</c>). Aparece na saída, no filtro <c>--rules</c> e em <see cref="Text.TextUnit.Flags"/>, então não muda depois de publicado.</summary>
    string Id { get; }

    /// <summary>Uma frase em português do que a regra confere, para listar as regras.</summary>
    string Description { get; }

    /// <summary>Confere o projeto. Não altera o contexto nem grava nada.</summary>
    IEnumerable<QaIssue> Check(QaContext context);
}
