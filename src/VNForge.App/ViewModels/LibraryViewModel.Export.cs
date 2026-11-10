using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>A parte da Biblioteca que o diálogo "Exportar catálogo" lê: os jogos, o filtro de agora e o andamento da tradução de cada um.</summary>
public sealed partial class LibraryViewModel
{
    private LibraryExportViewModel? export;

    /// <summary>O diálogo "Exportar catálogo" (#555).</summary>
    public LibraryExportViewModel Export => export ??= new LibraryExportViewModel(this);

    internal IReadOnlyList<LibraryGame> AllGames => games;

    internal IEnumerable<LibraryGame> ShownGames => Cards.Select(c => c.Game);

    internal GameLibrary CatalogLibrary => library;

    internal IFolderPicker FolderPicker => picker;

    internal ILibrarySheetSource? Sheets => sheets;

    internal TimeProvider Clock => clock;

    internal int? ReadyPercentOf(LibraryGame game) => PercentOf(game);

    internal StatusCounts? TranslationOf(LibraryGame game) => translations.GetValueOrDefault(game.Id);
}
