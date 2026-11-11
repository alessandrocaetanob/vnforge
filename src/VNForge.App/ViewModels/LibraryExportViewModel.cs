using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Library;
using VNForge.Core.Projects;

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
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ChooseFolderCommand), nameof(CancelCommand))]
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

        RefreshDetails();
        IsOpen = true;
    }

    partial void OnIncludeSensitiveChanged(bool value) => RefreshDetails();

    private void RefreshDetails()
    {
        var all = Pick(LibraryExportScope.All);
        var unmatched = library.AllGames.Count(g => g.Info is null);
        var adult = AdultMode == AdultMode.Blur ? library.AllGames.Count(g => g.Info is { Adult: true }) : 0;
        var reasons = new List<string>();
        if (unmatched > 0)
        {
            reasons.Add($"{unmatched} sem casamento no VNDB");
        }

        if (adult > 0)
        {
            reasons.Add(adult == 1 ? "1 adulto" : $"{adult} adultos");
        }

        var left = unmatched + adult;
        Describe(LibraryExportScope.All).Detail = Games(all.Count) + (left == 0 ? string.Empty : $"; {string.Join(" e ", reasons)} {(left == 1 ? "fica" : "ficam")} de fora");
        Describe(LibraryExportScope.WithPortuguese).Detail = Games(Pick(LibraryExportScope.WithPortuguese).Count);
        var current = Describe(LibraryExportScope.CurrentFilter);
        current.IsAvailable = library.HasFilter;
        current.Detail = library.HasFilter ? $"{FilterText()}, {Games(Pick(LibraryExportScope.CurrentFilter).Count)}" : "Nenhum filtro ligado";
        if (!current.IsAvailable && current.IsSelected)
        {
            Select(LibraryExportScope.All);
        }
    }

    /// <summary>Marca a opção de "Quais jogos"; as outras desmarcam.</summary>
    public void Select(LibraryExportScope scope)
    {
        foreach (var item in Scopes)
        {
            item.IsSelected = item.Scope == scope;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
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
            var plan = Plan();
            var path = await Task.Run(() => WriteCatalog(plan), CancellationToken.None);
            if (IsOpen)
            {
                // Esc durante a gravação fecha o diálogo: a pessoa desistiu, o navegador não abre sozinho.
                IsOpen = false;
                OpenFile(path);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ErrorMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAct() => !IsBusy;

    /// <summary>Tudo o que a gravação lê, tirado na thread da tela: a gravação roda em outra e a tela muda sob ela (uma varredura troca as listas).</summary>
    private sealed record ExportPlan(
        string Folder,
        GameLibrary Library,
        List<LibraryGame> Chosen,
        LibraryPrivacy Privacy,
        LibraryCatalogOptions Options,
        Dictionary<string, StatusCounts?> Translations,
        PlayLog Played,
        DateTimeOffset Now,
        ILibrarySheetSource? Sheets);

    private AdultMode AdultMode => library.AdultMode;

    private ExportPlan Plan()
    {
        var chosen = Pick(Scope);
        // O jogo adulto só entra no modo Mostrar (Pick); a opção do diálogo decide só as tags e os traços sensíveis dos jogos que entram.
        var privacy = new LibraryPrivacy(ShowSpoilers: IncludeSensitive, Adult: IncludeSensitive ? AdultMode.Show : AdultMode);
        return new ExportPlan(
            Folder,
            new GameLibrary([.. library.AllGames]),
            chosen,
            privacy,
            new LibraryCatalogOptions(privacy, IncludeSheet: IncludeSheet, IncludePlayed: IncludePlayed, IncludeVote: IncludeVote, IncludeTranslation: IncludeTranslation, IncludeAdultGames: AdultMode == AdultMode.Show),
            chosen.ToDictionary(g => g.Id, library.TranslationOf, StringComparer.Ordinal),
            library.Played,
            library.Clock.GetLocalNow(),
            library.Sheets);
    }

    private static string WriteCatalog(ExportPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Folder))
        {
            throw new LibraryException("Escolha a pasta onde o catálogo será gravado.");
        }

        var path = Path.GetFullPath(Path.Combine(plan.Folder.Trim(), FileName));
        if (LibraryCatalog.GameHolding(plan.Library, path) is { } holder)
        {
            throw new LibraryException($"A pasta escolhida fica dentro da pasta do jogo \"{holder.Title}\". O VNForge não grava na pasta de um jogo fora do backup; escolha outra.");
        }

        if (File.Exists(path) && !IsCatalog(path))
        {
            throw new LibraryException($"A pasta já tem um {FileName} que não é um catálogo do VNForge. Escolha outra pasta para não trocá-lo.");
        }

        var result = LibraryCatalog.Render(new GameLibrary(plan.Chosen, plan.Privacy), plan.Played, plan.Now, plan.Options, plan.Sheets, g => plan.Translations.GetValueOrDefault(g.Id));
        if (result.Games == 0)
        {
            throw new LibraryException("Nenhum jogo entra com esta escolha: faltam jogos casados com o VNDB, ou eles são de conteúdo adulto e o modo do conteúdo adulto não é Mostrar.");
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
            .Where(g => g.Info is not null && (AdultMode == AdultMode.Show || !g.Info.Adult) && (scope != LibraryExportScope.WithPortuguese || !LibraryListing.Matches(g, LibraryFilter.NoPortuguese, library.ReadyPercentOf(g)))),
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
