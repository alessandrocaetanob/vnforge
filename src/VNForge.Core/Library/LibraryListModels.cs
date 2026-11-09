using System.Globalization;
using System.Text.Json.Serialization;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>Os rótulos da lista do VNDB. Os de id abaixo de 10 são os mesmos para todo mundo (api.vndb.org/kana, seção "GET /ulist_labels"); os outros são da conta.</summary>
public static class LibraryListLabels
{
    public const int Playing = 1;
    public const int Finished = 2;
    public const int Stalled = 3;
    public const int Dropped = 4;
    public const int Wishlist = 5;
    public const int Blacklist = 6;

    /// <summary>O VNDB põe e tira sozinho com o voto; nunca entra nos rótulos que o app guarda nem nos que ele envia.</summary>
    public const int Voted = 7;

    public const int MaxId = 1600;

    private sealed record Factory(int Id, string Vndb, string Portuguese, string[] Names);

    private static readonly Factory[] Predefined =
    [
        new(Playing, "Playing", "Jogando", ["jogando", "playing"]),
        new(Finished, "Finished", "Terminado", ["terminado", "finished", "zerado"]),
        new(Stalled, "Stalled", "Parado", ["parado", "stalled"]),
        new(Dropped, "Dropped", "Abandonado", ["abandonado", "dropped"]),
        new(Wishlist, "Wishlist", "Desejado", ["desejado", "wishlist"]),
        new(Blacklist, "Blacklist", "Lista negra", ["lista-negra", "blacklist"]),
    ];

    /// <summary>O rótulo com o nome do VNDB para os de fábrica, ou <c>#id</c> para um da conta que ainda não se conhece pelo nome.</summary>
    public static VndbListLabel Named(int id) =>
        new(id, Array.Find(Predefined, p => p.Id == id)?.Vndb ?? "#" + id.ToString(CultureInfo.InvariantCulture));

    /// <summary>O nome para mostrar: em português para os de fábrica, o nome da conta para os outros.</summary>
    public static string Display(VndbListLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Array.Find(Predefined, p => p.Id == label.Id)?.Portuguese ?? label.Label;
    }

    /// <summary>Entende o nome em português ou em inglês de um rótulo de fábrica, ou o id numérico de um rótulo da conta.</summary>
    public static bool TryParse(string? text, out int id)
    {
        id = 0;
        var value = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (Array.Find(Predefined, p => p.Names.Contains(value)) is { } found)
        {
            id = found.Id;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id is >= 8 and <= MaxId;
    }

    public static string Names => string.Join(", ", Predefined.Select(p => p.Names[0]));
}

/// <summary>Os limites de voto e a conversão entre a nota que a pessoa digita (1 a 10, uma casa) e o voto do VNDB (10 a 100).</summary>
public static class LibraryListRules
{
    public const int MinVote = 10;
    public const int MaxVote = 100;

    /// <summary>De <c>8.5</c> ou <c>8,5</c> para 85.</summary>
    /// <exception cref="ArgumentException">Não é um número de 1 a 10 com no máximo uma casa decimal.</exception>
    public static int ParseVote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var score)
            || score * 10 != decimal.Truncate(score * 10)
            || score * 10 is < MinVote or > MaxVote)
        {
            throw new ArgumentException($"A nota '{text}' não vale: use de 1 a 10, com no máximo uma casa decimal (8.5, por exemplo).", nameof(text));
        }

        return (int)(score * 10);
    }

    /// <summary>De 85 para <c>8,5</c> (ou <c>8</c> para 80).</summary>
    public static string FormatVote(int vote, CultureInfo? culture = null) =>
        (vote / 10m).ToString(vote % 10 == 0 ? "0" : "0.0", culture ?? CultureInfo.GetCultureInfo("pt-BR"));
}

/// <summary>
/// O que a lista do VNDB tem de um jogo: rótulos, voto (10 a 100), datas de início e fim e notas. Os rótulos ficam ordenados por id e sem o 7 ("Voted");
/// notas vazias valem nulo. Duas listas com os mesmos rótulos (por id), voto, datas e notas são <see cref="SameAs"/>, ainda que os nomes dos rótulos difiram.
/// </summary>
public sealed record LibraryListFields
{
    public static LibraryListFields Empty { get; } = new();

    public IReadOnlyList<VndbListLabel> Labels { get; init; } = [];

    /// <summary>De 10 a 100.</summary>
    public int? Vote { get; init; }

    public DateOnly? Started { get; init; }

    public DateOnly? Finished { get; init; }

    public string? Notes { get; init; }

    /// <summary>A entrada da lista do VNDB no formato do app.</summary>
    public static LibraryListFields From(VndbListEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Normalize(new LibraryListFields
        {
            Labels = entry.Labels,
            Vote = entry.Vote,
            Started = entry.Started,
            Finished = entry.Finished,
            Notes = entry.Notes,
        });
    }

    public static LibraryListFields Normalize(LibraryListFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields with
        {
            Labels = [.. fields.Labels.Where(l => l.Id != LibraryListLabels.Voted).GroupBy(l => l.Id).Select(g => g.First()).OrderBy(l => l.Id)],
            Notes = string.IsNullOrWhiteSpace(fields.Notes) ? null : fields.Notes,
        };
    }

    public bool HasLabel(int id) => Labels.Any(l => l.Id == id);

    public bool SameAs(LibraryListFields? other) =>
        other is not null
        && Vote == other.Vote
        && Started == other.Started
        && Finished == other.Finished
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal)
        && Labels.Select(l => l.Id).Order().SequenceEqual(other.Labels.Select(l => l.Id).Order());
}

/// <summary>
/// O estado da lista do VNDB de um jogo da biblioteca. <paramref name="Fields"/> é o que o app mostra e edita; <paramref name="ModifiedAt"/> é quando a pessoa o mudou
/// por último aqui (ou a última modificação no VNDB, quando o estado veio de lá) e decide o desempate da sincronização. <see cref="Base"/> é o que os dois lados tinham
/// de acordo na última sincronização: sem ele não dá para dizer quem mudou o quê. <paramref name="VisualNovelId"/> amarra o estado ao jogo do VNDB confirmado;
/// se a confirmação mudar, o estado velho deixa de valer (<see cref="LibraryGame.CurrentList"/>).
/// </summary>
public sealed record LibraryListState(string VisualNovelId, LibraryListFields Fields, DateTimeOffset ModifiedAt)
{
    /// <summary>O acordo da última sincronização; nulo se este jogo nunca foi sincronizado.</summary>
    public LibraryListFields? Base { get; init; }

    public DateTimeOffset? SyncedAt { get; init; }

    /// <summary>Há mudança daqui que o VNDB ainda não tem (ou a primeira sincronização ainda não aconteceu).</summary>
    [JsonIgnore]
    public bool Pending => Base is null || !Fields.SameAs(Base);

    /// <summary>
    /// Aplica a mudança da pessoa sobre o estado atual (ou sobre uma lista vazia, se o jogo ainda não tem estado) e marca <paramref name="now"/> como a hora da edição.
    /// Rótulos entram e saem um a um; nada é trocado em bloco.
    /// </summary>
    /// <exception cref="ArgumentException">Voto fora de 10 a 100 ou rótulo inválido (de 1 a 1600, e o 7 sai sozinho com o voto).</exception>
    public static LibraryListState Edit(LibraryListState? current, string visualNovelId, VndbListUpdate update, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visualNovelId);
        ArgumentNullException.ThrowIfNull(update);
        Validate(update);

        var fields = current?.Fields ?? LibraryListFields.Empty;
        var labels = fields.Labels
            .Where(l => !update.LabelsUnset.Contains(l.Id))
            .Concat(update.LabelsSet.Where(id => !fields.HasLabel(id)).Select(LibraryListLabels.Named))
            .ToList();
        var edited = LibraryListFields.Normalize(fields with
        {
            Labels = labels,
            Vote = Apply(fields.Vote, update.Vote),
            Started = Apply(fields.Started, update.Started),
            Finished = Apply(fields.Finished, update.Finished),
            Notes = update.Notes.Kind switch
            {
                VndbChangeKind.Set => update.Notes.Value,
                VndbChangeKind.Clear => null,
                _ => fields.Notes,
            },
        });
        return (current ?? new LibraryListState(visualNovelId, edited, now)) with { Fields = edited, ModifiedAt = now };
    }

    private static T? Apply<T>(T? current, VndbChange<T> change)
        where T : struct => change.Kind switch
        {
            VndbChangeKind.Set => change.Value,
            VndbChangeKind.Clear => null,
            _ => current,
        };

    private static void Validate(VndbListUpdate update)
    {
        if (update.Vote is { Kind: VndbChangeKind.Set, Value: < LibraryListRules.MinVote or > LibraryListRules.MaxVote })
        {
            throw new ArgumentException($"O voto vai de {LibraryListRules.MinVote} a {LibraryListRules.MaxVote}.", nameof(update));
        }

        foreach (var id in update.LabelsSet.Concat(update.LabelsUnset))
        {
            if (id is < 1 or > LibraryListLabels.MaxId || id == LibraryListLabels.Voted)
            {
                throw new ArgumentException($"Rótulo inválido: o VNDB aceita de 1 a {LibraryListLabels.MaxId}, e o {LibraryListLabels.Voted} (Voted) sai sozinho com o voto.", nameof(update));
            }
        }
    }
}
