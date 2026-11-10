using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Quais jogos entram no catálogo: todos os casados com o VNDB, os que já têm português ou os que a grade mostra agora.</summary>
public enum LibraryExportScope
{
    All,
    WithPortuguese,
    CurrentFilter,
}

/// <summary>Uma das três opções de "Quais jogos". <see cref="IsSelected"/> é o que o botão de rádio mostra.</summary>
public sealed partial class LibraryExportScopeViewModel(LibraryExportScope scope, string title) : ObservableObject
{
    public LibraryExportScope Scope { get; } = scope;

    public string Title { get; } = title;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// O diálogo "Exportar catálogo": escolhe quais jogos e o que entra, grava <c>index.html</c> na pasta escolhida (<see cref="LibraryCatalog"/>, o mesmo gerador da CLI) e abre a página.
/// O crédito ao VNDB e as imagens por link não têm escolha: entram sempre.
/// </summary>
public sealed partial class LibraryExportViewModel(LibraryViewModel library) : ObservableObject
{
    public const string FileName = "index.html";

    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Abre a página gravada no navegador; o app liga ao abridor do sistema.</summary>
    public Action<string> OpenFile { get; set; } = _ => { };

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ChooseFolderCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeSheet { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludePlayed { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeVote { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeTranslation { get; set; } = true;

    /// <summary>Spoiler e conteúdo adulto: desligado por padrão, porque o catálogo é feito para ser mostrado.</summary>
    [ObservableProperty]
    public partial bool IncludeSensitive { get; set; }

    public IReadOnlyList<LibraryExportScopeViewModel> Scopes { get; } =
    [
        new(LibraryExportScope.All, "Todos") { IsSelected = true },
        new(LibraryExportScope.WithPortuguese, "Só com português"),
        new(LibraryExportScope.CurrentFilter, "O filtro atual"),
    ];

    public LibraryExportScope Scope => Scopes.First(s => s.IsSelected).Scope;

    /// <summary>Abre o diálogo com as contagens de agora e a escolha de "Todos".</summary>
    [RelayCommand]
    public void Open()
    {
        ErrorMessage = null;
        if (Folder.Length == 0)
        {
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VNForge", "Catalogo");
        }

        var all = Pick(LibraryExportScope.All);
        var left = library.AllGames.Count - all.Count;
        Describe(LibraryExportScope.All).Detail = Games(all.Count) + (left == 0 ? string.Empty : left == 1 ? "; 1 fica de fora por não ter casamento no VNDB" : $"; {left} ficam de fora por não terem casamento no VNDB");
        Describe(LibraryExportScope.WithPortuguese).Detail = Games(Pick(LibraryExportScope.WithPortuguese).Count);
        var current = Describe(LibraryExportScope.CurrentFilter);
        current.IsAvailable = library.HasFilter;
        current.Detail = library.HasFilter ? $"{FilterText()}, {Games(Pick(LibraryExportScope.CurrentFilter).Count)}" : "Nenhum filtro ligado";
        if (!current.IsAvailable && current.IsSelected)
        {
            Select(LibraryExportScope.All);
        }

        IsOpen = true;
    }

    /// <summary>Marca a opção de "Quais jogos"; as outras desmarcam.</summary>
    public void Select(LibraryExportScope scope)
    {
        foreach (var item in Scopes)
        {
            item.IsSelected = item.Scope == scope;
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ChooseFolderAsync()
    {
        if (await library.FolderPicker.PickFolderAsync("Pasta do catálogo") is { Length: > 0 } picked)
        {
            Folder = picked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ExportAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var path = await Task.Run(WriteCatalog, CancellationToken.None);
            IsOpen = false;
            OpenFile(path);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ErrorMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAct() => !IsBusy;

    private string WriteCatalog()
    {
        if (string.IsNullOrWhiteSpace(Folder))
        {
            throw new LibraryException("Escolha a pasta onde o catálogo será gravado.");
        }

        var path = Path.GetFullPath(Path.Combine(Folder.Trim(), FileName));
        var snapshot = library.CatalogLibrary;
        if (LibraryCatalog.GameHolding(snapshot, path) is { } holder)
        {
            throw new LibraryException($"A pasta escolhida fica dentro da pasta do jogo \"{holder.Title}\". O VNForge não grava na pasta de um jogo fora do backup; escolha outra.");
        }

        if (File.Exists(path) && !IsCatalog(path))
        {
            throw new LibraryException($"A pasta já tem um {FileName} que não é um catálogo do VNForge. Escolha outra pasta para não trocá-lo.");
        }

        var chosen = Pick(Scope);
        var privacy = new LibraryPrivacy(ShowSpoilers: IncludeSensitive, ShowAdult: IncludeSensitive);
        var options = new LibraryCatalogOptions(privacy, IncludeSheet: IncludeSheet, IncludePlayed: IncludePlayed, IncludeVote: IncludeVote, IncludeTranslation: IncludeTranslation);
        var result = LibraryCatalog.Render(new GameLibrary(chosen, privacy), library.Played, library.Clock.GetLocalNow(), options, library.Sheets, library.TranslationOf);
        if (result.Games == 0)
        {
            throw new LibraryException("Nenhum jogo com jogo do VNDB confirmado entra com esta escolha, então não há o que exportar.");
        }

        LibraryCatalog.Write(path, result.Html);
        return path;
    }

    private static bool IsCatalog(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[2048];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read).Contains($"<title>{LibraryCatalog.PageTitle}</title>", StringComparison.Ordinal);
    }

    private LibraryExportScopeViewModel Describe(LibraryExportScope scope) => Scopes.First(s => s.Scope == scope);

    /// <summary>Os jogos casados com o VNDB que a opção escolhe; sem casamento o catálogo não tem o que mostrar.</summary>
    private List<LibraryGame> Pick(LibraryExportScope scope) =>
    [
        .. (scope == LibraryExportScope.CurrentFilter ? library.ShownGames : library.AllGames)
            .Where(g => g.Info is not null && (scope != LibraryExportScope.WithPortuguese || !LibraryListing.Matches(g, LibraryFilter.NoPortuguese, library.ReadyPercentOf(g)))),
    ];

    private string FilterText()
    {
        var parts = new List<string>();
        if (library.SelectedFilter != LibraryFilter.All)
        {
            parts.Add(library.Filters.First(f => f.IsSelected).Label);
        }

        if (!string.IsNullOrWhiteSpace(library.SearchText))
        {
            parts.Add($"“{library.SearchText.Trim()}”");
        }

        return string.Join(" e ", parts);
    }

    private static string Games(int count) => count == 1 ? "1 jogo" : $"{count.ToString("N0", Portuguese)} jogos";
}
