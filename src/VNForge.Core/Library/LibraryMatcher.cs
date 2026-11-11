using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>As regras do casamento. Nada abaixo de <see cref="AutoThreshold"/> é confirmado sozinho, e nem acima dele quando o segundo colocado chega perto (<see cref="AutoMargin"/>).</summary>
public sealed record LibraryMatchOptions
{
    public const double DefaultAutoThreshold = 0.9;

    /// <summary>Confirma sozinho o candidato que passa dos limites. Sem isto, o casamento só propõe candidatos.</summary>
    public bool AutoConfirm { get; init; }

    /// <summary>Pontuação mínima (0 a 1) para confirmar sozinho.</summary>
    public double AutoThreshold { get; init; } = DefaultAutoThreshold;

    /// <summary>Quanto o primeiro precisa passar do segundo para ser confirmado sozinho.</summary>
    public double AutoMargin { get; init; } = 0.1;

    /// <summary>Candidatos abaixo disto não são guardados.</summary>
    public double MinCandidateScore { get; init; } = 0.35;

    public int MaxCandidates { get; init; } = 5;

    /// <summary>Quantos textos diferentes se busca por jogo, no máximo; o VNDB limita os pedidos.</summary>
    public int MaxSearches { get; init; } = 3;

    /// <summary>Quantos resultados pedir ao VNDB em cada busca.</summary>
    public int SearchLimit { get; init; } = 8;

    /// <summary>Falhas seguidas do VNDB que param o lote (rede fora, limite de pedidos).</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>O primeiro colocado pode ser confirmado sozinho: passa do limiar e, havendo segundo, está à frente dele pela folga.</summary>
    public bool IsDecisive(double top, double? second) =>
        top >= AutoThreshold - Epsilon && (second is null || top - second.Value >= AutoMargin - Epsilon);

    /// <summary>Tolerância do ponto flutuante: <c>1,0 - 0,9</c> dá 0,0999... e a folga de 10 pontos tem de contar.</summary>
    private const double Epsilon = 1e-9;
}

/// <summary>Um jogo cuja busca falhou e por quê.</summary>
public sealed record LibraryMatchFailure(LibraryGame Game, string Message);

/// <summary>O andamento do lote: quantos jogos já foram buscados (com ou sem falha), de quantos, e o último que terminou.</summary>
public sealed record LibraryMatchProgress(int Done, int Total, LibraryGame Game);

/// <summary>
/// O resultado de um lote de casamentos. <paramref name="Confirmed"/> são os confirmados sozinhos, <paramref name="Pending"/> os que ficaram com candidatos
/// para a pessoa escolher, <paramref name="NotFound"/> os que o VNDB não achou. <paramref name="AbortReason"/> diz por que parou antes do fim.
/// </summary>
public sealed record LibraryMatchRun(
    IReadOnlyList<LibraryGame> Confirmed,
    IReadOnlyList<LibraryGame> Pending,
    IReadOnlyList<LibraryGame> NotFound,
    IReadOnlyList<LibraryMatchFailure> Failures,
    bool Cancelled,
    string? AbortReason);

/// <summary>
/// Casa os jogos da biblioteca com o VNDB: busca pelo título da janela, pela pasta e pelo executável, pontua cada candidato contra todos os termos e,
/// só se a pessoa pedir (<see cref="LibraryMatchOptions.AutoConfirm"/>), confirma o que não deixa dúvida. O catálogo entra por <see cref="IVndbCatalog"/>.
/// </summary>
public sealed class LibraryMatcher(IVndbCatalog catalog, TimeProvider? time = null)
{
    private static readonly Dictionary<TermSource, double> Weights = new Dictionary<TermSource, double>
    {
        [TermSource.WindowTitle] = 1.0,
        [TermSource.Folder] = 0.95,
        [TermSource.Exe] = 0.85,
    };

    private const double AgreementThreshold = 0.8;
    private const double AgreementBonus = 0.05;
    private const double EvidenceThreshold = 0.6;
    private const int MaxTags = 40;
    private const double AdultImageLevel = VndbImageVisibility.AdultLevel;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Busca o jogo no VNDB e devolve-o com os candidatos (e confirmado, se as regras deixarem). O jogo já confirmado volta como está.
    /// </summary>
    /// <exception cref="VndbException">O VNDB falhou.</exception>
    public async Task<LibraryGame> MatchAsync(LibraryGame game, LibraryMatchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        options ??= new LibraryMatchOptions();
        if (game.IsConfirmed)
        {
            return game;
        }

        var terms = GameNames.SearchTerms(game.WindowTitle, game.Exe, GameLibrary.FolderName(game));
        var found = new Dictionary<string, VndbVisualNovel>(StringComparer.Ordinal);
        List<Scored> ranked = [];
        var searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var termText in terms.Select(term => term.Text))
        {
            if (searched.Count >= options.MaxSearches)
            {
                break;
            }

            if (!searched.Add(termText))
            {
                continue;
            }

            foreach (var result in await catalog.SearchVisualNovelsAsync(termText,options.SearchLimit, cancellationToken).ConfigureAwait(false))
            {
                found.TryAdd(result.Id, result);
            }

            ranked = Rank(found.Values, terms, options);
            if (IsDecisive(ranked, options))
            {
                break;
            }
        }

        var candidates = ranked.Take(options.MaxCandidates).ToList();
        var updated = game with
        {
            SearchedAt = _time.GetUtcNow(),
            Candidates = [.. candidates.Select(c => c.Candidate)],
        };

        if (options.AutoConfirm && IsDecisive(ranked, options))
        {
            return Confirm(updated, ranked[0].Novel, LibraryConfirmation.Auto, ranked[0].Candidate.Score);
        }

        return updated;
    }

    /// <summary>Confirma o jogo do VNDB <paramref name="vndbId"/> para o jogo da biblioteca, buscando a ficha no VNDB.</summary>
    /// <exception cref="LibraryException">O VNDB não tem o jogo.</exception>
    /// <exception cref="VndbException">O VNDB falhou.</exception>
    public async Task<LibraryGame> ConfirmAsync(LibraryGame game, string vndbId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        var novel = await catalog.GetVisualNovelAsync(vndbId, cancellationToken).ConfigureAwait(false)
            ?? throw new LibraryException($"O VNDB não tem o jogo {vndbId}. Confira o id (como v17).");
        var score = game.Candidates.FirstOrDefault(c => c.VndbId == novel.Id)?.Score;
        return Confirm(game with { SearchedAt = game.SearchedAt ?? _time.GetUtcNow() }, novel, LibraryConfirmation.Manual, score);
    }

    /// <summary>Marca o jogo como confirmado e guarda a ficha (capa, sinopse, duração, tags) do VNDB.</summary>
    public static LibraryGame Confirm(LibraryGame game, VndbVisualNovel novel, LibraryConfirmation by, double? score = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(novel);
        var info = InfoOf(novel);
        return game with
        {
            VndbId = novel.Id,
            Confirmation = by,
            MatchScore = score,
            Title = info.Title,
            Info = info,
        };
    }

    /// <summary>Desfaz a confirmação e volta o título para o da pasta. Os candidatos ficam.</summary>
    public static LibraryGame Unconfirm(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var name = GameLibrary.FolderName(game);
        return game with
        {
            VndbId = null,
            Confirmation = LibraryConfirmation.None,
            MatchScore = null,
            Info = null,
            Title = GameNames.CleanFolderName(game.Folder) ?? game.WindowTitle ?? (string.IsNullOrWhiteSpace(name) ? game.Folder : name),
        };
    }

    /// <summary>
    /// Casa os jogos ainda sem confirmação (e sem busca anterior, a menos que <paramref name="again"/>) e vai trocando-os na <paramref name="library"/>, para que quem
    /// grava o arquivo no fim, ou no cancelamento, não perca o que já foi feito.
    /// </summary>
    /// <param name="onlyGame">Casa só este jogo, mesmo que já tenha sido buscado.</param>
    /// <param name="onMatched">Chamada a cada jogo casado, logo depois de trocá-lo na <paramref name="library"/>: quem grava o arquivo o faz aqui, jogo a jogo, e não depende do fim do lote.</param>
    public async Task<LibraryMatchRun> MatchAllAsync(
        GameLibrary library,
        LibraryMatchOptions? options = null,
        bool again = false,
        string? onlyGame = null,
        IProgress<LibraryMatchProgress>? progress = null,
        Action<LibraryGame>? onMatched = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        options ??= new LibraryMatchOptions();
        var queue = library.Games
            .Where(g => !g.IsConfirmed && (onlyGame is not null ? g.Id == onlyGame : again || g.SearchedAt is null))
            .ToList();

        List<LibraryGame> confirmed = [];
        List<LibraryGame> pending = [];
        List<LibraryGame> notFound = [];
        List<LibraryMatchFailure> failures = [];
        var consecutive = 0;
        var cancelled = false;
        string? abort = null;
        for (var i = 0; i < queue.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            try
            {
                var matched = await MatchAsync(queue[i], options, cancellationToken).ConfigureAwait(false);
                library.Replace(matched);
                onMatched?.Invoke(matched);
                consecutive = 0;
                if (matched.IsConfirmed)
                {
                    confirmed.Add(matched);
                }
                else if (matched.Candidates.Count > 0)
                {
                    pending.Add(matched);
                }
                else
                {
                    notFound.Add(matched);
                }

                progress?.Report(new LibraryMatchProgress(i + 1, queue.Count, matched));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (VndbException e)
            {
                failures.Add(new LibraryMatchFailure(queue[i], e.Message));
                progress?.Report(new LibraryMatchProgress(i + 1, queue.Count, queue[i]));
                consecutive++;
                if (consecutive >= options.MaxConsecutiveFailures)
                {
                    abort = $"O VNDB falhou {consecutive} vezes seguidas; parei. Último erro: {e.Message}";
                    break;
                }
            }
        }

        return new LibraryMatchRun(confirmed, pending, notFound, failures, cancelled, abort);
    }

    /// <summary>A ficha que a Biblioteca guarda do jogo do VNDB.</summary>
    public static LibraryVndbInfo InfoOf(VndbVisualNovel novel)
    {
        ArgumentNullException.ThrowIfNull(novel);
        var tags = novel.Tags
            .Where(t => !t.Lie)
            .OrderByDescending(t => t.Rating)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Take(MaxTags)
            .Select(t => new LibraryTag(t.Id, t.Name, t.Category, t.Rating, t.Spoiler))
            .ToList();
        var adult = (novel.Image is { } image && image.Sexual >= AdultImageLevel)
            || tags.Any(t => t.Category == "ero" && t.Spoiler == 0 && t.Rating >= 2);
        return new LibraryVndbInfo(
            novel.Title,
            novel.AltTitle,
            novel.Released,
            novel.Image?.Url,
            novel.Description,
            novel.Length,
            novel.LengthMinutes,
            novel.LengthVotes,
            tags,
            adult,
            novel.Rating);
    }

    private static bool IsDecisive(List<Scored> ranked, LibraryMatchOptions options) =>
        ranked.Count > 0
        && options.IsDecisive(ranked[0].Candidate.Score, ranked.Count > 1 ? ranked[1].Candidate.Score : null);

    private static List<Scored> Rank(IEnumerable<VndbVisualNovel> novels, IReadOnlyList<SearchTerm> terms, LibraryMatchOptions options) =>
        [.. novels
            .Select(n => Score(n, terms))
            .Where(s => s.Candidate.Score >= options.MinCandidateScore)
            .OrderByDescending(s => s.Candidate.Score)
            .ThenBy(s => VndbNumber(s.Candidate.VndbId))];

    private static Scored Score(VndbVisualNovel novel, IReadOnlyList<SearchTerm> terms)
    {
        var names = TitlesOf(novel);
        var bySource = new Dictionary<TermSource, double>();
        foreach (var term in terms)
        {
            var similarity = names.Select(n => TitleSimilarity.Between(term.Text, n)).DefaultIfEmpty(0).Max();
            bySource[term.Source] = Math.Max(bySource.GetValueOrDefault(term.Source), similarity);
        }

        var weighted = bySource.Select(p => (Source: p.Key, Similarity: p.Value, Score: p.Value * Weights[p.Key])).ToList();
        var best = weighted.Count == 0 ? 0 : weighted.Max(w => w.Score);
        var agreeing = weighted.Count(w => w.Similarity >= AgreementThreshold);
        var score = Math.Min(1.0, best + (Math.Max(0, agreeing - 1) * AgreementBonus));
        var evidence = weighted
            .Where(w => w.Similarity >= EvidenceThreshold)
            .OrderBy(w => w.Source)
            .Select(w => EvidenceName(w.Source))
            .ToList();
        return new Scored(novel, new LibraryCandidate(novel.Id, novel.Title, novel.AltTitle, novel.Released, Math.Round(score, 3), evidence)
        {
            Image = VndbImageRef.Cover(novel.Image),
            Length = novel.Length,
            LengthMinutes = novel.LengthMinutes,
            Rating = novel.Rating,
        });
    }

    private static IEnumerable<string> TitlesOf(VndbVisualNovel novel)
    {
        var names = new List<string> { novel.Title };
        if (novel.AltTitle is not null)
        {
            names.Add(novel.AltTitle);
        }

        foreach (var title in novel.Titles)
        {
            names.Add(title.Title);
            if (title.Latin is not null)
            {
                names.Add(title.Latin);
            }
        }

        names.AddRange(novel.Aliases);
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal);
    }

    private static string EvidenceName(TermSource source) => source switch
    {
        TermSource.WindowTitle => "janela",
        TermSource.Folder => "pasta",
        _ => "exe",
    };

    private static long VndbNumber(string id) =>
        id.Length > 1 && long.TryParse(id.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;

    private sealed record Scored(VndbVisualNovel Novel, LibraryCandidate Candidate);
}
