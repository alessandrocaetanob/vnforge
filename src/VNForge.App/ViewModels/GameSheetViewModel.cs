using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Em que ponto está a aba Ficha do jogo aberto.</summary>
public enum GameSheetState
{
    /// <summary>O jogo não tem o vínculo com o VNDB: não há de onde tirar a ficha.</summary>
    NoLink,
    Loading,

    /// <summary>O jogo está ligado ao VNDB, mas a ficha ainda não foi buscada.</summary>
    NotFetched,
    Ready,
    Failed,
}

/// <summary>
/// A aba Ficha do jogo (d9, #554): sinopse, gêneros e tags, personagens, dados do VNDB, o estado do texto em português e o controle de spoiler. Tudo o que decide o que aparece vem do Core
/// (<see cref="SheetPage"/>, <see cref="SheetViewer"/>, <see cref="SheetBlocks"/>); aqui só se liga isso à tela. A revisão da ficha (<see cref="Review"/>) é uma segunda página da mesma aba.
/// </summary>
public sealed partial class GameSheetViewModel : ObservableObject
{
    private const int FirstTiles = 6;

    private static readonly SpoilerOption[] Options = [new(0, "Escondido"), new(1, "Leve"), new(2, "Tudo")];

    private string? visualNovelId;
    private SheetPageData? data;
    private GameSheetCard? card;
    private IReadOnlyList<SheetBlock> blocks = [];
    private IReadOnlyList<CharacterTile> tiles = [];
    private bool levelPending = true;
    private int generation;

    public GameSheetViewModel()
    {
        Review = new SheetReviewViewModel(this);
        reveals.Changed += OnRevealsChanged;
    }

    /// <summary>A pasta de configuração do usuário, onde ficam <c>sheets/</c> e o <c>library.json</c>. Sem ela a ficha não abre.</summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>O catálogo do VNDB que "Buscar a ficha" usa; o app passa o cliente com cache, os testes o catálogo falso.</summary>
    public Func<IVndbCatalog>? CatalogFactory { get; set; }

    /// <summary>O tradutor da ficha; o app passa o Claude com a chave do usuário, os testes o tradutor falso. Pode lançar quando falta a chave.</summary>
    public Func<ISheetTranslator>? TranslatorFactory { get; set; }

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Quem mostra o aviso de uma ação (o aviso do app): o texto e a gravidade.</summary>
    public Action<string, ToastKind>? Notify { get; set; }

    /// <summary>A leitura da ficha em andamento (ao abrir o jogo, depois de buscar ou traduzir).</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    public SheetReviewViewModel Review { get; }

    public string GameTitle { get; private set; } = string.Empty;

    public string? VisualNovelId => visualNovelId;

    /// <summary>O cartão "Na sua lista do VNDB" do jogo, quando ele está na Biblioteca e confirmado no VNDB; nulo some o cartão.</summary>
    [ObservableProperty]
    public partial LibraryVndbListViewModel? ListCard { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(ShowEmpty), nameof(EmptyMessage), nameof(EmptyActionText), nameof(EmptyActionCommand))]
    public partial GameSheetState State { get; private set; } = GameSheetState.NoLink;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>Buscando no VNDB ou traduzindo: as ações esperam.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand), nameof(TranslateCommand))]
    [NotifyPropertyChangedFor(nameof(TranslateLabel), nameof(ShowBusy))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string BusyText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview))]
    public partial bool IsReviewing { get; private set; }

    public bool IsOverview => !IsReviewing;

    public bool ShowBusy => IsBusy;

    public bool IsReady => State == GameSheetState.Ready && card is not null;

    public bool ShowEmpty => !IsReady;

    public string EmptyMessage => State switch
    {
        GameSheetState.NoLink => "Este jogo ainda não está ligado ao VNDB, então não há ficha para mostrar. Ligue-o ao criar o projeto de tradução.",
        GameSheetState.Loading => "Lendo a ficha do jogo…",
        GameSheetState.NotFetched => "A ficha deste jogo ainda não foi buscada no VNDB. Ela traz a sinopse, as tags e os personagens, que a IA traduz para o português.",
        GameSheetState.Failed => ErrorMessage.Length > 0 ? ErrorMessage : "Não foi possível ler a ficha deste jogo.",
        _ => string.Empty,
    };

    public string? EmptyActionText => State switch
    {
        GameSheetState.NotFetched => "Buscar a ficha no VNDB",
        GameSheetState.Failed => "Tentar de novo",
        _ => null,
    };

    public IRelayCommand? EmptyActionCommand => State switch
    {
        GameSheetState.NotFetched => FetchCommand,
        GameSheetState.Failed => ReloadCommand,
        _ => null,
    };

    // --- Spoiler ---

    public IReadOnlyList<SpoilerOption> SpoilerOptions => Options;

    public SpoilerOption SelectedSpoiler
    {
        get => Options[SpoilerLevel];
        set
        {
            if (value is not null)
            {
                SpoilerLevel = value.Level;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSpoiler), nameof(SpoilerHint), nameof(SpoilerLevelText))]
    public partial int SpoilerLevel { get; set; }

    public string SpoilerHint => "Traços, tags e papéis com spoiler ficam escondidos. O nível vem do VNDB.";

    /// <summary>O que o botão da revisão diz sobre o nível: "Mostrando nível 2", ou "Spoiler escondido".</summary>
    public string SpoilerLevelText => SpoilerLevel == 0 ? "Spoiler escondido" : $"Mostrando nível {SpoilerLevel.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Os jogos que a pessoa revelou na Biblioteca nesta sessão; o shell liga o registro da Biblioteca.</summary>
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
        if (data is not null)
        {
            Rebuild();
        }
    }

    /// <summary>O modo adulto desta ficha: o da Biblioteca, ou Mostrar quando a pessoa revelou este jogo no modo Desfocar.</summary>
    private AdultMode EffectiveAdult =>
        data?.Privacy.Adult is { } mode ? mode == AdultMode.Blur && Reveals.IsRevealed(data.Game.VisualNovelId) ? AdultMode.Show : mode : AdultMode.Blur;

    /// <summary>O jogo está revelado nesta sessão: o retrato e a capa aparecem sem borrão.</summary>
    internal bool IsAdultRevealed => data is not null && data.Privacy.Adult == AdultMode.Blur && Reveals.IsRevealed(data.Game.VisualNovelId);

    internal SheetVisibility Visibility => new(SpoilerLevel, EffectiveAdult);

    /// <summary>"Mostrar" no chip das tags adultas ou no olho do retrato: libera o jogo inteiro (capa, tags, traços e retratos) até a pessoa sair da Biblioteca.</summary>
    [RelayCommand]
    private void RevealAdult()
    {
        if (data is not null)
        {
            Reveals.Reveal(data.Game.VisualNovelId);
        }
    }

    partial void OnSpoilerLevelChanged(int value)
    {
        if (data is not null)
        {
            Rebuild();
        }
    }

    [RelayCommand]
    private void CycleSpoiler() => SpoilerLevel = (SpoilerLevel + 1) % (SheetVisibility.MaxSpoilerLevel + 1);

    /// <summary>"Mostrar" num cartão escondido: sobe o nível até o que o cartão pede.</summary>
    [RelayCommand]
    private void Reveal(CharacterTile? tile)
    {
        if (tile is { ShowsHidden: true } && tile.HiddenLevel > SpoilerLevel)
        {
            SpoilerLevel = tile.HiddenLevel;
        }
    }

    // --- Sinopse ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SynopsisText), nameof(SynopsisToggleText))]
    public partial bool ShowOriginal { get; set; }

    public bool HasSynopsis => card?.Synopsis is not null;

    public string SynopsisText => data?.Game.Synopsis is { } text && card?.Synopsis is { } shown
        ? ShowOriginal ? SheetViewer.ShownSource(text, Visibility).Text : shown.Text
        : "Este jogo não tem sinopse no VNDB.";

    public SheetPill? SynopsisPill => card?.Synopsis is { } shown ? SheetPill.Of(shown.Translated ? shown.Status : TranslationStatus.Untranslated, feminine: true) : null;

    /// <summary>"Ver o original" só existe quando há tradução para alternar.</summary>
    public bool CanToggleOriginal => card?.Synopsis is { Translated: true };

    public string SynopsisToggleText => ShowOriginal ? "Ver a tradução" : "Ver o original";

    [RelayCommand]
    private void ToggleOriginal() => ShowOriginal = !ShowOriginal;

    // --- Tags ---

    public IReadOnlyList<SheetChip> Tags { get; private set; } = [];

    public SheetPill? TagsPill { get; private set; }

    public bool HasTags => Tags.Count > 0;

    public string TagsNote => "O gênero vem em dois valores: o do começo do jogo e o real. O real fica escondido até você pedir.";

    // --- Personagens ---

    public IReadOnlyList<CharacterTile> CharacterTiles => ShowAllCharacters ? tiles : [.. tiles.Take(FirstTiles)];

    public bool HasCharacters => tiles.Count > 0;

    public bool CanShowAll => tiles.Count > FirstTiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterTiles), nameof(ShowAllText))]
    public partial bool ShowAllCharacters { get; set; }

    public string ShowAllText => ShowAllCharacters ? "Ver menos" : "Ver todos";

    public string CharactersCount => $"· {tiles.Count.ToString(CultureInfo.InvariantCulture)} no VNDB";

    [RelayCommand]
    private void ToggleAllCharacters() => ShowAllCharacters = !ShowAllCharacters;

    // --- Sobre o jogo ---

    public string DurationText => card?.LengthDetail ?? string.Empty;

    public string ReleasedText => card?.Released ?? string.Empty;

    public bool HasReleased => card?.Released is not null;

    public string RatingText => card?.Rating ?? string.Empty;

    public bool HasRating => card?.Rating is not null;

    public string VndbIdText => visualNovelId ?? string.Empty;

    // --- Texto em português ---

    public SheetStatusCounts BlockCounts { get; private set; } = new(0, 0, 0, 0, 0);

    public IReadOnlyList<SheetPill> CountPills { get; private set; } = [];

    public bool HasPending => BlockCounts.Pending > 0;

    public string TranslateLabel => HasPending ? "Traduzir com a IA" : "Traduzir de novo com a IA";

    internal bool HasBlocks => blocks.Count > 0;

    internal IReadOnlyList<SheetBlock> Blocks => blocks;

    internal GameSheetCard? Card => card;

    internal SheetStore? Store => ConfigDirectory is { } dir ? new SheetStore(dir) : null;

    // --- Abrir e ler ---

    /// <summary>
    /// Abre a ficha do jogo do VNDB <paramref name="id"/> (nulo quando o projeto não tem o vínculo). O nível de spoiler volta ao da Biblioteca, que esconde por padrão.
    /// </summary>
    public void Open(string? id, string title)
    {
        generation++;
        ListCard = null;
        visualNovelId = id;
        GameTitle = title;
        data = null;
        levelPending = true;
        ShowOriginal = false;
        ShowAllCharacters = false;
        CloseReview();
        State = id is null ? GameSheetState.NoLink : GameSheetState.Loading;
        ErrorMessage = string.Empty;
        Rebuild();
        Loading = id is null ? Task.CompletedTask : LoadAsync();
    }

    [RelayCommand]
    private Task ReloadAsync()
    {
        State = GameSheetState.Loading;
        return Loading = LoadAsync();
    }

    /// <summary>Relê os arquivos da ficha, mantendo o nível de spoiler que a pessoa escolheu.</summary>
    internal async Task LoadAsync()
    {
        var wanted = generation;
        var id = visualNovelId;
        if (id is null)
        {
            return;
        }

        if (ConfigDirectory is not { } directory)
        {
            State = GameSheetState.Failed;
            ErrorMessage = "A pasta de configuração do VNForge não está disponível, então a ficha não pode ser lida.";
            return;
        }

        try
        {
            var loaded = await Task.Run(() => SheetPage.Load(directory, id)).ConfigureAwait(true);
            if (wanted != generation)
            {
                return;
            }

            data = loaded;
            if (loaded is null)
            {
                State = GameSheetState.NotFetched;
            }
            else
            {
                if (levelPending)
                {
                    levelPending = false;
                    SpoilerLevel = SheetVisibility.From(loaded.Privacy).SpoilerLevel;
                }

                State = GameSheetState.Ready;
            }
        }
        catch (Exception ex) when (ex is LibraryException or IOException or UnauthorizedAccessException or JsonException)
        {
            if (wanted != generation)
            {
                return;
            }

            data = null;
            ErrorMessage = ex.Message;
            State = GameSheetState.Failed;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        if (data is null)
        {
            card = null;
            blocks = [];
            tiles = [];
            Tags = [];
            TagsPill = null;
            BlockCounts = new SheetStatusCounts(0, 0, 0, 0, 0);
            CountPills = [];
        }
        else
        {
            var visibility = Visibility;
            card = SheetViewer.Card(data.Game, data.Characters, data.Terms, visibility);
            blocks = SheetBlocks.Build(data.Game, data.Characters, data.Terms, visibility);
            tiles = BuildTiles(card, IsAdultRevealed);
            Tags = BuildTags(card, data.UserTags, data.Privacy.Adult == AdultMode.Blur);
            TagsPill = card.TagsStatus is { } status ? new SheetPill(TagsPillText(status), SheetPill.ToneOf(status)) : null;
            BlockCounts = SheetStatusCounts.OfStatuses(blocks.Select(b => b.Status));
            CountPills = BuildCountPills(BlockCounts);
        }

        OnPropertyChanged(string.Empty);
        Review.Update(blocks, card);
    }

    private static string TagsPillText(TranslationStatus status) => status switch
    {
        TranslationStatus.Untranslated => "Sem tradução ainda, valem para todos os jogos",
        TranslationStatus.Machine => "Traduzidas pela IA, valem para todos os jogos",
        TranslationStatus.Translated => "Traduzidas, valem para todos os jogos",
        TranslationStatus.Reviewed => "Revisadas, valem para todos os jogos",
        _ => "Aprovadas, valem para todos os jogos",
    };

    private static List<SheetChip> BuildTags(GameSheetCard card, IReadOnlyList<string> userTags, bool canReveal)
    {
        var chips = card.Tags.Select(t => new SheetChip(t.Name)).ToList();
        chips.AddRange(userTags.Select(t => new SheetChip(t, IsMine: true)));
        if (card.HiddenSpoilerTags > 0)
        {
            chips.Add(new SheetChip(Count(card.HiddenSpoilerTags, "tag com spoiler", "tags com spoiler"), IsHidden: true));
        }

        if (card.HiddenAdultTags > 0)
        {
            chips.Add(new SheetChip(Count(card.HiddenAdultTags, "tag de conteúdo adulto", "tags de conteúdo adulto"), IsHidden: true, CanReveal: canReveal));
        }

        return chips;
    }

    private static List<CharacterTile> BuildTiles(GameSheetCard card, bool revealed)
    {
        var tiles = new List<CharacterTile>();
        foreach (var character in card.Characters)
        {
            var kind = character.ImageHidden ? (character.Image is null ? TileKind.ImageHidden : TileKind.ImageBlurred) : character.Image is null ? TileKind.NoImage : TileKind.Portrait;
            var line = character.Traits.Count > 0 ? string.Join(", ", character.Traits.Select(t => t.Name)) : character.Description?.Text ?? string.Empty;
            tiles.Add(new CharacterTile(
                character.Id,
                kind,
                character.Name,
                character.Original,
                character.Image,
                new SheetPill(SheetLabels.RoleBadge(character.RoleCode, character.Gender), character.RoleCode == "main" ? SheetTone.Approved : SheetTone.Pending),
                character.Status is { } status ? SheetPill.Of(status) : null,
                line,
                kind == TileKind.NoImage ? "Sem imagem no VNDB" : kind == TileKind.ImageHidden ? "Imagem de conteúdo adulto escondida" : string.Empty,
                0,
                revealed));
        }

        tiles.AddRange(card.HiddenCharacterCards.Select(h => new CharacterTile(h.Id, TileKind.Hidden, "Nome escondido", null, null, null, null, string.Empty, $"Spoiler de nível {h.Spoiler.ToString(CultureInfo.InvariantCulture)}", h.Spoiler)));
        return tiles;
    }

    private static List<SheetPill> BuildCountPills(SheetStatusCounts counts)
    {
        var pills = new List<SheetPill>();
        void Add(int n, string text, SheetTone tone)
        {
            if (n > 0)
            {
                pills.Add(new SheetPill(text, tone));
            }
        }

        Add(counts.Approved, Count(counts.Approved, "aprovado", "aprovados"), SheetTone.Approved);
        Add(counts.Reviewed, Count(counts.Reviewed, "revisado", "revisados"), SheetTone.Reviewed);
        Add(counts.Translated, Count(counts.Translated, "traduzido", "traduzidos"), SheetTone.Translated);
        Add(counts.Machine, $"{counts.Machine.ToString(CultureInfo.InvariantCulture)} pela IA", SheetTone.Ai);
        Add(counts.Pending, $"{counts.Pending.ToString(CultureInfo.InvariantCulture)} sem tradução", SheetTone.Pending);
        return pills;
    }

    internal static string Count(int n, string one, string many) => $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    // --- Ações ---

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        if (visualNovelId is not { } id || ConfigDirectory is not { } directory)
        {
            return;
        }

        if (CatalogFactory is null)
        {
            Notify?.Invoke("Não há conexão com o VNDB configurada neste app.", ToastKind.Warning);
            return;
        }

        IsBusy = true;
        BusyText = "Buscando a ficha no VNDB…";
        try
        {
            var catalog = CatalogFactory();
            var result = await Task.Run(() => SheetFetcher.FetchAsync(catalog, new SheetStore(directory), id, Clock)).ConfigureAwait(true);
            Notify?.Invoke($"Ficha buscada no VNDB: {Count(result.Characters, "personagem", "personagens")} e {Count(result.Sheet.Tags.Count, "tag", "tags")}. {VndbCredit.ShortText}", ToastKind.Success);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notify?.Invoke($"Não foi possível buscar a ficha: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private bool CanFetch() => !IsBusy && visualNovelId is not null && State is GameSheetState.NotFetched or GameSheetState.Ready;

    /// <summary>Traduz o que falta da ficha; quando já está tudo traduzido, refaz o que a IA traduziu e ninguém revisou.</summary>
    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync()
    {
        if (visualNovelId is not { } id || ConfigDirectory is not { } directory)
        {
            return;
        }

        var translator = CreateTranslator();
        if (translator is null)
        {
            return;
        }

        var force = !HasPending;
        var gate = new object();
        var running = true;
        IsBusy = true;
        BusyText = "Traduzindo a ficha…";
        try
        {
            var progress = new Progress<SheetTranslationProgress>(p =>
            {
                // O aviso chega depois, na fila da tela: um que chega depois do fim não pode reescrever o texto já apagado.
                lock (gate)
                {
                    if (running)
                    {
                        BusyText = $"Traduzindo a ficha: {p.Done.ToString(CultureInfo.InvariantCulture)} de {p.Total.ToString(CultureInfo.InvariantCulture)}…";
                    }
                }
            });
            var result = await Task.Run(() => new SheetTranslationRunner(translator, new SheetStore(directory)).RunAsync(id, new SheetTranslationOptions { Force = force }, progress)).ConfigureAwait(true);
            ReportTranslation(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notify?.Invoke($"Não foi possível traduzir a ficha: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            lock (gate)
            {
                running = false;
                IsBusy = false;
                BusyText = string.Empty;
            }
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private bool CanTranslate() => !IsBusy && State == GameSheetState.Ready;

    internal ISheetTranslator? CreateTranslator()
    {
        try
        {
            return TranslatorFactory?.Invoke() ?? throw new InvalidOperationException("Nenhuma IA configurada.");
        }
        catch (InvalidOperationException ex)
        {
            Notify?.Invoke($"{ex.Message} Configure a chave da IA nos Ajustes para traduzir a ficha.", ToastKind.Warning);
            return null;
        }
    }

    internal void ReportTranslation(SheetTranslationResult result)
    {
        if (result.Failed > 0)
        {
            var reason = result.Failures[0].Message;
            Notify?.Invoke($"{Count(result.Translated, "texto traduzido", "textos traduzidos")}; {Count(result.Failed, "ficou", "ficaram")} sem tradução ({reason}). Tente de novo.", ToastKind.Warning);
        }
        else if (result.Translated == 0)
        {
            Notify?.Invoke("Não havia o que traduzir: o que a IA traduziu já foi revisado.", ToastKind.Info);
        }
        else
        {
            Notify?.Invoke($"{Count(result.Translated, "texto traduzido", "textos traduzidos")} pela IA. Revise a ficha para aprová-los.", ToastKind.Success);
        }
    }

    /// <summary>Abre a revisão da ficha, bloco a bloco.</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
    private void StartReview()
    {
        Review.Begin();
        IsReviewing = true;
    }

    private bool CanReview() => State == GameSheetState.Ready && HasBlocks;

    /// <summary>Sai da revisão para a ficha; grava o que foi escrito e ainda não foi gravado.</summary>
    [RelayCommand]
    internal void ExitReview()
    {
        if (IsReviewing)
        {
            if (!Review.CommitQuietly())
            {
                return;
            }

            IsReviewing = false;
        }
    }

    private void CloseReview() => IsReviewing = false;

    partial void OnStateChanged(GameSheetState value)
    {
        FetchCommand.NotifyCanExecuteChanged();
        TranslateCommand.NotifyCanExecuteChanged();
        StartReviewCommand.NotifyCanExecuteChanged();
    }

    internal Task ReloadAfterChangeAsync() => Loading = LoadAsync();

    internal void SetBusy(bool busy) => IsBusy = busy;

    partial void OnIsBusyChanged(bool value) => Review.RefreshActions();
}
