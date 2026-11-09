using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
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
public sealed partial class VndbCandidateItem(VndbCandidate candidate) : ObservableObject
{
    public VndbCandidate Candidate { get; } = candidate;

    public string Title => Candidate.VisualNovel.Title;

    /// <summary>O título na escrita original; vazio quando o VNDB não tem.</summary>
    public string OriginalTitle => Candidate.VisualNovel.AltTitle ?? string.Empty;

    public bool HasOriginalTitle => Candidate.VisualNovel.AltTitle is not null;

    /// <summary>Ano, tamanho e duração, como "2019 · Média · 18 h".</summary>
    public string Details => Describe(Candidate.VisualNovel);

    public bool MatchesFolder => Candidate.Evidence == VndbEvidence.FolderAndExe;

    public string EvidenceLabel => MatchesFolder ? "Combina com a pasta e o exe" : "Só o título";

    /// <summary>A primeira letra do título, no lugar da capa: o app ainda não baixa as imagens do VNDB.</summary>
    public string CoverInitial => Title.Length == 0 ? "?" : Title[..1].ToUpperInvariant();

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private static readonly string[] LengthLabels = ["Muito curta", "Curta", "Média", "Longa", "Muito longa"];

    private static string Describe(VndbVisualNovel vn)
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

        return string.Join(" · ", parts);
    }
}

/// <summary>Uma linha da tabela "Personagens que chegam".</summary>
public sealed record VndbCharacterRow(string Initial, string Original, string Romaji, string Aliases, string Gender, bool HasGender, string Role);

/// <summary>O que o jogo confirmado traz ao projeto: personagens e sinopse.</summary>
public sealed record VndbPreview(string Label, string CountText, IReadOnlyList<VndbCharacterRow> Characters, string Synopsis, bool HasSynopsis, string SynopsisNote, string GenderNote)
{
    public static VndbPreview From(VndbLink link)
    {
        var rows = link.Characters.Select(c => new VndbCharacterRow(
            (c.Original ?? c.Name) is { Length: > 0 } shown ? shown[..1] : "?",
            c.Original ?? "Sem escrita original",
            c.Name,
            c.Aliases.Count == 0 ? string.Empty : string.Join(", ", c.Aliases),
            GenderLabel(c.Gender),
            c.Gender != CharacterGender.Unknown,
            RoleLabel(c.Role))).ToList();
        var withGender = link.Characters.Count(c => c.Gender != CharacterGender.Unknown);
        return new VndbPreview(
            $"{link.Title}, {link.VisualNovelId}",
            $"{link.Characters.Count} do VNDB. Os que casam com os nomes do jogo (ES.CHAR.NAME) entram em characters.json quando você extrai o texto.",
            rows,
            link.Synopsis ?? "O VNDB não tem sinopse deste jogo.",
            link.Synopsis is not null,
            "Fica guardada no projeto, no idioma do VNDB.",
            $"{withGender} dos {link.Characters.Count} personagens do VNDB vêm com gênero. O aviso “personagem sem gênero” não aparece para os que casarem com o jogo.");
    }

    private static string GenderLabel(CharacterGender gender) => gender switch
    {
        CharacterGender.Male => "Masculino",
        CharacterGender.Female => "Feminino",
        CharacterGender.Other => "Outro",
        _ => "Sem gênero",
    };

    private static string RoleLabel(string role) => role switch
    {
        "main" => "Protagonista",
        "primary" => "Principal",
        "side" => "Secundário",
        _ => "Aparece",
    };
}

/// <summary>O passo "Ficha do VNDB": busca o jogo pelo título lido do jogo, pede a confirmação e mostra o que chega ao projeto.</summary>
public sealed partial class NewProjectWizardViewModel
{
    private readonly IVndbCatalog? vndb;
    private VndbLink? confirmedLink;
    private string? vndbSearchedFolder;

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

    /// <summary>"Nenhum deles, seguir sem VNDB" aparece enquanto o jogo não foi confirmado.</summary>
    public bool CanSkipVndb => Stage is VndbStage.Choosing or VndbStage.Empty or VndbStage.Failed;

    /// <summary>O jogo do VNDB que o usuário confirmou; nulo quando seguiu sem ele.</summary>
    public VndbLink? ConfirmedVndb => confirmedLink;

    [RelayCommand(CanExecute = nameof(CanSkipVndbNow))]
    private void SkipVndb()
    {
        ErrorMessage = null;
        confirmedLink = null;
        Preview = null;
        Stage = VndbStage.Skipped;
        Step = WizardStep.Names;
    }

    private bool CanSkipVndbNow() => !IsBusy && CanSkipVndb;

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

    private bool CanSearchVndb() => !IsBusy && !string.IsNullOrWhiteSpace(SearchTitle);

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
                title = await Task.Run(() => ProjectInitializer.ReadGameTitle(found, folder));
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
        try
        {
            var folder = FolderNameOf(GameFolder);
            var found = await VndbProjectLinker.FindCandidatesAsync(vndb, query, folder, SelectedExe);
            foreach (var candidate in found)
            {
                var item = new VndbCandidateItem(candidate);
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
        catch (VNForgeException e)
        {
            VndbHeadline = "O VNDB não respondeu";
            VndbDetail = $"{e.Message} Tente de novo ou siga sem o VNDB.";
            Stage = VndbStage.Failed;
        }
        finally
        {
            IsBusy = false;
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
        try
        {
            var characters = await vndb.GetCharactersAsync(chosen.Candidate.VisualNovel.Id);
            confirmedLink = VndbProjectLinker.From(chosen.Candidate.VisualNovel, characters);
            Preview = VndbPreview.From(confirmedLink);
            Stage = VndbStage.Preview;
        }
        catch (VNForgeException e)
        {
            ErrorMessage = $"{e.Message} Tente de novo ou siga sem o VNDB.";
            Stage = VndbStage.Choosing;
            VndbHeadline = Candidates.Count == 1 ? "Achamos 1 jogo com este título no VNDB" : $"Achamos {Candidates.Count} jogos com este título no VNDB";
        }
        finally
        {
            IsBusy = false;
        }
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
        ClearCandidates();
        confirmedLink = null;
        vndbSearchedFolder = null;
        Preview = null;
        ShowTitleSearch = false;
        Stage = VndbStage.Idle;
    }
}
