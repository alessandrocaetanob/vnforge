namespace VNForge.Core.Vndb;

/// <summary>
/// Fronteira entre o app e o catálogo do VNDB, no mesmo molde da IA: o Core não conhece o serviço. O <c>VndbClient</c> (<c>VNForge.Vndb</c>) a implementa pela API HTTP
/// e o <c>FakeVndbCatalog</c> dos testes, em memória. Só lê; a lista da pessoa fica em <see cref="IVndbUserList"/>.
/// </summary>
public interface IVndbCatalog
{
    /// <summary>Os jogos que casam com <paramref name="title"/> (título, título alternativo, apelido ou título de edição), do que mais casa para o que menos.</summary>
    Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default);

    /// <summary>O jogo com esse id (<c>v17</c>), ou nulo se o VNDB não o tem.</summary>
    Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default);

    /// <summary>Os personagens do jogo com esse id.</summary>
    Task<IReadOnlyList<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default);

    /// <summary>Os personagens dos jogos que casam com <paramref name="title"/>, num pedido só.</summary>
    Task<IReadOnlyList<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default);

    /// <summary>As edições do jogo com esse id.</summary>
    Task<IReadOnlyList<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default);
}

/// <summary>
/// A lista de jogos de uma conta do VNDB. O token vem sempre de fora (cofre do usuário ou variável de ambiente) e nunca é gravado em arquivo do projeto.
/// </summary>
public interface IVndbUserList
{
    /// <summary>Confere o token e devolve a conta e as permissões dele.</summary>
    Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// A lista de <paramref name="userId"/> (<c>u2</c>). Sem <paramref name="token"/> vêm só as entradas públicas; com o token da própria conta
    /// e a permissão <c>listread</c>, vêm também as privadas.
    /// </summary>
    Task<IReadOnlyList<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default);

    /// <summary>Grava as mudanças na lista da conta do <paramref name="token"/> (permissão <c>listwrite</c>).</summary>
    Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default);
}
