using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Um par da revisão: o original do VNDB, em inglês, e o campo em português, que a pessoa edita.</summary>
public sealed partial class ReviewPairViewModel : ObservableObject
{
    public ReviewPairViewModel(SheetPair pair, bool showsActions, bool spoilersShown)
    {
        Pair = pair;
        ShowsActions = showsActions;
        var hidden = pair.HasSpoilers && !spoilersShown;
        CanEdit = !hidden;
        Original = hidden ? Plain(pair.Original, false) : pair.Original;
        saved = hidden ? Plain(pair.Target ?? string.Empty, false) : pair.Target ?? string.Empty;
        Draft = saved;
        Hint = hidden ? "Este texto tem spoiler. Mostre o spoiler (Tudo) para ler e editar." : string.Empty;
        Pill = SheetPill.Of(pair.Status);
    }

    private string saved;

    [ObservableProperty]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    public SheetPair Pair { get; }

    public string Title => Pair.Title;

    public string Original { get; }

    public SheetPill Pill { get; }

    public bool CanEdit { get; }

    public string Hint { get; }

    public bool HasHint => Hint.Length > 0;

    /// <summary>Só o primeiro par do bloco leva os botões, como no quadro: eles valem para o bloco todo.</summary>
    public bool ShowsActions { get; }

    public bool HasError => Error.Length > 0;

    /// <summary>A pessoa mudou o texto e ainda não o gravou.</summary>
    public bool IsDirty => CanEdit && !string.Equals(Draft.Trim(), saved.Trim(), StringComparison.Ordinal);

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    internal void MarkSaved() => saved = Draft;

    private static string Plain(string text, bool showSpoilers) => LibraryVisibility.PlainSynopsis(text, showSpoilers, out _);
}

/// <summary>Uma etiqueta do cartão "Traços com spoiler": o traço e, se ele tem spoiler, o nível.</summary>
public sealed record TraitChip(string Text, bool IsSpoiler, bool IsHidden);

/// <summary>
/// A revisão da ficha (segunda tela do quadro da #554): um bloco por vez, com o original do VNDB ao lado do português, os botões "Traduzir de novo", "Marcar revisado" e "Aprovar e
/// próxima", o filtro por etapa e o progresso. Um bloco é revisado como uma fala: a etapa mais atrasada dele é a que vale.
/// </summary>
public sealed partial class SheetReviewViewModel : ObservableObject
{
    private static readonly SheetFilterOption[] FilterOptions = [new(SheetReviewFilter.All, "Tudo"), new(SheetReviewFilter.Machine, "Só IA"), new(SheetReviewFilter.Pending, "Só pendentes")];

    private readonly GameSheetViewModel owner;
    private IReadOnlyList<SheetBlock> all = [];
    private List<SheetBlock> shown = [];
    private GameSheetCard? card;
    private string? currentKey;
    private int index;

    internal SheetReviewViewModel(GameSheetViewModel owner) => this.owner = owner;

    public IReadOnlyList<SheetFilterOption> Filters => FilterOptions;

    [ObservableProperty]
    public partial SheetFilterOption SelectedFilter { get; set; } = FilterOptions[0];

    public ObservableCollection<ReviewPairViewModel> Pairs { get; } = [];

    public IReadOnlyList<TraitChip> SpoilerTraits { get; private set; } = [];

    public bool HasSpoilerCard => SpoilerTraits.Count > 0;

    /// <summary>Os títulos dos blocos que o filtro deixa na lista, na ordem da revisão.</summary>
    public IReadOnlyList<string> BlockTitles => [.. shown.Select(b => b.Title)];

    public SheetBlock? Current => index >= 0 && index < shown.Count ? shown[index] : null;

    public bool HasBlock => Current is not null;

    public bool IsEmpty => !HasBlock;

    public string EmptyText => all.Count == 0 ? "Não há nada para revisar nesta ficha." : "Nenhum bloco neste filtro. Escolha Tudo para ver todos.";

    public string Title => "Revisar a ficha";

    public string Subtitle => Current is { } block
        ? $"{block.Title} · bloco {(index + 1).ToString(CultureInfo.InvariantCulture)} de {shown.Count.ToString(CultureInfo.InvariantCulture)} · revisado igual a uma fala"
        : "revisado igual a uma fala";

    // --- Progresso ---

    public int Done => all.Count(b => b.Status >= TranslationStatus.Reviewed);

    public string ProgressText => $"{Done.ToString(CultureInfo.InvariantCulture)} de {all.Count.ToString(CultureInfo.InvariantCulture)}";

    public double ProgressFraction => all.Count == 0 ? 0 : (double)Done / all.Count;

    public IReadOnlyList<SheetPill> CountPills => owner.CountPills;

    public string KeepText => $"Quando {owner.GameTitle} vira projeto, os personagens aprovados entram no Glossário com o gênero, e a sinopse vai para a bíblia da tradução.";

    public string SpoilerLevelText => owner.SpoilerLevelText;

    public bool CanAct => HasBlock && !owner.IsBusy && Pairs.All(p => p.CanEdit);

    internal void Begin()
    {
        SelectedFilter = FilterOptions[0];
        index = 0;
        currentKey = shown.Count > 0 ? shown[0].Key : null;
        Refill();
    }

    /// <summary>A ficha foi relida: refaz a lista pelo filtro e fica no mesmo bloco, ou no que ocupa o lugar dele quando o filtro o tirou da lista.</summary>
    internal void Update(IReadOnlyList<SheetBlock> blocks, GameSheetCard? newCard)
    {
        all = blocks;
        card = newCard;
        var previous = index;
        shown = [.. blocks.Where(Matches)];
        var found = currentKey is null ? -1 : shown.FindIndex(b => b.Key == currentKey);
        index = found >= 0 ? found : Math.Clamp(previous, 0, Math.Max(shown.Count - 1, 0));
        currentKey = Current?.Key;
        Refill();
    }

    partial void OnSelectedFilterChanged(SheetFilterOption value)
    {
        shown = [.. all.Where(Matches)];
        index = 0;
        currentKey = Current?.Key;
        Refill();
    }

    private bool Matches(SheetBlock block) => SelectedFilter.Filter switch
    {
        SheetReviewFilter.Machine => block.Status == TranslationStatus.Machine,
        SheetReviewFilter.Pending => block.Status < TranslationStatus.Reviewed,
        _ => true,
    };

    private void Refill()
    {
        Pairs.Clear();
        if (Current is { } block)
        {
            var spoilersShown = owner.Visibility.ShowsSpoilers;
            for (var i = 0; i < block.Pairs.Count; i++)
            {
                Pairs.Add(new ReviewPairViewModel(block.Pairs[i], i == 0, spoilersShown));
            }

            SpoilerTraits = BuildSpoilerTraits(block);
        }
        else
        {
            SpoilerTraits = [];
        }

        OnPropertyChanged(string.Empty);
        RefreshActions();
    }

    private List<TraitChip> BuildSpoilerTraits(SheetBlock block)
    {
        var character = block.CharacterId is { } id ? card?.Characters.FirstOrDefault(c => c.Id == id) : null;
        if (character is null)
        {
            return [];
        }

        var chips = character.Traits
            .Select(t => t.Spoiler > 0 || t.Lie ? new TraitChip($"{t.Name} · spoiler {Math.Max(t.Spoiler, t.Lie ? 2 : 0).ToString(CultureInfo.InvariantCulture)}", true, false) : new TraitChip(t.Name, false, false))
            .ToList();
        if (character.HiddenTraits > 0)
        {
            chips.Add(new TraitChip(GameSheetViewModel.Count(character.HiddenTraits, "traço escondido", "traços escondidos"), false, true));
        }

        return chips;
    }

    // --- Ações ---

    [RelayCommand]
    private void Next() => Move(1);

    [RelayCommand]
    private void Previous() => Move(-1);

    private void Move(int step)
    {
        var target = index + step;
        if (target < 0 || target >= shown.Count)
        {
            return;
        }

        CommitQuietly();
        index = target;
        currentKey = Current?.Key;
        Refill();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RetranslateAsync()
    {
        if (Current is not { } block || owner.Store is not { } store || owner.VisualNovelId is not { } id)
        {
            return;
        }

        var translator = owner.CreateTranslator();
        if (translator is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await Task.Run(() => new SheetTranslationRunner(translator, store).RetranslateAsync(id, block.Fields)).ConfigureAwait(true);
            owner.ReportTranslation(result);
            return true;
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task MarkReviewedAsync()
    {
        await RunAsync(() => Mark(approve: false)).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ApproveNextAsync()
    {
        var next = index + 1 < shown.Count ? shown[index + 1].Key : null;
        var last = next is null;
        if (!await RunAsync(() => Mark(approve: true)).ConfigureAwait(true))
        {
            return;
        }

        if (next is not null && Current?.Key != next)
        {
            var target = shown.FindIndex(b => b.Key == next);
            if (target >= 0)
            {
                index = target;
                currentKey = Current?.Key;
                Refill();
            }
        }
        else if (last)
        {
            owner.Notify?.Invoke("Fim da lista: não há mais blocos depois deste.", ToastKind.Info);
        }
    }

    private async Task<bool> Mark(bool approve)
    {
        if (Current is not { } block || owner.Store is not { } store || owner.VisualNovelId is not { } id)
        {
            return false;
        }

        var pairs = Pairs.ToList();
        var visibility = owner.Visibility;
        var outcome = await Task.Run(() =>
        {
            var errors = SaveDrafts(store, id, pairs);
            if (errors.Count > 0)
            {
                return (errors, Empty: false);
            }

            // Relê o bloco: o que a pessoa acabou de escrever passa a ter tradução e entra na revisão.
            var fresh = SheetBlocks.Build(store, id, visibility).FirstOrDefault(b => b.Key == block.Key);
            var fields = fresh is null ? [] : fresh.Pairs.SelectMany(p => p.Fields.Zip(p.Texts, (f, t) => (Field: f, Text: t))).Where(x => x.Text.HasTarget).Select(x => x.Field).ToList();
            if (fields.Count == 0)
            {
                return (errors, Empty: true);
            }

            SheetEditor.Review(store, id, fields);
            if (approve)
            {
                SheetEditor.Approve(store, id, fields);
            }

            return (errors, Empty: false);
        }).ConfigureAwait(true);
        Show(outcome.errors);
        if (outcome.Empty)
        {
            owner.Notify?.Invoke("Este bloco ainda não tem tradução. Traduza com a IA ou escreva a sua.", ToastKind.Warning);
        }

        // Com erro (ou sem tradução) a tela fica como está: reler a ficha refaria os campos e apagaria o que a pessoa escreveu.
        return outcome.errors.Count == 0 && !outcome.Empty;
    }

    /// <summary>Grava o que a pessoa escreveu e devolve o erro de cada par que não pôde ser gravado (lista com o número errado de nomes, spoiler perdido).</summary>
    private static List<(ReviewPairViewModel Pair, string Message)> SaveDrafts(SheetStore store, string id, IReadOnlyList<ReviewPairViewModel> pairs)
    {
        var errors = new List<(ReviewPairViewModel, string)>();
        foreach (var pair in pairs.Where(p => p.IsDirty))
        {
            try
            {
                SheetBlocks.EditPair(store, id, pair.Pair, pair.Draft.Trim());
            }
            catch (LibraryException ex)
            {
                errors.Add((pair, ex.Message));
            }
        }

        return errors;
    }

    private void Show(List<(ReviewPairViewModel Pair, string Message)> errors)
    {
        foreach (var pair in Pairs)
        {
            pair.Error = errors.FirstOrDefault(e => ReferenceEquals(e.Pair, pair)).Message ?? string.Empty;
        }
    }

    /// <summary>Grava o texto escrito antes de sair ou de trocar de bloco; o que não vale fica no par e não sai da tela.</summary>
    internal void CommitQuietly()
    {
        if (owner.Store is not { } store || owner.VisualNovelId is not { } id || !Pairs.Any(p => p.IsDirty))
        {
            return;
        }

        var errors = SaveDrafts(store, id, [.. Pairs]);
        Show(errors);
        if (errors.Count == 0)
        {
            _ = owner.ReloadAfterChangeAsync();
        }
    }

    internal void RefreshActions()
    {
        OnPropertyChanged(nameof(CanAct));
        RetranslateCommand.NotifyCanExecuteChanged();
        MarkReviewedCommand.NotifyCanExecuteChanged();
        ApproveNextCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> RunAsync(Func<Task<bool>> action)
    {
        var changed = false;
        owner.SetBusy(true);
        try
        {
            changed = await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is LibraryException or InvalidOperationException or HttpRequestException)
        {
            owner.Notify?.Invoke(ex.Message, ToastKind.Error);
        }
        finally
        {
            owner.SetBusy(false);
        }

        if (changed)
        {
            await owner.ReloadAfterChangeAsync().ConfigureAwait(true);
        }

        return changed;
    }

    [RelayCommand]
    private void CycleSpoiler() => owner.CycleSpoilerCommand.Execute(null);
}
