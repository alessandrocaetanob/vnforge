namespace VNForge.AI.Translation;

/// <summary>A direção de uma reescrita de fala pedida num toque: o que a pessoa quer diferente na tradução de hoje.</summary>
public enum RewriteDirection
{
    /// <summary>Mais curta, com o mesmo sentido, para caber na caixa do jogo.</summary>
    Shorter,

    /// <summary>Mais natural em português do Brasil falado.</summary>
    MoreNatural,

    /// <summary>Mais próxima do que o original diz.</summary>
    MoreFaithful,

    /// <summary>Em registro mais formal e respeitoso.</summary>
    MoreFormal,

    /// <summary>Como o personagem falaria, pelo "Como fala" dele no Glossário.</summary>
    AsCharacter,
}

/// <summary>Pedido de reescrita de uma fala: a direção e a tradução de hoje, com a caixa do jogo quando o perfil a conhece.</summary>
/// <param name="Direction">O que mudar na tradução.</param>
/// <param name="Current">A tradução de hoje, que é o ponto de partida das duas opções.</param>
/// <param name="MaxLines">Máximo de linhas por caixa do perfil de formatação; nulo quando não há limite ou perfil.</param>
/// <param name="MaxWidth">Largura máxima de cada linha, em colunas; nulo sem perfil.</param>
public sealed record RewriteRequest(RewriteDirection Direction, string Current, int? MaxLines = null, int? MaxWidth = null);

/// <summary>As opções de reescrita de uma linha.</summary>
/// <param name="Id">Id da linha reescrita.</param>
/// <param name="Options">Até duas versões diferentes entre si, com os mesmos marcadores de controle; pode vir vazia.</param>
/// <param name="Refused">O modelo recusou a linha mesmo após o fallback; <paramref name="Options"/> fica vazia.</param>
public sealed record RewriteResult(string Id, IReadOnlyList<string> Options, bool Refused = false);

/// <summary>Reescreve a tradução de uma fala em uma direção e devolve duas opções. Atrás de uma interface para os testes trocarem a IA por um provedor falso.</summary>
public interface IRewriteProvider
{
    /// <param name="batch">Exatamente uma linha, com o glossário e os personagens (o "Como fala" entra pelo tom) que valem para ela.</param>
    /// <param name="request">A direção e a tradução de hoje.</param>
    Task<RewriteResult> RewriteAsync(TranslationBatch batch, RewriteRequest request, CancellationToken ct);
}
