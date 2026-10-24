using VNForge.Core.Text;

namespace VNForge.Core.Search;

/// <summary>Em que parte da linha a busca procura.</summary>
public enum SearchField
{
    /// <summary>A tradução (só linhas com tradução utilizável). É o único campo que a substituição altera.</summary>
    Translation,

    /// <summary>O original do jogo. Só leitura: nunca é substituído.</summary>
    Source,
}

/// <summary>O que procurar e como.</summary>
/// <param name="Pattern">Texto ou expressão regular (<paramref name="UseRegex"/>). Vazio é erro.</param>
/// <param name="UseRegex">Verdadeiro trata <paramref name="Pattern"/> como expressão regular do .NET; falso procura o texto literal.</param>
/// <param name="MatchCase">Verdadeiro diferencia maiúsculas de minúsculas.</param>
/// <param name="WholeWord">
/// Verdadeiro só aceita ocorrências que não encostam em letra, dígito, sublinhado ou acento combinante. Um marcador de controle (<c>\n</c>, <c>{...}</c>)
/// vizinho conta como separador. Não serve para o original em japonês: kana e kanji são letras e nunca ficam separados.
/// </param>
/// <param name="Field">Onde procurar.</param>
public sealed record SearchOptions(
    string Pattern,
    bool UseRegex = false,
    bool MatchCase = false,
    bool WholeWord = false,
    SearchField Field = SearchField.Translation)
{
    /// <summary>Tempo máximo de uma busca numa só linha; uma expressão que passe disso é recusada em vez de travar a tela.</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Tempo máximo de uma busca numa só linha.</summary>
    public TimeSpan MatchTimeout { get; init; } = DefaultMatchTimeout;
}

/// <summary>Trecho de um texto, em <c>char</c> UTF-16.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>Uma linha em que a busca achou algo.</summary>
/// <param name="ScriptKey">Chave do script.</param>
/// <param name="Number">Posição da linha no script, a partir de 1.</param>
/// <param name="UnitId">Id da unidade em <c>text/*.json</c>.</param>
/// <param name="Text">O texto em que se procurou (tradução ou original).</param>
/// <param name="Matches">Onde as ocorrências estão em <paramref name="Text"/>, em ordem.</param>
public sealed record SearchHit(string ScriptKey, int Number, string UnitId, string Text, IReadOnlyList<TextRange> Matches);

/// <summary>O resultado de busca (<c>ProjectSearch.Find</c>).</summary>
/// <param name="Hits">As linhas com ocorrência, por script e número de linha.</param>
/// <param name="EmptyMatchLines">Linhas em que o padrão só casou texto vazio (como <c>^</c>, <c>$</c> ou um lookahead), que a busca ignora.</param>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, int EmptyMatchLines);

/// <summary>Uma linha que a substituição vai alterar.</summary>
/// <param name="ScriptKey">Chave do script.</param>
/// <param name="Number">Posição da linha no script, a partir de 1.</param>
/// <param name="UnitId">Id da unidade em <c>text/*.json</c>.</param>
/// <param name="Before">A tradução como está agora.</param>
/// <param name="After">A tradução como ficará.</param>
/// <param name="Count">Quantas ocorrências a linha troca.</param>
/// <param name="PreviousStatus">O status da linha agora; gravar a substituição a deixa <see cref="TranslationStatus.Translated"/>.</param>
public sealed record ReplaceItem(string ScriptKey, int Number, string UnitId, string Before, string After, int Count, TranslationStatus PreviousStatus = TranslationStatus.Translated);

/// <summary>Por que uma linha com ocorrência ficou como está.</summary>
public enum SkipReason
{
    /// <summary>O texto da linha já tem um marcador de controle malformado; não dá para saber o que é marcador.</summary>
    MalformedMarkers,

    /// <summary>O texto de troca deixaria um marcador de controle malformado (uma barra solta, uma chave sem fechamento).</summary>
    ReplacementBreaksMarkers,
}

/// <summary>Uma linha com ocorrência que a substituição deixou como está.</summary>
/// <param name="ScriptKey">Chave do script.</param>
/// <param name="Number">Posição da linha no script, a partir de 1.</param>
/// <param name="Reason">O motivo.</param>
public sealed record SkippedLine(string ScriptKey, int Number, SkipReason Reason);

/// <summary>
/// A prévia da substituição: cada linha que mudaria, sem gravar nada. É também o que <see cref="ProjectSearch.Apply"/> grava.
/// </summary>
/// <param name="Options">A busca que gerou a prévia.</param>
/// <param name="Replacement">O texto de troca informado (com <c>$1</c> etc. quando a busca é regex).</param>
/// <param name="Items">As linhas que mudam, por script e número de linha.</param>
/// <param name="ProtectedMatches">Ocorrências que encostam num marcador de controle (<c>\n</c>, <c>{...}</c>) e por isso ficaram de fora.</param>
/// <param name="Skipped">Linhas com ocorrência deixadas intactas porque o texto ou a troca quebraria a sintaxe dos marcadores.</param>
/// <param name="EmptyMatchLines">Linhas em que o padrão só casou texto vazio, que a substituição ignora.</param>
public sealed record ReplacePreview(
    SearchOptions Options,
    string Replacement,
    IReadOnlyList<ReplaceItem> Items,
    int ProtectedMatches,
    IReadOnlyList<SkippedLine> Skipped,
    int EmptyMatchLines)
{
    /// <summary>Total de ocorrências que a substituição troca.</summary>
    public int TotalReplacements => Items.Sum(i => i.Count);

    public int SkippedLines => Skipped.Count;

    /// <summary>Linhas puladas porque o texto delas já tinha um marcador malformado.</summary>
    public int MalformedLines => Skipped.Count(s => s.Reason == SkipReason.MalformedMarkers);

    /// <summary>Linhas puladas porque a troca deixaria um marcador malformado.</summary>
    public int BrokenByReplacementLines => Skipped.Count(s => s.Reason == SkipReason.ReplacementBreaksMarkers);

    /// <summary>Linhas <see cref="TranslationStatus.Approved"/> que perdem a aprovação ao gravar.</summary>
    public int ApprovedLines => Items.Count(i => i.PreviousStatus == TranslationStatus.Approved);

    /// <summary>Linhas <see cref="TranslationStatus.Reviewed"/> que perdem a revisão ao gravar.</summary>
    public int ReviewedLines => Items.Count(i => i.PreviousStatus == TranslationStatus.Reviewed);

    public bool IsEmpty => Items.Count == 0;
}

/// <summary>O que <see cref="ProjectSearch.Apply"/> fez.</summary>
/// <param name="Applied">Linhas gravadas.</param>
/// <param name="Stale">Linhas da prévia que não foram gravadas porque a tradução mudou (ou a linha sumiu) depois da prévia.</param>
public sealed record ReplaceResult(int Applied, IReadOnlyList<ReplaceItem> Stale);

/// <summary>Busca recusada: texto vazio, expressão regular inválida ou lenta demais, ou pedido que a substituição não aceita.</summary>
public sealed class SearchException : VNForgeException
{
    public SearchException(string message)
        : base(message)
    {
    }

    public SearchException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
