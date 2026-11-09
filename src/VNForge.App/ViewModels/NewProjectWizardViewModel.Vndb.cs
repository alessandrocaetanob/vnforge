using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Em que ponto está o passo "Ficha do VNDB" do assistente.</summary>
public enum VndbStage
{
    /// <summary>Ainda não buscou.</summary>
    Idle,

    /// <summary>Procurando o jogo no VNDB.</summary>
    Searching,

    /// <summary>Há resultados e a pessoa confirma o certo.</summary>
    Choosing,

    /// <summary>O VNDB não tem o título buscado.</summary>
    Empty,

    /// <summary>O VNDB não respondeu.</summary>
    Failed,

    /// <summary>O assistente foi criado sem catálogo do VNDB.</summary>
    Unavailable,

    /// <summary>Trazendo os personagens do jogo confirmado.</summary>
    Confirming,

    /// <summary>Jogo confirmado: mostra o que chega ao projeto.</summary>
    Preview,

    /// <summary>A pessoa seguiu sem o VNDB.</summary>
    Skipped,
}

/// <summary>Um resultado da busca no VNDB, como cartão de escolha única.</summary>
public sealed partial class VndbCandidateItem(VndbCandidate candidate, string? engine = null) : ObservableObject
{
    public VndbCandidate Candidate { get; } = candidate;

    public string Title => Candidate.VisualNovel.Title;

    /// <summary>O título na escrita original; vazio quando o VNDB não tem.</summary>
    public string OriginalTitle => Candidate.VisualNovel.AltTitle ?? string.Empty;

    public bool HasOriginalTitle => Candidate.VisualNovel.AltTitle is not null;

    /// <summary>Ano, tamanho, duração, motor do jogo e nota, como "2019 · Média · 18 h · YU-RIS · nota 7,9". O motor é o que o assistente detectou na pasta.</summary>
    public string Details => Describe(Candidate.VisualNovel, engine);

    public bool MatchesFolder => Candidate.Evidence == VndbEvidence.FolderAndExe;

    public string EvidenceLabel => MatchesFolder ? "Combina com a pasta e o exe" : "Só o título";

    /// <summary>A capa do jogo no VNDB (a miniatura, com as marcas de conteúdo); nula quando o VNDB não tem. A tela a mostra no <c>Cover</c>, que baixa do cache e respeita a privacidade.</summary>
    public VndbImageRef? Cover => VndbImageRef.Cover(Candidate.VisualNovel.Image);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private static readonly string[] LengthLabels = ["Muito curta", "Curta", "Média", "Longa", "Muito longa"];

    private static string Describe(VndbVisualNovel vn, string? engine)
    {
        var parts = new List<string>();
        if (vn.Released is { Length: >= 4 } released)
        {
            parts.Add(released[..4]);
        }

        if (vn.Length is >= 1 and <= 5)
        {
            parts.Add(LengthLabels[vn.Length.Value - 1]);
        }

        if (vn.LengthMinutes is { } minutes and > 0)
        {
            parts.Add($"{(minutes / 60.0).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))} h");
        }

        if (!string.IsNullOrWhiteSpace(engine))
        {
            parts.Add(engine);
        }

        if (VndbRating.Label(vn.Rating) is { } rating)
        {
            parts.Add(rating);
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Uma linha da tabela "Personagens que chegam". <paramref name="InGame"/> é onde o nome aparece no jogo ("ES.CHAR.NAME 3") ou "sem par no VNDB";
/// vazio quando o assistente não conseguiu ler os nomes do jogo. <paramref name="Display"/> é o nome em destaque (latino, na ordem ocidental);
/// <paramref name="Original"/> vai pequeno abaixo dele, quando <paramref name="HasOriginal"/>.
/// </summary>
public sealed record VndbCharacterRow(string Seed, VndbImageRef? Image, string Original, string Romaji, string Aliases, string Gender, bool HasGender, string InGame, string Display, bool HasOriginal);

/// <summary>O que o jogo confirmado traz ao projeto: personagens e sinopse.</summary>
public sealed record VndbPreview(string Label, string CountText, IReadOnlyList<VndbCharacterRow> Characters, string Synopsis, bool HasSynopsis, string SynopsisNote, string GenderNote, bool IsSynopsisTranslated = false)
{
    /// <summary>Nome do personagem do jogo que o VNDB não tem, no lugar do nome original.</summary>
    private const string NoPair = "sem par no VNDB";

    /// <param name="link">O jogo do VNDB confirmado, com os personagens.</param>
    /// <param name="gameNames">Os nomes de personagem do jogo (<c>ES.CHAR.NAME</c>), na ordem em que o jogo os traz; nulo quando não foi possível lê-los.</param>
    /// <param name="translatedSynopsis">A sinopse que a IA já traduziu, quando há; a tela a mostra com o selo "Traduzida pela IA, você revisa depois". Nulo: fica o texto no idioma do VNDB.</param>
    public static VndbPreview From(VndbLink link, IReadOnlyList<string>? gameNames = null, string? translatedSynopsis = null)
    {
        var translated = string.IsNullOrWhiteSpace(translatedSynopsis) ? null : translatedSynopsis.Trim();
        var (rows, countText, genderNote) = gameNames is null ? Unpaired(link) : Paired(link, gameNames);
        return new VndbPreview(
            $"{link.Title}, {link.VisualNovelId}",
            countText,
            rows,
            translated ?? link.Synopsis ?? "O VNDB não tem sinopse deste jogo.",
            translated is not null || link.Synopsis is not null,
            "Fica guardada no projeto, no idioma do VNDB.",
            genderNote,
            translated is not null);
    }

    /// <summary>Sem os nomes do jogo: lista os personagens do VNDB e diz que o casamento vem na extração.</summary>
    private static (List<VndbCharacterRow>, string, string) Unpaired(VndbLink link)
    {
        var rows = link.Characters.Select(c => Row(c, string.Empty)).ToList();
        var withGender = link.Characters.Count(c => c.Gender != CharacterGender.Unknown);
        return (
            rows,
            $"{link.Characters.Count} do VNDB. Os que casam com os nomes do jogo (ES.CHAR.NAME) entram em characters.json quando você extrai o texto.",
            $"{withGender} dos {link.Characters.Count} personagens do VNDB vêm com gênero. Para esses, o aviso “personagem sem gênero” não aparece quando casarem com o jogo; os outros seguem com o aviso.");
    }

    /// <summary>Com os nomes do jogo: os que casam com o VNDB vêm primeiro, com a posição do nome no jogo; os que o VNDB não tem fecham a tabela.</summary>
    private static (List<VndbCharacterRow>, string, string) Paired(VndbLink link, IReadOnlyList<string> gameNames)
    {
        var matches = VndbProjectLinker.Match(gameNames, link.Characters).GroupBy(m => m.GameName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Character, StringComparer.Ordinal);
        var rows = new List<VndbCharacterRow>();
        var unpaired = new List<VndbCharacterRow>();
        for (var i = 0; i < gameNames.Count; i++)
        {
            var name = gameNames[i];
            if (matches.TryGetValue(name, out var character))
            {
                rows.Add(Row(character, $"ES.CHAR.NAME {i + 1}"));
            }
            else
            {
                unpaired.Add(new VndbCharacterRow(name, null, name, string.Empty, string.Empty, GenderLabel(CharacterGender.Unknown), false, NoPair, name, false));
            }
        }

        var noGender = unpaired.Count + rows.Count(r => !r.HasGender);
        rows.AddRange(unpaired);
        return (
            rows,
            $"{link.Characters.Count} do VNDB, {matches.Count} casados com os nomes do jogo (ES.CHAR.NAME)",
            GenderNoteFor(gameNames.Count, noGender, unpaired.Count));
    }

    private static string GenderNoteFor(int total, int remaining, int unpaired)
    {
        if (total == 0)
        {
            return "O jogo não traz nomes de personagem para casar com o VNDB.";
        }

        var warning = $"O aviso “{(total == 1 ? "1 personagem sem gênero" : $"{ProjectLabels.Number(total)} personagens sem gênero")}”";
        return remaining switch
        {
            0 => $"{warning} some: o VNDB traz o gênero de todos.",
            1 when unpaired == 1 => $"{warning} cai para 1, o da personagem que o VNDB não tem.",
            _ when unpaired == remaining => $"{warning} cai para {ProjectLabels.Number(remaining)}, os dos personagens que o VNDB não tem.",
            _ => $"{warning} cai para {ProjectLabels.Number(remaining)}: os que o VNDB não tem e os que ele traz sem gênero.",
        };
    }

    private static VndbCharacterRow Row(VndbLinkedCharacter c, string inGame) => new(
        c.Name,
        c.Image,
        c.Original ?? string.Empty,
        c.Name,
        c.Aliases.Count == 0 ? string.Empty : string.Join(", ", c.Aliases),
        GenderLabel(c.Gender),
        c.Gender != CharacterGender.Unknown,
        inGame,
        WesternOrder(c.Name, c.Original),
        c.Original is not null);

    /// <summary>O VNDB escreve o romaji de nome japonês com a família primeiro ("Aizawa Kenta"); a coluna em destaque o traz na ordem ocidental ("Kenta Aizawa"). Só troca nome de duas palavras com kanji no original: nome estrangeiro, em katakana, já vem na ordem ocidental.</summary>
    private static string WesternOrder(string name, string? original)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 2 && original is not null && original.Any(IsKanji) ? $"{words[1]} {words[0]}" : name;
    }

    private static bool IsKanji(char c) => c is >= '\u3400' and <= '\u9FFF';

    private static string GenderLabel(CharacterGender gender) => gender switch
    {
        CharacterGender.Male => "Masculino",
        CharacterGender.Female => "Feminino",
        CharacterGender.Other => "Outro",
        _ => "Sem gênero",
    };
}

/// <summary>O passo "Ficha do VNDB": busca o jogo pelo título lido do jogo, pede a confirmação e mostra o que chega ao projeto.</summary>
public sealed partial class NewProjectWizardViewModel
{
    private readonly IVndbCatalog? vndb;
    private readonly Func<GameInspection, string, CancellationToken, IReadOnlyList<string>?> gameCharacterNames;
    private readonly Func<VndbLink, string?> translatedSynopsis;
    private VndbLink? confirmedLink;
    private string? vndbSearchedFolder;
    private CancellationTokenSource? vndbCts;

    /// <summary>Os resultados da busca no VNDB, o mais provável primeiro.</summary>
    public ObservableCollection<VndbCandidateItem> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVndbChoosing), nameof(IsVndbPreview), nameof(IsVndbMessage), nameof(IsVndbBusy), nameof(CanSkipVndb))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(SkipVndbCommand))]
    public partial VndbStage Stage { get; private set; }

    [ObservableProperty]
    public partial string VndbHeadline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string VndbDetail { get; private set; } = string.Empty;

    /// <summary>O que o assistente buscou no VNDB.</summary>
    [ObservableProperty]
    public partial string VndbQuery { get; private set; } = string.Empty;

    /// <summary>O campo de "Buscar por outro título".</summary>
    [ObservableProperty]
    public partial string SearchTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowTitleSearch { get; private set; }

    [ObservableProperty]
    public partial VndbPreview? Preview { get; private set; }

    public bool IsVndbChoosing => Stage == VndbStage.Choosing;

    public bool IsVndbPreview => Stage == VndbStage.Preview;

    /// <summary>Busca em andamento, sem resultado, VNDB fora do ar ou sem catálogo: só o aviso e as saídas.</summary>
    public bool IsVndbMessage => Stage is VndbStage.Searching or VndbStage.Empty or VndbStage.Failed or VndbStage.Unavailable or VndbStage.Confirming or VndbStage.Idle;

    public bool IsVndbBusy => Stage is VndbStage.Searching or VndbStage.Confirming;

    /// <summary>"Nenhum deles, seguir sem VNDB" aparece enquanto o jogo não foi confirmado, também com um pedido ao VNDB em andamento (o 429 do VNDB pode segurar um pedido por minutos).</summary>
    public bool CanSkipVndb => Stage is VndbStage.Choosing or VndbStage.Empty or VndbStage.Failed or VndbStage.Searching or VndbStage.Confirming;

    /// <summary>O jogo do VNDB que o usuário confirmou; nulo quando seguiu sem ele.</summary>
    public VndbLink? ConfirmedVndb => confirmedLink;

    [RelayCommand(CanExecute = nameof(CanSkipVndbNow))]
    private void SkipVndb()
    {
        ErrorMessage = null;
        CancelVndbRequest();
        confirmedLink = null;
        Preview = null;
        Stage = VndbStage.Skipped;
        Step = WizardStep.Names;
    }

    private bool CanSkipVndbNow() => CanSkipVndb;

    [RelayCommand]
    private void ToggleTitleSearch()
    {
        ShowTitleSearch = !ShowTitleSearch;
        if (ShowTitleSearch && string.IsNullOrWhiteSpace(SearchTitle))
        {
            SearchTitle = VndbQuery;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearchVndb))]
    private async Task SearchVndbAsync()
    {
        ShowTitleSearch = false;
        await RunVndbSearchAsync(SearchTitle.Trim(), "Título digitado");
    }

    private bool CanSearchVndb() => !string.IsNullOrWhiteSpace(SearchTitle);

    partial void OnStageChanged(VndbStage value) => RefreshSteps();

    partial void OnSearchTitleChanged(string value) => SearchVndbCommand.NotifyCanExecuteChanged();

    /// <summary>Entra no passo: busca pelo título lido do jogo, uma vez por pasta de jogo.</summary>
    private async Task EnterVndbStepAsync()
    {
        if (vndbSearchedFolder == GameFolder && Stage != VndbStage.Idle)
        {
            return;
        }

        vndbSearchedFolder = GameFolder;
        IsBusy = true;
        string? title = null;
        try
        {
            if (inspection is not null && GameFolder is not null)
            {
                var found = inspection;
                var folder = GameFolder;
                title = await Task.Run(() => ProjectInitializer.ReadGameTitle(found, folder), CancellationToken.None);
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            title = null;
        }
        finally
        {
            IsBusy = false;
        }

        if (title is not null)
        {
            await RunVndbSearchAsync(title, "Título lido do jogo (yscfg.ybn)");
        }
        else
        {
            await RunVndbSearchAsync(FolderNameOf(GameFolder), "Nome da pasta do jogo");
        }
    }

    private async Task RunVndbSearchAsync(string query, string source)
    {
        CancelVndbRequest();
        VndbQuery = query;
        SearchTitle = query;
        ClearCandidates();
        Preview = null;
        confirmedLink = null;
        if (vndb is null)
        {
            VndbHeadline = "A busca no VNDB não está disponível";
            VndbDetail = "Siga sem o VNDB.";
            Stage = VndbStage.Unavailable;
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            VndbHeadline = "Não achamos este título no VNDB";
            VndbDetail = "O jogo não traz o título. Busque por um título ou siga sem o VNDB.";
            Stage = VndbStage.Empty;
            return;
        }

        VndbHeadline = "Buscando o jogo no VNDB";
        VndbDetail = $"{source}: {query}.";
        Stage = VndbStage.Searching;
        IsBusy = true;
        using var cts = vndbCts = new CancellationTokenSource();
        try
        {
            var folder = FolderNameOf(GameFolder);
            var found = await VndbProjectLinker.FindCandidatesAsync(vndb, query, folder, SelectedExe, cancellationToken: cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var candidate in found)
            {
                var item = new VndbCandidateItem(candidate, EngineName);
                item.PropertyChanged += OnCandidateChanged;
                Candidates.Add(item);
            }

            if (Candidates.Count == 0)
            {
                VndbHeadline = "Não achamos este título no VNDB";
                VndbDetail = $"{source}: {query}. Busque por outro título ou siga sem o VNDB.";
                Stage = VndbStage.Empty;
                return;
            }

            VndbHeadline = Candidates.Count == 1 ? "Achamos 1 jogo com este título no VNDB" : $"Achamos {Candidates.Count} jogos com este título no VNDB";
            VndbDetail = $"{source}: {query}. Confirme o certo para trazer personagens e sinopse.";
            Stage = VndbStage.Choosing;
            if (Candidates[0].MatchesFolder)
            {
                Candidates[0].IsSelected = true;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Quem cancelou (seguir sem VNDB, Cancelar, outra busca) já cuidou do estado.
        }
        catch (VNForgeException e) when (!cts.IsCancellationRequested)
        {
            VndbHeadline = "O VNDB não respondeu";
            VndbDetail = $"{e.Message} Tente de novo ou siga sem o VNDB.";
            Stage = VndbStage.Failed;
        }
        finally
        {
            FinishVndbRequest(cts);
        }
    }

    private void ClearCandidates()
    {
        foreach (var item in Candidates)
        {
            item.PropertyChanged -= OnCandidateChanged;
        }

        Candidates.Clear();
    }

    private void OnCandidateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VndbCandidateItem.IsSelected) || sender is not VndbCandidateItem { IsSelected: true } chosen)
        {
            NextCommand.NotifyCanExecuteChanged();
            return;
        }

        foreach (var other in Candidates.Where(c => !ReferenceEquals(c, chosen)))
        {
            other.IsSelected = false;
        }

        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Avançar com um resultado marcado confirma o jogo: traz os personagens e mostra o que chega ao projeto.</summary>
    private async Task ConfirmVndbAsync()
    {
        var chosen = Candidates.FirstOrDefault(c => c.IsSelected);
        if (chosen is null || vndb is null)
        {
            return;
        }

        Stage = VndbStage.Confirming;
        VndbHeadline = "Trazendo os personagens";
        VndbDetail = chosen.Title;
        IsBusy = true;
        using var cts = vndbCts = new CancellationTokenSource();
        try
        {
            var characters = await vndb.GetCharactersAsync(chosen.Candidate.VisualNovel.Id, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var gameNames = await ReadGameNamesAsync(cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            confirmedLink = VndbProjectLinker.From(chosen.Candidate.VisualNovel, characters);
            Preview = VndbPreview.From(confirmedLink, gameNames, translatedSynopsis(confirmedLink));
            Stage = VndbStage.Preview;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Quem cancelou já cuidou do estado.
        }
        catch (VNForgeException e) when (!cts.IsCancellationRequested)
        {
            ErrorMessage = $"{e.Message} Tente de novo ou siga sem o VNDB.";
            Stage = VndbStage.Choosing;
            VndbHeadline = ChoosingHeadline();
        }
        finally
        {
            FinishVndbRequest(cts);
        }
    }

    /// <summary>
    /// Os nomes de personagem que o jogo traz, para a ficha dizer quais casam com o VNDB. Lê os scripts numa pasta temporária; se não der (engine sem extração,
    /// script ilegível), devolve nulo e a ficha lista os personagens do VNDB, com o casamento para quando o texto for extraído.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ReadGameNamesAsync(CancellationToken cancellationToken)
    {
        if (inspection is not { } found || GameFolder is not { } folder)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => gameCharacterNames(found, folder, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (IsExpected(e) && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private string ChoosingHeadline() =>
        Candidates.Count == 1 ? "Achamos 1 jogo com este título no VNDB" : $"Achamos {Candidates.Count} jogos com este título no VNDB";

    /// <summary>Cancela o pedido ao VNDB em andamento, se houver, e solta o assistente.</summary>
    private void CancelVndbRequest()
    {
        if (vndbCts is not { } running)
        {
            return;
        }

        vndbCts = null;
        running.Cancel();
        IsBusy = false;
    }

    private void FinishVndbRequest(CancellationTokenSource cts)
    {
        if (ReferenceEquals(vndbCts, cts))
        {
            vndbCts = null;
            IsBusy = false;
        }
    }

    /// <summary>Voltando ao passo depois de "seguir sem VNDB": a lista volta como estava; se o pedido foi interrompido antes de ter resultado, o passo oferece buscar de novo.</summary>
    private void RestoreVndbAfterSkip()
    {
        if (Candidates.Count > 0)
        {
            VndbHeadline = ChoosingHeadline();
            VndbDetail = $"Título buscado: {VndbQuery}. Confirme o certo para trazer personagens e sinopse.";
            Stage = VndbStage.Choosing;
            return;
        }

        VndbHeadline = "A busca foi interrompida";
        VndbDetail = "Busque por um título ou siga sem o VNDB.";
        Stage = VndbStage.Empty;
    }

    /// <summary>Volta da confirmação para a lista de resultados, sem sair do passo.</summary>
    private bool BackFromVndbPreview()
    {
        if (Step != WizardStep.Vndb || Stage != VndbStage.Preview)
        {
            return false;
        }

        confirmedLink = null;
        Preview = null;
        Stage = VndbStage.Choosing;
        return true;
    }

    private bool CanGoNextFromVndb() => Stage switch
    {
        VndbStage.Choosing => Candidates.Any(c => c.IsSelected),
        VndbStage.Preview or VndbStage.Empty or VndbStage.Failed or VndbStage.Unavailable or VndbStage.Skipped => true,
        _ => false,
    };

    private void ResetVndb()
    {
        CancelVndbRequest();
        ClearCandidates();
        confirmedLink = null;
        vndbSearchedFolder = null;
        Preview = null;
        ShowTitleSearch = false;
        Stage = VndbStage.Idle;
    }
}
