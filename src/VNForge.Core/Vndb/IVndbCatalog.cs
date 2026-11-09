namespace VNForge.Core.Vndb;

/// <summary>
/// Uma lista que o VNDB entrega em páginas. O cliente segue as páginas até um teto; se o teto chegou e o VNDB ainda tinha mais, <paramref name="Truncated"/> é verdadeiro
/// e <paramref name="Items"/> é só o começo. Quem precisa da lista inteira (sincronizar a lista da conta, por exemplo) deve tratar o corte, não ignorá-lo.
/// </summary>
public sealed record VndbResults<T>(IReadOnlyList<T> Items, bool Truncated);

/// <summary>
/// Fronteira entre o app e o catálogo do VNDB, no mesmo molde da IA: o Core não conhece o serviço. O <c>VndbClient</c> (<c>VNForge.Vndb</c>) a implementa pela API HTTP
/// e o <c>FakeVndbCatalog</c> dos testes, em memória. Só lê; a lista da pessoa fica em <see cref="IVndbUserList"/>.
/// </summary>
public interface IVndbCatalog
{
    /// <summary>Os jogos que casam com <paramref name="title"/> (título, título alternativo, apelido ou título de edição), do que mais casa para o que menos. No máximo 100.</summary>
    Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default);

    /// <summary>O jogo com esse id (<c>v17</c>), ou nulo se o VNDB não o tem.</summary>
    Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default);

    /// <summary>Os personagens do jogo com esse id, em páginas de 100 até 1000 personagens; passando disso, <see cref="VndbResults{T}.Truncated"/> avisa.</summary>
    Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default);

    /// <summary>Os personagens dos jogos que casam com <paramref name="title"/>, em páginas de 100 até 500 personagens; passando disso, <see cref="VndbResults{T}.Truncated"/> avisa.</summary>
    Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default);

    /// <summary>As edições do jogo com esse id, em páginas de 100 até 300 edições; passando disso, <see cref="VndbResults{T}.Truncated"/> avisa.</summary>
    Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default);
}

/// <summary>
/// A lista de jogos de uma conta do VNDB. O token vem sempre de fora (cofre do usuário ou variável de ambiente) e nunca é gravado em arquivo do projeto.
/// </summary>
public interface IVndbUserList
{
    /// <summary>Confere o token e devolve a conta e as permissões dele.</summary>
    Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// A lista de <paramref name="userId"/> (<c>u2</c>), em páginas de 100 até 20 mil entradas (<see cref="VndbResults{T}.Truncated"/> avisa se passou). Sem <paramref name="token"/>
    /// vêm só as entradas públicas; com o token da própria conta e a permissão <c>listread</c>, vêm também as privadas.
    /// </summary>
    Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default);

    /// <summary>
    /// Grava as mudanças na lista da conta do <paramref name="token"/> (permissão <c>listwrite</c>). Voto de 10 a 100; rótulos de 1 a 1600, sem o 7 (&quot;Voted&quot;),
    /// que o VNDB põe e tira sozinho com o voto.
    /// </summary>
    Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default);
}
