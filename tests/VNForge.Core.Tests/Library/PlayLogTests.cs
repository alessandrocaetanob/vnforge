using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R2")]
public sealed class PlayLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlaySession Session(int startHour, int minutes, bool interrupted = false) =>
        new($"s{startHour}-{minutes}", T0.AddHours(startHour), T0.AddHours(startHour).AddMinutes(minutes), TimeSpan.FromMinutes(minutes), interrupted);

    private static LibraryGame Game(string id, string title) =>
        new(id, Path.Combine(Path.GetTempPath(), id), "x.exe", LibraryEngine.Unknown, title, null, T0);

    [Fact]
    public void Arquivo_ilegivel_ou_de_outra_versao_e_recusado_e_continua_intacto()
    {
        var file = new PlayLogFile(_dir.Path);
        foreach (var text in new[] { "{ não é json", "null", """{"version":9,"games":{}}""" })
        {
            File.WriteAllText(file.Path, text);

            var error = Assert.Throws<LibraryException>(() => file.Load());

            Assert.Contains(file.Path, error.Message);
            Assert.Equal(text, File.ReadAllText(file.Path));
        }
    }
}
