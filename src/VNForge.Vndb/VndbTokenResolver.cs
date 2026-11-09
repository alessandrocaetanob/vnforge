namespace VNForge.Vndb;

/// <summary>
/// De onde vem o token da conta do VNDB. Nunca de arquivo do projeto (regra 4 do CLAUDE.md): a variável de ambiente <c>VNDB_TOKEN</c> tem prioridade e,
/// quando o cofre do usuário (DPAPI) existir para o token (d4), ele entra depois dela, como na chave da IA.
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
}
