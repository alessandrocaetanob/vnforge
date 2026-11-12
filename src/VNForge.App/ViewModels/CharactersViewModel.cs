using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Factsheet;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Como a grade de personagens se ordena.</summary>
public enum CharacterSort
{
    Name,
    MostGames,
    Role,
}

public sealed record CharacterSortOption(CharacterSort Sort, string Label);

/// <summary>
/// A área Personagens (d11, #558): todos os personagens dos jogos da Biblioteca, com busca e filtro por papel e por traço, e a página de cada um com o "Aparece em". Quem decide o que
/// entra e o que fica escondido é o Core (<see cref="LibraryCharacters"/>: jogos que a pessoa tem, edição casada com a pasta, spoiler por jogo e por traço); aqui só se liga isso à tela.
/// </summary>
public sealed partial class CharactersViewModel : ObservableObject
{
    private const int FirstTraits = 6;
    private const string GamesPlural = "jogos";
    private static readonly CharacterSortOption[] Sorts = [new(CharacterSort.Name, "Nome"), new(CharacterSort.MostGames, "Mais jogos"), new(CharacterSort.Role, "Papel")];
    private static readonly string[] RoleCodes = ["main", "primary", "side", "appears"];

    private readonly HashSet<string> selectedRoles = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedTraits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> traitNames = new(StringComparer.Ordinal);
    private Snapshot? snapshot;
    private (string Id, bool RevealSpoilers)? pending;
    private string? openId;
    private int generation;
    private Extras? extras;
    private readonly HashSet<string> warnedProjects = new(StringComparer.Ordinal);
    private int allTraitCount;
    private bool applying;

    /// <summary>A pasta de configuração do usuário, onde ficam <c>sheets/</c>, o <c>library.json</c> e as edições casadas. Sem ela a área não abre.</summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>O catálogo do VNDB que "Buscar do VNDB" usa; o app passa o cliente com cache, os testes o catálogo falso.</summary>
    public Func<IVndbCatalog>? CatalogFactory { get; set; }

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Os jogos que a pessoa revelou nesta sessão no modo Desfocar; o shell liga o registro da Biblioteca, o mesmo da grade e da Ficha.</summary>
    public AdultReveal Reveals
    {
        get => reveals;
        set
        {
            reveals.Changed -= OnRevealsChanged;
            reveals = value;
            reveals.Changed += OnRevealsChanged;
        }
    }

    private AdultReveal reveals = new();

    private void OnRevealsChanged(object? sender, EventArgs e)
    {
        if (snapshot is not null)
        {
            Rebuild();
        }
    }

    /// <summary>Quem mostra o aviso de uma ação (o aviso do app): o texto e a gravidade.</summary>
    public Action<string, ToastKind>? Notify { get; set; }

    /// <summary>"Abrir jogo" da página do personagem: o shell leva ao jogo, ou começa o projeto dele, como a gaveta da Biblioteca.</summary>
    public event EventHandler<LibraryGame>? GameOpenRequested;

    /// <summary>A leitura em andamento (ao entrar na área); os testes a aguardam.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Buscando os personagens no VNDB: o botão espera.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyPropertyChangedFor(nameof(SyncLabel))]
    public partial bool IsSyncing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncLabel))]
    public partial string SyncText { get; private set; } = string.Empty;

    public string SyncLabel => IsSyncing ? SyncText : "Buscar do VNDB";

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Um aviso sobre o que não abriu: o personagem que só aparece em jogos com spoiler escondido, ou que saiu da Biblioteca.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TraitSearch { get; set; } = string.Empty;

    /// <summary>O interruptor "Mostrar spoiler" da barra de filtros. Começa no que a Biblioteca guarda (escondido por padrão) e vale só enquanto a área está aberta.</summary>
    [ObservableProperty]
    public partial bool ShowSpoilers { get; set; }

    [ObservableProperty]
    public partial bool ShowAllTraits { get; set; }

    public IReadOnlyList<CharacterSortOption> SortOptions => Sorts;

    [ObservableProperty]
    public partial CharacterSortOption SelectedSort { get; set; } = Sorts[0];

    public IReadOnlyList<CharacterListItem> Items { get; private set; } = [];

    public IReadOnlyList<CharacterFacet> RoleFacets { get; private set; } = [];

    /// <summary>Os traços que a barra mostra: os marcados primeiro e depois os mais comuns; todos com "Ver os N traços" ou com a busca de traço.</summary>
    public IReadOnlyList<CharacterFacet> TraitFacets { get; private set; } = [];

    public IReadOnlyList<CharacterFacet> ActiveTraits { get; private set; } = [];

    public CharacterPage? Page { get; private set; }

    public bool IsPage => Page is not null;

    public bool IsList => Page is null;

    public bool HasItems => Items.Count > 0;

    public bool HasActiveTraits => ActiveTraits.Count > 0;

    public bool HasFilter => selectedRoles.Count > 0 || selectedTraits.Count > 0 || !string.IsNullOrWhiteSpace(SearchText);

    public bool CanShowAllTraits => allTraitCount > TraitFacets.Count || ShowAllTraits;

    public string ShowAllTraitsText => ShowAllTraits ? "Ver menos" : $"Ver os {allTraitCount.ToString(CultureInfo.InvariantCulture)} traços";

    public string Subtitle { get; private set; } = string.Empty;

    /// <summary>"58 personagens de 12 jogos", a barra de status da área.</summary>
    public string StatusText { get; private set; } = "Personagens";

    /// <summary>O que dizer no lugar da grade: a pasta que falta, nenhum personagem ainda ou nenhum resultado para o filtro.</summary>
    public string EmptyMessage { get; private set; } = string.Empty;

    public bool ShowEmpty => !HasItems && EmptyMessage.Length > 0;

    public bool CanClearFilter => HasFilter && snapshot is not null;

    /// <summary>O nome do personagem aberto, para o caminho da barra de título; nulo na lista.</summary>
    public string? OpenName => Page?.Name;

    /// <summary>
    /// Abre a página do personagem <paramref name="id"/> na próxima leitura (a que o shell pede ao entrar na área). <paramref name="revealSpoilers"/> vem da ficha do jogo que o mostrou
    /// com spoiler à vista: sem isso a página recusaria um personagem que a ficha acabou de mostrar.
    /// </summary>
    public void Open(string id, bool revealSpoilers = false) => pending = (id, revealSpoilers);

    /// <summary>A leitura do que a página do personagem busca nos projetos (cartão "Na tradução" e andamento); os testes a aguardam.</summary>
    public Task ExtrasLoading { get; private set; } = Task.CompletedTask;

    public Task LoadAsync() => Loading = LoadCoreAsync();

    /// <param name="keepState">Relê os arquivos sem mexer no que a pessoa fez na tela (página aberta, busca, filtros e spoiler), como depois de buscar no VNDB.</param>
    private async Task LoadCoreAsync(bool keepState = false)
    {
        var wanted = ++generation;
        if (ConfigDirectory is not { } directory)
        {
            snapshot = null;
            ErrorMessage = "A pasta de configuração do VNForge não está disponível, então os personagens não podem ser lidos.";
            Rebuild();
            return;
        }

        IsLoading = true;
        try
        {
            var loaded = await Task.Run(() => Read(directory)).ConfigureAwait(true);
            if (wanted != generation)
            {
                return;
            }

            snapshot = loaded;
            ErrorMessage = null;
            // Cada propriedade refaz a grade ao mudar; aqui o estado volta ao de entrada e a grade se refaz uma vez só, no fim.
            applying = true;
            if (!keepState)
            {
                Notice = null;
                openId = null;
                extras = null;
                selectedRoles.Clear();
                selectedTraits.Clear();
                traitNames.Clear();
                SearchText = string.Empty;
                TraitSearch = string.Empty;
                ShowAllTraits = false;
                ShowSpoilers = loaded.Library.Privacy.ShowSpoilers;
            }

            if (pending is { } request)
            {
                pending = null;
                ShowSpoilers |= request.RevealSpoilers;
                openId = request.Id;
            }

            applying = false;
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException or JsonException)
        {
            if (wanted != generation)
            {
                return;
            }

            snapshot = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            applying = false;
            if (wanted == generation)
            {
                IsLoading = false;
            }
        }

        Rebuild();
        if (openId is { } open)
        {
            await LoadExtrasAsync(open).ConfigureAwait(true);
        }
    }

    private static Snapshot Read(string directory)
    {
        var store = new SheetStore(directory);
        return new Snapshot(new LibraryFile(directory).Load(), store.LoadAllCharacters(), store.LoadTerms(), new GameEditionsFile(directory).Load());
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnTraitSearchChanged(string value) => Rebuild();

    partial void OnShowSpoilersChanged(bool value) => Rebuild();

    partial void OnShowAllTraitsChanged(bool value) => Rebuild();

    partial void OnSelectedSortChanged(CharacterSortOption value) => Rebuild();

    private void Rebuild()
    {
        if (applying)
        {
            return;
        }

        if (snapshot is not { } data)
        {
            Items = [];
            RoleFacets = [];
            TraitFacets = [];
            ActiveTraits = [];
            Page = null;
            Subtitle = string.Empty;
            StatusText = "Personagens";
            EmptyMessage = ErrorMessage ?? string.Empty;
            allTraitCount = 0;
            OnPropertyChanged(string.Empty);
            return;
        }

        try
        {
            RebuildFrom(data);
        }
        catch (Exception ex) when (ex is VNForgeException or InvalidOperationException)
        {
            // Um arquivo da Biblioteca ou das fichas que o Core recusa, ou que não bate com o outro, não derruba a tela (este método roda dentro de handlers de propriedade): ela mostra o erro, como as outras falhas de leitura.
            snapshot = null;
            ErrorMessage = ex is InvalidOperationException
                ? $"A Biblioteca e as fichas de personagens não batem entre si ({ex.Message}). Busque as fichas de novo."
                : ex.Message;
            Rebuild();
        }
    }

    private void RebuildFrom(Snapshot data)
    {
        var privacy = new LibraryPrivacy(ShowSpoilers, data.Library.Privacy.Adult);
        var text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
        var all = LibraryCharacters.List(data.Library, data.Characters, data.Terms, data.Editions, privacy, isRevealed: Reveals.IsRevealed);
        // Um traço que a privacidade voltou a esconder (o adulto depois de borrar o jogo de novo) sai do filtro: ele some das facetas, e a lista ficaria filtrada por algo invisível.
        var seen = all.Characters.SelectMany(c => c.Traits).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        selectedTraits.RemoveWhere(id => !seen.Contains(id));
        LibraryCharacterList basis, shown;
        try
        {
            basis = text is null ? all : LibraryCharacters.List(data.Library, data.Characters, data.Terms, data.Editions, privacy, new LibraryCharacterFilter(text), Reveals.IsRevealed);
            shown = selectedTraits.Count == 0 ? basis : LibraryCharacters.List(data.Library, data.Characters, data.Terms, data.Editions, privacy, new LibraryCharacterFilter(text, [.. selectedTraits]), Reveals.IsRevealed);
        }
        catch (LibraryException)
        {
            // Uma busca só de pontuação não casa com nada: a grade fica vazia em vez de explodir.
            basis = shown = new LibraryCharacterList([], 0);
        }

        var cards = shown.Characters.Where(c => selectedRoles.Count == 0 || c.AppearsIn.Any(a => selectedRoles.Contains(a.RoleCode))).ToList();
        cards = Sort(cards);
        Items = [.. cards.Select(c => ItemOf(c, privacy.Adult))];
        BuildFacets(basis.Characters);
        var games = all.Characters.SelectMany(c => c.AppearsIn).Select(a => a.GameId).Distinct(StringComparer.Ordinal).Count();
        StatusText = $"{GameSheetViewModel.Count(all.Characters.Count, "personagem", "personagens")} de {GameSheetViewModel.Count(games, "jogo", GamesPlural)}";
        Subtitle = $"{Items.Count.ToString(CultureInfo.InvariantCulture)} de {all.Characters.Count.ToString(CultureInfo.InvariantCulture)} · só os dos jogos da sua Biblioteca"
            + (shown.Hidden > 0 ? $" · {GameSheetViewModel.Count(shown.Hidden, "escondido", "escondidos")} por spoiler" : string.Empty);
        EmptyMessage = EmptyMessageFor(all);
        Page = openId is { } id ? BuildPage(data, privacy, id) : null;
        OnPropertyChanged(string.Empty);
    }

    private string EmptyMessageFor(LibraryCharacterList all)
    {
        if (all.Characters.Count > 0)
        {
            return Items.Count == 0 ? "Nenhum personagem com esse filtro." : string.Empty;
        }

        return all.Hidden > 0
            ? "Os personagens dos seus jogos têm spoiler e estão escondidos. Ligue \"Mostrar spoiler\" para vê-los."
            : "Nenhum personagem ainda. Eles chegam com a ficha dos jogos da Biblioteca: busque a ficha de um jogo ligado ao VNDB, ou rode vnf library characters sync.";
    }

    private List<LibraryCharacterCard> Sort(List<LibraryCharacterCard> cards) => SelectedSort.Sort switch
    {
        CharacterSort.MostGames => [.. cards.OrderByDescending(c => c.AppearsIn.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)],
        CharacterSort.Role => [.. cards.OrderBy(c => SheetLabels.RoleRank(ShownAppearance(c).RoleCode)).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)],
        _ => cards,
    };

    private static SheetPill RolePill(string roleCode, string gender) => new(SheetLabels.RoleBadge(roleCode, gender), roleCode == "main" ? SheetTone.Approved : SheetTone.Pending);

    /// <summary>Com filtro de papel, a aparição que casou com ele (o cartão "side" que passou pelo filtro "side" não mostra o papel "main" de outro jogo); sem filtro, a mais importante.</summary>
    private LibraryCharacterAppearance ShownAppearance(LibraryCharacterCard card) =>
        card.AppearsIn.FirstOrDefault(a => selectedRoles.Contains(a.RoleCode)) ?? card.AppearsIn[0];

    private CharacterListItem ItemOf(LibraryCharacterCard card, AdultMode mode) =>
        new(card.Id, card.Name, card.Original, card.Image, RolePill(ShownAppearance(card).RoleCode, card.Gender), GameSheetViewModel.Count(card.AppearsIn.Count, "jogo", GamesPlural), PortraitOf(card, mode));

    /// <summary>
    /// O retrato tem uma chave só: o jogo do papel mais importante do personagem, igual na lista (com ou sem filtro de papel) e na página. O olho revela esse jogo e o retrato está
    /// revelado se e só se ele está; revelar outro jogo do personagem não o mostra. O olho só aparece no retrato adulto que o Desfocar borra.
    /// </summary>
    private CharacterPortraitReveal PortraitOf(LibraryCharacterCard card, AdultMode mode)
    {
        var key = card.AppearsIn[0].VisualNovelId;
        var revealed = Reveals.IsRevealed(key);
        return new CharacterPortraitReveal(key, revealed, card is { ImageHidden: true, Image: not null } && mode == AdultMode.Blur && !revealed);
    }

    private void BuildFacets(IReadOnlyList<LibraryCharacterCard> basis)
    {
        RoleFacets =
        [
            .. RoleCodes.Select(code => NewFacet(
                code,
                SheetLabels.RoleBadge(code, null),
                basis.Count(c => c.AppearsIn.Any(a => a.RoleCode == code)),
                selectedRoles))
        ];

        var counts = basis.SelectMany(c => c.Traits.DistinctBy(t => t.Id)).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => (Name: g.First().Name, Count: g.Count()), StringComparer.Ordinal);
        foreach (var pair in counts)
        {
            traitNames[pair.Key] = pair.Value.Name;
        }

        // Um traço marcado fica na lista mesmo quando a busca de texto o deixou sem personagens.
        foreach (var key in selectedTraits.Where(k => !counts.ContainsKey(k)))
        {
            counts[key] = (traitNames.GetValueOrDefault(key, key), 0);
        }

        var search = TitleSimilarity.Normalize(TraitSearch ?? string.Empty);
        var facets = counts
            .Where(p => search.Length == 0 || selectedTraits.Contains(p.Key) || TitleSimilarity.Normalize(p.Value.Name).Contains(search, StringComparison.Ordinal))
            .OrderByDescending(p => selectedTraits.Contains(p.Key))
            .ThenByDescending(p => p.Value.Count)
            .ThenBy(p => p.Value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => NewFacet(p.Key, p.Value.Name, p.Value.Count, selectedTraits))
            .ToList();
        allTraitCount = facets.Count;
        TraitFacets = ShowAllTraits || search.Length > 0 ? facets : [.. facets.Take(FirstTraits)];
        ActiveTraits = [.. facets.Where(f => f.IsSelected)];
    }

    private CharacterFacet NewFacet(string key, string label, int count, HashSet<string> selection)
    {
        var facet = new CharacterFacet(key, label, count) { IsSelected = selection.Contains(key) };
        facet.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterFacet.IsSelected))
            {
                if (facet.IsSelected)
                {
                    selection.Add(key);
                }
                else
                {
                    selection.Remove(key);
                }

                Rebuild();
            }
        };
        return facet;
    }

    // --- Página do personagem ---

    private CharacterPage? BuildPage(Snapshot data, LibraryPrivacy privacy, string id)
    {
        var lookup = LibraryCharacters.Find(data.Library, data.Characters, data.Terms, data.Editions, privacy, id, Reveals.IsRevealed);
        if (lookup.Card is not { } card)
        {
            openId = null;
            Notice = lookup.HiddenBySpoiler
                ? "Esse personagem só aparece em jogos com spoiler escondido. Ligue \"Mostrar spoiler\" para vê-lo."
                : "Esse personagem não está em nenhum jogo da sua Biblioteca.";
            return null;
        }

        Notice = null;
        var raw = data.Characters.First(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        var owned = data.Library.Games.Where(g => g.VndbId is not null).Select(g => g.VndbId!).ToHashSet(StringComparer.Ordinal);
        var outside = raw.Appearances.Select(a => a.VisualNovelId).Distinct(StringComparer.Ordinal).Count(v => !owned.Contains(v));
        var description = card.Description;
        var status = description is { Translated: true } ? description.Status : TranslationStatus.Untranslated;
        var known = extras is { } read && string.Equals(read.Id, id, StringComparison.OrdinalIgnoreCase) ? read : null;
        var games = card.AppearsIn.Select(a => GameRow(data, privacy, card, a, known?.Counts.GetValueOrDefault(a.GameId), Reveals.IsRevealed(a.VisualNovelId))).ToList();
        var seen = privacy.Adult == AdultMode.Blur && Reveals.IsRevealed(card.AppearsIn[0].VisualNovelId) ? privacy with { Adult = AdultMode.Show } : privacy;
        var hiddenTraits = raw.Traits.Where(t => SheetViewer.IsTraitHidden(t, seen)).ToList();
        return new CharacterPage(
            card.Id,
            card.Name,
            card.Original is { Length: > 0 } original ? $"{original} · {card.Name}" : null,
            card.Aliases.Count > 0 ? $"Também chamado de {string.Join(", ", card.Aliases)}" : null,
            card.Image,
            new SheetPill($"Descrição {SheetLabels.Status(status)}", SheetPill.ToneOf(status)),
            description?.Text ?? "Este personagem não tem descrição no VNDB.",
            Tags(card, hiddenTraits),
            Groups(card, hiddenTraits),
            games,
            $"{GameSheetViewModel.Count(games.Count, "jogo", GamesPlural)} da sua Biblioteca",
            PageNote(card, outside),
            PortraitOf(card, privacy.Adult),
            known?.Translation);
    }

    private static string? PageNote(LibraryCharacterCard card, int outside)
    {
        var notes = new List<string>();
        if (card.HiddenAppearances > 0)
        {
            notes.Add($"{GameSheetViewModel.Count(card.HiddenAppearances, "jogo da sua Biblioteca fica escondido", "jogos da sua Biblioteca ficam escondidos")} por spoiler.");
        }

        if (outside > 0)
        {
            notes.Add($"{card.Name} também está em {GameSheetViewModel.Count(outside, "jogo", GamesPlural)} do VNDB que não {(outside == 1 ? "está" : "estão")} na sua Biblioteca. {(outside == 1 ? "Ele não entra na lista" : "Eles não entram na lista")}.");
        }

        return notes.Count > 0 ? string.Join(' ', notes) : null;
    }

    /// <summary>O que a página do personagem lê dos projetos: o andamento de cada jogo dele e o cartão "Na tradução". Lê-se uma vez ao abrir a página, fora da thread da tela, e a página reaproveita enquanto aberta.</summary>
    private sealed record Extras(string Id, IReadOnlyDictionary<string, StatusCounts?> Counts, CharacterTranslation? Translation, IReadOnlyDictionary<string, string> Unreadable);

    private async Task LoadExtrasAsync(string id)
    {
        var data = snapshot;
        if (data is null || (extras is { } known && string.Equals(known.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var read = await Task.Run(() => ReadExtras(data, id)).ConfigureAwait(true);
        if (ReferenceEquals(snapshot, data) && string.Equals(openId, id, StringComparison.OrdinalIgnoreCase))
        {
            extras = read;
            Rebuild();
            var fresh = read.Unreadable.Where(u => warnedProjects.Add(u.Key)).Select(u => u.Value).ToList();
            if (fresh.Count > 0)
            {
                Notify?.Invoke($"Não foi possível ler {(fresh.Count == 1 ? "o projeto de" : "os projetos de")} {string.Join(", ", fresh)}. A página mostra só o que veio dos outros.", ToastKind.Warning);
            }
        }
    }

    /// <summary>O cartão "Na tradução" só existe quando um dos jogos do personagem tem projeto e o Glossário dele tem o personagem (casado pelo id do VNDB).</summary>
    private static Extras ReadExtras(Snapshot data, string id)
    {
        var visualNovels = data.Characters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Appearances.Select(a => a.VisualNovelId).ToHashSet(StringComparer.Ordinal) ?? [];
        var games = data.Library.Games.Where(g => g.ProjectPath is not null && g.VndbId is not null && visualNovels.Contains(g.VndbId)).ToList();
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = games.ToDictionary(g => g.Id, g => ReadCounts(g, unreadable), StringComparer.Ordinal);
        CharacterTranslation? translation = null;
        foreach (var game in games)
        {
            try
            {
                var project = ProjectFolder.Open(game.ProjectPath!);
                if (project.LoadCharacters().FirstOrDefault(c => string.Equals(c.VndbId, id, StringComparison.OrdinalIgnoreCase)) is not { } entry)
                {
                    continue;
                }

                var lines = project.LoadAllText().Values.SelectMany(units => units).Count(u => string.Equals(u.Speaker, entry.Source, StringComparison.Ordinal));
                translation = new CharacterTranslation(
                    entry.Gender switch { CharacterGender.Male => "Masculino", CharacterGender.Female => "Feminino", CharacterGender.Other => "Outro", _ => "Sem gênero" },
                    entry.Source,
                    $"{lines.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} em {game.Title}");
                break;
            }
            catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                unreadable[game.Id] = game.Title;
            }
        }

        return new Extras(id, counts, translation, unreadable);
    }

    /// <summary><see cref="LibraryCatalog.ReadTranslation"/> devolve nulo quando a pasta some ou está presa; para um jogo com projeto isso é um projeto ilegível, que vira aviso em vez de sumir calado.</summary>
    private static StatusCounts? ReadCounts(LibraryGame game, Dictionary<string, string> unreadable)
    {
        try
        {
            if (LibraryCatalog.ReadTranslation(game) is { } counts)
            {
                return counts;
            }
        }
        catch (JsonException)
        {
            unreadable[game.Id] = game.Title;
            return null;
        }

        unreadable[game.Id] = game.Title;
        return null;
    }

    private static CharacterGameRow GameRow(Snapshot data, LibraryPrivacy privacy, LibraryCharacterCard card, LibraryCharacterAppearance appearance, StatusCounts? translation, bool revealed)
    {
        var game = data.Library.Games.First(g => g.Id == appearance.GameId);
        var described = LibraryCards.Describe(game, privacy, translation);
        var origin = appearance.Editions.Count > 0 ? "Edição casada com a sua pasta" : "Papel mais importante";
        return new CharacterGameRow(
            game.Id,
            game.Title,
            described.Cover == LibraryCoverKind.Image ? described.Image : null,
            described.Subtitle,
            (described.ProgressPercent ?? 0) / 100.0,
            RolePill(appearance.RoleCode, card.Gender),
            $"{origin} · spoiler nível {appearance.Spoiler.ToString(CultureInfo.InvariantCulture)}",
            revealed);
    }

    private static List<SheetChip> Tags(LibraryCharacterCard card, List<VndbTrait> hidden)
    {
        var chips = new List<SheetChip>();
        if (card.Gender != SheetLabels.Gender(null))
        {
            chips.Add(new SheetChip(char.ToUpper(card.Gender[0], CultureInfo.GetCultureInfo("pt-BR")) + card.Gender[1..]));
        }

        if (card.Age is { } age)
        {
            chips.Add(new SheetChip(GameSheetViewModel.Count(age, "ano", "anos")));
        }

        if (card.RealGender is { } real)
        {
            chips.Add(new SheetChip($"Gênero real: {real}", IsWarning: true));
        }

        chips.AddRange(card.Traits.Take(FirstTraits).Select(t => new SheetChip(t.Name, IsWarning: t.Spoiler > 0)));
        if (card.HiddenTraits > 0)
        {
            var reason = hidden.All(t => t.Spoiler > 0) ? ("traço com spoiler", "traços com spoiler") : ("traço escondido", "traços escondidos");
            chips.Add(new SheetChip(GameSheetViewModel.Count(card.HiddenTraits, reason.Item1, reason.Item2), IsHidden: true));
        }

        return chips;
    }

    /// <summary>Os grupos do VNDB em português, na ordem do quadro; Cabelo, Olhos, Corpo e Roupas viram um só, Aparência.</summary>
    private static readonly string[] GroupOrder = ["Aparência", "Personalidade", "Ocupação", "Objetos", "Atividades", "Situações", "Conteúdo adulto"];

    private static string GroupName(string vndbGroup) => vndbGroup switch
    {
        "Hair" or "Eyes" or "Body" or "Clothes" => "Aparência",
        "Personality" => "Personalidade",
        "Role" => "Ocupação",
        "Items" => "Objetos",
        "Engages in" => "Atividades",
        "Subject of" => "Situações",
        "Sexual Content" or "Sexual content" => "Conteúdo adulto",
        _ => vndbGroup,
    };

    private static List<CharacterTraitGroup> Groups(LibraryCharacterCard card, List<VndbTrait> hidden)
    {
        var groups = card.Traits.GroupBy(t => GroupName(t.Group))
            .OrderBy(g => Array.IndexOf(GroupOrder, g.Key) is var rank and >= 0 ? rank : GroupOrder.Length)
            .Select(g => new CharacterTraitGroup(g.Key, [.. g.Select(t => new SheetChip(t.Name, IsWarning: t.Spoiler > 0))]))
            .ToList();
        if (hidden.Count > 0)
        {
            groups.Add(new CharacterTraitGroup("Spoiler", [.. hidden.Select(t => new SheetChip(t.Spoiler > 0 ? $"Nível {t.Spoiler.ToString(CultureInfo.InvariantCulture)}" : "Adulto", IsHidden: true))]));
        }

        return groups;
    }

    // --- Ações ---

    /// <summary>Traz do VNDB os personagens e as edições dos jogos da Biblioteca (o mesmo <see cref="LibraryCharacterSync"/> da CLI); o que já veio não volta ao VNDB.</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (ConfigDirectory is not { } directory)
        {
            return;
        }

        if (CatalogFactory is null)
        {
            Notify?.Invoke("Não há conexão com o VNDB configurada neste app.", ToastKind.Warning);
            return;
        }

        IsSyncing = true;
        SyncText = "Buscando no VNDB…";
        try
        {
            var library = new LibraryFile(directory).Load();
            var catalog = CatalogFactory();
            var progress = new Progress<LibraryCharacterSyncProgress>(p => SyncText = $"Buscando no VNDB: {p.Done.ToString(CultureInfo.InvariantCulture)} de {p.Total.ToString(CultureInfo.InvariantCulture)}…");
            var result = await Task.Run(() => LibraryCharacterSync.SyncAsync(catalog, library, new SheetStore(directory), new GameEditionsFile(directory), new LibraryCharacterSyncOptions(Time: Clock, Progress: progress))).ConfigureAwait(true);
            if (result.Failures.Count > 0)
            {
                Notify?.Invoke($"{GameSheetViewModel.Count(result.Failures.Count, "jogo ficou", "jogos ficaram")} sem personagens ({result.Failures[0].Message}). Tente de novo.", ToastKind.Warning);
            }
            else
            {
                Notify?.Invoke($"{GameSheetViewModel.Count(result.Characters, "personagem trazido", "personagens trazidos")} de {GameSheetViewModel.Count(result.Games, "jogo", GamesPlural)}. {VndbCredit.ShortText}", ToastKind.Success);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notify?.Invoke($"Não foi possível buscar os personagens: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsSyncing = false;
            SyncText = string.Empty;
        }

        var reload = LoadCoreAsync(keepState: true);
        Loading = reload;
        await reload.ConfigureAwait(true);
    }

    private bool CanSync() => !IsSyncing && ConfigDirectory is not null;

    [RelayCommand]
    private void OpenCharacter(CharacterListItem? item)
    {
        if (item is not null)
        {
            openId = item.Id;
            Rebuild();
            if (openId is not null)
            {
                ExtrasLoading = LoadExtrasAsync(item.Id);
            }
        }
    }

    /// <summary>O olho do retrato borrado: libera o jogo (capa e retratos dele) em toda a Biblioteca até a pessoa sair dela.</summary>
    [RelayCommand]
    private void RevealPortrait(string? visualNovelId)
    {
        if (!string.IsNullOrEmpty(visualNovelId))
        {
            Reveals.Reveal(visualNovelId);
        }
    }

    /// <summary>Volta da página do personagem para a lista, com a busca e os filtros que estavam.</summary>
    [RelayCommand]
    private void Back()
    {
        openId = null;
        Notice = null;
        Rebuild();
    }

    [RelayCommand]
    private void OpenGame(CharacterGameRow? row)
    {
        if (row is not null && snapshot?.Library.Games.FirstOrDefault(g => g.Id == row.GameId) is { } game)
        {
            GameOpenRequested?.Invoke(this, game);
        }
    }

    [RelayCommand]
    private void RemoveTrait(CharacterFacet? facet)
    {
        if (facet is not null && selectedTraits.Remove(facet.Key))
        {
            Rebuild();
        }
    }

    [RelayCommand]
    private void ToggleAllTraits() => ShowAllTraits = !ShowAllTraits;

    [RelayCommand]
    private void ClearFilter()
    {
        applying = true;
        selectedRoles.Clear();
        selectedTraits.Clear();
        TraitSearch = string.Empty;
        SearchText = string.Empty;
        applying = false;
        Rebuild();
    }

    private sealed record Snapshot(GameLibrary Library, IReadOnlyList<CharacterSheet> Characters, SheetTerms Terms, GameEditions Editions);
}
