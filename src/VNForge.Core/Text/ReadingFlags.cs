namespace VNForge.Core.Text;

/// <summary>
/// A marca "soa estranho" que quem lê a cena como jogador põe numa fala (<c>F</c> no leitor do Editor). Vive em <see cref="TextUnit.Flags"/>, como as marcas da
/// Conferência, então fica no <c>text/&lt;chave&gt;.json</c> e sobrevive a fechar o projeto e à re-extração. Não é id de regra do QA e não silencia aviso nenhum.
/// </summary>
public static class ReadingFlags
{
    /// <summary>O valor gravado em <see cref="TextUnit.Flags"/>.</summary>
    public const string SoundsOdd = "soa-estranho";

    /// <summary>A linha foi marcada como "soa estranho".</summary>
    public static bool IsOdd(this TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Flags.Contains(SoundsOdd, StringComparer.Ordinal);
    }

    /// <summary>A linha com a marca posta ou tirada; devolve a mesma instância quando já estava assim.</summary>
    public static TextUnit WithOdd(this TextUnit unit, bool odd)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.IsOdd() == odd)
        {
            return unit;
        }

        IReadOnlyList<string> flags = odd ? [.. unit.Flags, SoundsOdd] : [.. unit.Flags.Where(f => !string.Equals(f, SoundsOdd, StringComparison.Ordinal))];
        return unit with { Flags = flags };
    }
}
