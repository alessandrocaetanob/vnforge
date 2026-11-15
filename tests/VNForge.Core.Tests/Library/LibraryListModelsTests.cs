using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R5")]
public sealed class LibraryListModelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Estado_vai_e_volta_pelo_library_json_sem_token_nem_segredo()
    {
        var state = LibraryListState.Edit(null, "v4", new VndbListUpdate { Vote = 85, LabelsSet = [2], Notes = "Ótimo", Finished = new DateOnly(2026, 3, 4) }, Now);
        var game = new LibraryGame("g1", "C:\\Jogos\\A", null, LibraryEngine.Unknown, "A", null, Now) { VndbId = "v4", List = state with { Base = LibraryListFields.Empty, SyncedAt = Now } };

        var json = LibraryFile.Serialize(new GameLibrary([game]));
        var back = LibraryFile.Parse(json).Games[0];

        Assert.Equal(85, back.CurrentList!.Fields.Vote);
        Assert.Equal([2], back.CurrentList.Fields.Labels.Select(l => l.Id));
        Assert.Equal(new DateOnly(2026, 3, 4), back.CurrentList.Fields.Finished);
        Assert.Equal("Ótimo", back.CurrentList.Fields.Notes);
        Assert.True(back.CurrentList.Pending);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }
}
