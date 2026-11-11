using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>As regras de entrada da API do VNDB, compartilhadas pelo <see cref="VndbClient"/> e pelo <see cref="FakeVndbCatalog"/>: o falso recusa o que o real recusaria.</summary>
internal static class VndbRules
{
    public const int MinVote = 10;
    public const int MaxVote = 100;
    public const int MaxLabelId = 1600;

    /// <summary>O rótulo "Voted": o VNDB o põe e tira sozinho conforme o voto.</summary>
    public const int VotedLabelId = 7;

    public static string RequireTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return title.Trim();
    }

    public static void RequireId(string id, char prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length < 2 || id[0] != prefix || id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new ArgumentException($"Id do VNDB inválido: '{id}'. O esperado é como '{prefix}17'.", nameof(id));
        }
    }

    public static void RequireValid(VndbListUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Vote.Kind == VndbChangeKind.Set)
        {
            // S3236 é falso positivo: o ParamName "update" faz parte do contrato da exceção.
            ArgumentOutOfRangeException.ThrowIfLessThan(update.Vote.Value, MinVote, nameof(update));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(update.Vote.Value, MaxVote, nameof(update));
        }

        var invalid = update.LabelsSet.Concat(update.LabelsUnset).Cast<int?>().FirstOrDefault(id => id is < 1 or > MaxLabelId || id == VotedLabelId);
        if (invalid is { } label)
        {
            throw new ArgumentOutOfRangeException(nameof(update), label, $"Rótulo inválido: o VNDB aceita de 1 a {MaxLabelId}, e o {VotedLabelId} (Voted) sai sozinho com o voto.");
        }
    }

    /// <summary>O número do id (<c>v10</c> é 10), para ordenar <c>v2</c> antes de <c>v10</c>.</summary>
    public static long NumberOf(string id) =>
        id.Length > 1 && long.TryParse(id.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;
}
