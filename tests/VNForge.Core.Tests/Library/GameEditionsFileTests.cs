using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: o arquivo <c>library-editions.json</c> que guarda a edição casada com a pasta de cada jogo.</summary>
[Trait("Risco", "R2")]
public sealed class GameEditionsFileTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("{ nao e json")]
    public void Arquivo_ilegivel_e_recusado_citando_o_arquivo_e_nunca_sobrescrito(string json)
    {
        var file = new GameEditionsFile(_dir.Path);
        File.WriteAllText(file.Path, json);

        var load = Assert.Throws<LibraryException>(() => file.Load());
        Assert.Throws<LibraryException>(() => file.Update(e => e.Set("g9", Moon, [], Now)));

        Assert.Contains("library-editions.json", load.Message, StringComparison.Ordinal);
        Assert.Equal(json, File.ReadAllText(file.Path));
    }
}
