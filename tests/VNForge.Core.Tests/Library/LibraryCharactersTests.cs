using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: o registro de personagens da Biblioteca, com o catálogo falso do VNDB.</summary>
[Trait("Risco", "R9")]
public sealed class LibraryCharactersTests : IDisposable
{
    private static readonly LibraryPrivacy Hidden = new();
    private static readonly LibraryPrivacy All = new(ShowSpoilers: true, Adult: AdultMode.Show);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<(GameLibrary Library, SheetStore Store)> PrepareAsync() => (OwnedLibrary(), await FilledAsync(_dir.Path));

    private static LibraryCharacterList List(GameLibrary library, SheetStore store, LibraryPrivacy privacy, GameEditions? editions = null, LibraryCharacterFilter? filter = null) =>
        LibraryCharacters.List(library, store.LoadAllCharacters(), store.LoadTerms(), editions ?? new GameEditions(), privacy, filter);

    [Fact]
    public async Task Busca_ou_traco_sem_letras_nem_numeros_e_recusado()
    {
        const string text = "!!!";
        var (library, store) = await PrepareAsync();

        var error = Assert.Throws<LibraryException>(() => List(library, store, All, filter: new LibraryCharacterFilter(Text: text)));

        Assert.Contains("não tem letras nem números", error.Message, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => List(library, store, All, filter: new LibraryCharacterFilter(Traits: ["--"])));
    }
}
