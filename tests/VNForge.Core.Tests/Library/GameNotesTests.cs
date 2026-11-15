using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R2")]
public sealed class GameNotesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(-3));

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Append_de_texto_vazio_e_recusado_sem_mexer_na_nota()
    {
        var notes = new GameNotes();
        notes.Set("g1", "fica", Now);

        var error = Assert.Throws<LibraryException>(() => notes.Append("g1", "  ", Now));

        Assert.Contains("vazio", error.Message);
        Assert.Equal("fica", notes.Of("g1")!.Text);
    }
}
