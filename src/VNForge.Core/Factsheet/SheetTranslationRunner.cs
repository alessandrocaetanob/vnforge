using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>Como a ficha é traduzida: <see cref="Force"/> refaz o que a IA já traduziu; os limites cortam os lotes.</summary>
public sealed record SheetTranslationOptions
{
    /// <summary>Traduz de novo só o que está em <see cref="TranslationStatus.Machine"/>. O que a pessoa escreveu, revisou ou aprovou nunca é refeito, nem quando o VNDB mudou o original (<see cref="SheetText.Outdated"/> só existe em texto da pessoa).</summary>
    public bool Force { get; init; }

    /// <summary>Quantos nomes de tag ou de traço vão num pedido.</summary>
    public int NamesPerCall { get; init; } = 80;

    /// <summary>Quantos textos corridos vão num pedido, no máximo.</summary>
    public int TextsPerCall { get; init; } = 8;

    /// <summary>Quantos caracteres de texto corrido vão num pedido, no máximo; um texto maior que isso vai sozinho.</summary>
    public int CharactersPerCall { get; init; } = 6000;
}

/// <summary>Os itens que a tradução ainda tem a fazer, antes de qualquer chamada à IA. É o que o <c>--dry-run</c> mostra.</summary>
public sealed record SheetTranslationPlan(string GameTitle, IReadOnlyList<SheetTranslationItem> Items)
{
    public int Count(SheetItemKind kind) => Items.Count(i => i.Kind == kind);

    /// <summary>Quantos caracteres de original vão para a IA.</summary>
    public int TotalCharacters => Items.Sum(i => i.Source.Length);

    public bool IsEmpty => Items.Count == 0;
}

/// <summary>Itens que ficaram sem tradução, e por quê.</summary>
public sealed record SheetTranslationFailure(IReadOnlyList<string> Keys, string Message);

/// <summary>O que uma execução fez: quantos itens foram traduzidos, quantas chamadas à IA e o que falhou. Itens que falharam seguem pendentes e uma nova execução os pega.</summary>
public sealed record SheetTranslationResult(int Translated, int Calls, IReadOnlyList<SheetTranslationFailure> Failures)
{
    public int Failed => Failures.Sum(f => f.Keys.Count);
}

/// <summary>Quantos itens já foram tentados, de quantos.</summary>
public sealed record SheetTranslationProgress(int Done, int Total);

/// <summary>
/// Traduz a ficha de um jogo pela <see cref="ISheetTranslator"/>. Só pede o que falta: os nomes de tag e de traço que já foram traduzidos (para este ou para outro jogo)
/// não voltam à IA, e cada lote é gravado ao voltar, então uma execução interrompida ou com lote recusado não perde o que já traduziu. A resposta é conferida item a item:
/// chave faltando, texto vazio ou trecho de spoiler que a IA perdeu deixa só aquele item pendente.
/// </summary>
public sealed class SheetTranslationRunner(ISheetTranslator translator, SheetStore store)
{
    /// <summary>O que falta traduzir no jogo.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha.</exception>
    public SheetTranslationPlan Plan(string visualNovelId, bool force = false) => Plan(store, visualNovelId, force);

    /// <summary>O mesmo que <see cref="Plan(string, bool)"/> sem precisar de um tradutor: serve para estimar o custo antes de escolher o provedor.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha.</exception>
    public static SheetTranslationPlan Plan(SheetStore store, string visualNovelId, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        var game = store.LoadGame(visualNovelId) ?? throw new LibraryException($"O jogo {visualNovelId} ainda não tem ficha. Busque-a com vnf library sheet fetch.");
        var characters = store.LoadCharacters(game.CharacterIds);
        var terms = store.LoadTerms();
        List<SheetTranslationItem> items = [];

        if (game.Synopsis is { } synopsis && Needs(synopsis, force))
        {
            items.Add(new SheetTranslationItem(SheetField.Synopsis.Key, SheetItemKind.Synopsis, synopsis.Source, null));
        }

        foreach (var character in characters.Where(c => c.Description is not null && Needs(c.Description, force)))
        {
            items.Add(new SheetTranslationItem(SheetField.OfCharacter(character.Id).Key, SheetItemKind.CharacterDescription, character.Description!.Source, character.Name));
        }

        foreach (var tag in game.Tags)
        {
            if (terms.Find(SheetTermKind.Tag, tag.Id) is { } term && Needs(term.Text, force))
            {
                items.Add(new SheetTranslationItem(SheetField.OfTag(tag.Id).Key, SheetItemKind.TagName, term.Text.Source, term.Context));
            }
        }

        var traits = characters.SelectMany(c => c.Traits).Select(t => t.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var id in traits)
        {
            if (terms.Find(SheetTermKind.Trait, id) is { } term && Needs(term.Text, force))
            {
                items.Add(new SheetTranslationItem(SheetField.OfTrait(id).Key, SheetItemKind.TraitName, term.Text.Source, term.Context));
            }
        }

        return new SheetTranslationPlan(game.Title, items);
    }

    /// <summary>Traduz o que falta, em lotes, e grava cada lote que voltar.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha, ou um arquivo de ficha está ilegível.</exception>
    /// <exception cref="OperationCanceledException">Cancelado; o que já voltou está gravado.</exception>
    public async Task<SheetTranslationResult> RunAsync(
        string visualNovelId, SheetTranslationOptions? options = null, IProgress<SheetTranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new SheetTranslationOptions();
        return await RunPlanAsync(visualNovelId, Plan(visualNovelId, options.Force), options, overwrite: false, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Traduz de novo só os textos pedidos, mesmo os que a pessoa já revisou ou aprovou: é o que "Traduzir de novo" num bloco da revisão faz, depois de a pessoa pedir. O texto volta como
    /// <see cref="TranslationStatus.Machine"/>. Quem chama confirma antes, porque a tradução anterior se perde.
    /// </summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha, ou um dos textos não é dela.</exception>
    /// <exception cref="OperationCanceledException">Cancelado; o que já voltou está gravado.</exception>
    public async Task<SheetTranslationResult> RetranslateAsync(
        string visualNovelId, IReadOnlyList<SheetField> fields, SheetTranslationOptions? options = null, IProgress<SheetTranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return await RunPlanAsync(visualNovelId, PlanFields(store, visualNovelId, fields), options ?? new SheetTranslationOptions(), overwrite: true, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Os itens de <paramref name="fields"/>, que o <see cref="RetranslateAsync"/> pede à IA sem olhar a etapa em que estão.</summary>
    /// <exception cref="LibraryException">O jogo ainda não tem ficha, ou um dos textos não é dela.</exception>
    public static SheetTranslationPlan PlanFields(SheetStore store, string visualNovelId, IReadOnlyList<SheetField> fields)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fields);
        var game = store.LoadGame(visualNovelId) ?? throw new LibraryException($"O jogo {visualNovelId} ainda não tem ficha. Busque-a com vnf library sheet fetch.");
        var terms = store.LoadTerms();
        List<SheetTranslationItem> items = [];
        foreach (var field in fields.Distinct())
        {
            var item = field.Kind switch
            {
                SheetFieldKind.Synopsis when game.Synopsis is { } synopsis => new SheetTranslationItem(field.Key, SheetItemKind.Synopsis, synopsis.Source, null),
                SheetFieldKind.Character when game.CharacterIds.Contains(field.Id!) && store.LoadCharacter(field.Id!) is { Description: { } description } character =>
                    new SheetTranslationItem(field.Key, SheetItemKind.CharacterDescription, description.Source, character.Name),
                SheetFieldKind.Tag when terms.Find(SheetTermKind.Tag, field.Id!) is { } term => new SheetTranslationItem(field.Key, SheetItemKind.TagName, term.Text.Source, term.Context),
                SheetFieldKind.Trait when terms.Find(SheetTermKind.Trait, field.Id!) is { } term => new SheetTranslationItem(field.Key, SheetItemKind.TraitName, term.Text.Source, term.Context),
                _ => null,
            };
            items.Add(item ?? throw new LibraryException($"{field.Key} não está na ficha deste jogo."));
        }

        return new SheetTranslationPlan(game.Title, items);
    }

    private async Task<SheetTranslationResult> RunPlanAsync(
        string visualNovelId, SheetTranslationPlan plan, SheetTranslationOptions options, bool overwrite, IProgress<SheetTranslationProgress>? progress, CancellationToken cancellationToken)
    {
        var translated = 0;
        var calls = 0;
        var done = 0;
        List<SheetTranslationFailure> failures = [];

        foreach (var batch in Batches(plan.Items, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls++;
            IReadOnlyList<SheetTranslation> reply;
            try
            {
                reply = await translator.TranslateAsync(new SheetTranslationRequest(plan.GameTitle, batch), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(new SheetTranslationFailure([.. batch.Select(i => i.Key)], e.Message));
                done += batch.Count;
                progress?.Report(new SheetTranslationProgress(done, plan.Items.Count));
                continue;
            }

            var (accepted, rejected) = Check(batch, reply);
            failures.AddRange(rejected);
            translated += Save(visualNovelId, accepted, options.Force, overwrite);
            done += batch.Count;
            progress?.Report(new SheetTranslationProgress(done, plan.Items.Count));
        }

        return new SheetTranslationResult(translated, calls, failures);
    }

    /// <summary>Os lotes em que <see cref="RunAsync"/> divide os itens: os nomes de tag e de traço juntos, depois os textos corridos, sem passar dos limites de <paramref name="options"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<SheetTranslationItem>> Split(IReadOnlyList<SheetTranslationItem> items, SheetTranslationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. Batches(items, options ?? new SheetTranslationOptions())];
    }

    private static bool Needs(SheetText text, bool force) =>
        text.Status == TranslationStatus.Untranslated || (force && text.Status == TranslationStatus.Machine);

    private static IEnumerable<List<SheetTranslationItem>> Batches(IReadOnlyList<SheetTranslationItem> items, SheetTranslationOptions options)
    {
        var names = items.Where(i => !i.IsProse).ToList();
        for (var i = 0; i < names.Count; i += Math.Max(options.NamesPerCall, 1))
        {
            yield return names.GetRange(i, Math.Min(Math.Max(options.NamesPerCall, 1), names.Count - i));
        }

        var batch = new List<SheetTranslationItem>();
        var size = 0;
        foreach (var item in items.Where(i => i.IsProse))
        {
            if (batch.Count > 0 && (batch.Count >= options.TextsPerCall || size + item.Source.Length > options.CharactersPerCall))
            {
                yield return batch;
                batch = [];
                size = 0;
            }

            batch.Add(item);
            size += item.Source.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static (List<(SheetTranslationItem Item, string Text)> Accepted, List<SheetTranslationFailure> Rejected) Check(
        IReadOnlyList<SheetTranslationItem> batch, IReadOnlyList<SheetTranslation> reply)
    {
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var translation in reply)
        {
            if (translation?.Key is not null && !byKey.TryAdd(translation.Key, translation.Text ?? string.Empty))
            {
                repeated.Add(translation.Key);
            }
        }

        List<(SheetTranslationItem, string)> accepted = [];
        List<SheetTranslationFailure> rejected = [];
        foreach (var item in batch)
        {
            var problem = !byKey.TryGetValue(item.Key, out var text) ? "A IA não devolveu este item."
                : repeated.Contains(item.Key) ? "A IA devolveu este item mais de uma vez."
                : string.IsNullOrWhiteSpace(text) ? "A IA devolveu o item vazio."
                : item.IsProse && !SheetMarkup.SameSpoilerLayout(text, item.Source) ? "A IA perdeu ou mudou um trecho de spoiler."
                : null;
            if (problem is null)
            {
                accepted.Add((item, text!.Trim()));
            }
            else
            {
                rejected.Add(new SheetTranslationFailure([item.Key], problem));
            }
        }

        return (accepted, rejected);
    }

    /// <summary>Grava o lote relendo cada texto sob a trava: só entra o que ainda tem o mesmo original e ainda precisa de tradução (a pessoa pode ter mexido no meio).</summary>
    private int Save(string visualNovelId, List<(SheetTranslationItem Item, string Text)> accepted, bool force, bool overwrite) =>
        accepted.Count == 0
            ? 0
            : store.Update(s =>
            {
                var game = s.LoadGame(visualNovelId)!;
                var terms = s.LoadTerms();
                var saved = 0;
                foreach (var (item, target) in accepted)
                {
                    var field = SheetField.Parse(item.Key);
                    switch (field.Kind)
                    {
                        case SheetFieldKind.Synopsis when game.Synopsis is { } synopsis && Fresh(synopsis, item, force, overwrite):
                            game = game with { Synopsis = synopsis.TranslatedByAi(target) };
                            s.SaveGame(game);
                            saved++;
                            break;
                        case SheetFieldKind.Character when s.LoadCharacter(field.Id!) is { Description: { } description } character && Fresh(description, item, force, overwrite):
                            s.SaveCharacter(character with { Description = description.TranslatedByAi(target) });
                            saved++;
                            break;
                        case SheetFieldKind.Tag or SheetFieldKind.Trait:
                        {
                            var kind = field.Kind == SheetFieldKind.Tag ? SheetTermKind.Tag : SheetTermKind.Trait;
                            if (terms.Find(kind, field.Id!) is { } term && Fresh(term.Text, item, force, overwrite))
                            {
                                terms.Replace(kind, field.Id!, term.Text.TranslatedByAi(target));
                                saved++;
                            }

                            break;
                        }
                    }
                }

                s.SaveTerms(terms);
                return saved;
            });

    private static bool Fresh(SheetText current, SheetTranslationItem item, bool force, bool overwrite) =>
        string.Equals(current.Source, item.Source, StringComparison.Ordinal) && (overwrite || Needs(current, force));
}
