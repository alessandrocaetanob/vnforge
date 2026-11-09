namespace VNForge.Vndb;

/// <summary>De onde veio o token do VNDB.</summary>
public enum VndbTokenSource
{
    None,
    Environment,
    Vault,
}

/// <summary>O token resolvido e sua origem. Nunca vai para log, <c>--json</c> nem arquivo.</summary>
public sealed record ResolvedVndbToken(string? Token, VndbTokenSource Source)
{
    /// <summary>Um aviso do cofre (por exemplo, o arquivo do Windows ignorado em outro sistema) para mostrar à pessoa; nunca contém o token.</summary>
    public string? Notice { get; init; }

    public override string ToString() => $"ResolvedVndbToken {{ Source = {Source} }}";
}

/// <summary>
/// De onde vem o token da conta do VNDB. Nunca de arquivo do projeto (regra 4 do CLAUDE.md): a variável de ambiente <c>VNDB_TOKEN</c> tem prioridade e,
/// depois dela, entra o cofre do usuário (DPAPI), como na chave da IA.
/// </summary>
public static class VndbTokenResolver
{
    public const string EnvironmentVariable = "VNDB_TOKEN";

    /// <summary>O token da variável de ambiente, sem espaços nas pontas, ou nulo se ela não existe ou está vazia.</summary>
    public static string? Resolve(Func<string, string?>? readEnvironment = null)
    {
        var value = (readEnvironment ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>O token da variável de ambiente ou, na falta dela, o do cofre, com a origem.</summary>
    public static ResolvedVndbToken Resolve(IVndbTokenStore store, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Resolve(readEnvironment) is { } fromEnvironment)
        {
            return new ResolvedVndbToken(fromEnvironment, VndbTokenSource.Environment);
        }

        var fromVault = store.Load();
        return string.IsNullOrWhiteSpace(fromVault)
            ? new ResolvedVndbToken(null, VndbTokenSource.None) { Notice = store.Notice }
            : new ResolvedVndbToken(fromVault.Trim(), VndbTokenSource.Vault);
    }
}
