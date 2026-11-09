using System.Globalization;

namespace VNForge.Core.Eval;

/// <summary>
/// Uma configuração do banco de provas: o que muda de uma tradução para outra. <see cref="Id"/> é a letra (A, B, C...) que a tabela e o gráfico usam.
/// O modelo e o esforço guardam o texto do projeto (<c>claude-sonnet-5-5</c>, <c>medium</c>): o Core não conhece o provedor de IA.
/// </summary>
/// <param name="ContextLines">Quantas falas antes e depois da linha entram no pedido.</param>
/// <param name="WithBible">Se o resumo da cena (a bíblia do jogo) entra no pedido.</param>
public sealed record EvalConfig(string Id, string Model, string Effort, int ContextLines, bool WithBible)
{
    /// <summary>O modelo como a pessoa o lê: <c>claude-opus-5-5</c> vira <c>Opus 5.5</c>.</summary>
    public string ModelName => ModelLabel(Model);

    /// <summary>O esforço em português: <c>effort alto</c>.</summary>
    public string EffortText => $"effort {EffortLabel(Effort)}";

    public string BibleText => WithBible ? "com bíblia" : "sem bíblia";

    public string ContextText => ContextLines == 1 ? "1 fala de contexto" : $"{ContextLines} falas de contexto";

    /// <summary>Uma linha só: <c>Opus 5.5 · effort alto · com bíblia</c>.</summary>
    public string Title => $"{ModelName} · {EffortText} · {BibleText}";

    /// <summary>O que a tabela escreve embaixo do modelo: <c>effort alto · 15 falas · com bíblia</c>.</summary>
    public string Detail => $"{EffortText} · {ContextLines.ToString(CultureInfo.InvariantCulture)} {(ContextLines == 1 ? "fala" : "falas")} · {BibleText}";

    public static string ModelLabel(string model)
    {
        var parts = (model.StartsWith("claude-", StringComparison.Ordinal) ? model[7..] : model).Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return model;
        }

        var name = char.ToUpperInvariant(parts[0][0]) + parts[0][1..];
        return parts.Length == 1 ? name : $"{name} {string.Join('.', parts[1..])}";
    }

    public static string EffortLabel(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => "baixo",
        "medium" => "médio",
        "high" => "alto",
        var other => other,
    };

    /// <summary>A letra da configuração de posição <paramref name="index"/> (0 vira A).</summary>
    public static string LetterFor(int index) => ((char)('A' + index)).ToString();
}

/// <summary>Uma linha do conjunto ouro: o original e a tradução que a pessoa aprovou, com as falas ao redor para a rodada às cegas.</summary>
/// <param name="Before">A fala anterior, com a tradução aprovada (<c>Kenta, "Ehehe..."</c>); vazia no começo do script.</param>
/// <param name="After">A fala seguinte, no original; vazia no fim do script.</param>
public sealed record GoldLine(string Id, string ScriptKey, string? Speaker, string Source, string Reference, string Before = "", string After = "");

/// <summary>O resultado de uma linha numa configuração.</summary>
/// <param name="Refused">O modelo recusou a linha ou o lote dela falhou; a tradução fica vazia e o chrF vale 0.</param>
/// <param name="JudgeScore">A nota do juiz de IA, de 0 a 10; nula quando o juiz não rodou ou não avaliou a linha.</param>
public sealed record EvalLineResult(string LineId, string Translation, double Chrf, double? JudgeScore = null, bool Refused = false);

/// <summary>O que uma configuração fez no conjunto da rodada: o texto de cada linha, os tokens, o custo e o tempo.</summary>
/// <param name="Dollars">O custo das traduções, em dólar, com o preço da tabela; o do juiz fica em <paramref name="JudgeDollars"/>.</param>
/// <param name="Seconds">O tempo que a tradução das linhas levou, com o juiz de fora.</param>
/// <param name="Error">O primeiro erro de lote que a configuração teve; as linhas desse lote entram como recusadas.</param>
public sealed record EvalConfigReport(
    EvalConfig Config,
    IReadOnlyList<EvalLineResult> Lines,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal Dollars,
    decimal JudgeDollars,
    double Seconds,
    string? Error = null)
{
    /// <summary>O chrF médio das linhas, de 0 a 100; linha recusada conta como 0.</summary>
    public double MeanChrf => Lines.Count == 0 ? 0 : Lines.Average(l => l.Chrf);

    /// <summary>A nota média do juiz, de 0 a 10; nula quando nenhuma linha foi avaliada.</summary>
    public double? MeanJudge
    {
        get
        {
            var scores = Lines.Where(l => l.JudgeScore is not null).Select(l => l.JudgeScore!.Value).ToList();
            return scores.Count == 0 ? null : scores.Average();
        }
    }

    public int RefusedLines => Lines.Count(l => l.Refused);

    /// <summary>O custo, em dólar, de traduzir <paramref name="lines"/> linhas na mesma configuração, pelo custo por linha medido.</summary>
    public decimal ProjectedDollars(int lines) => Lines.Count == 0 ? 0m : Dollars / Lines.Count * lines;

    /// <summary>O mesmo custo pela Message Batches API, que cobra metade.</summary>
    public decimal ProjectedBatchDollars(int lines) => ProjectedDollars(lines) / 2m;

    /// <summary>O tempo de traduzir <paramref name="lines"/> linhas, pelo tempo por linha medido.</summary>
    public TimeSpan ProjectedTime(int lines) => Lines.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Seconds / Lines.Count * lines);
}

/// <summary>Uma linha sorteada para a rodada, com o que a rodada às cegas mostra dela.</summary>
public sealed record EvalSample(string LineId, string ScriptKey, string? Speaker, string Source, string Reference, string Before, string After);

/// <summary>Um par da rodada às cegas: a mesma linha traduzida por duas configurações. <paramref name="First"/> aparece como "Texto A" e <paramref name="Second"/> como "Texto B".</summary>
public sealed record BlindPair(int Index, string LineId, string First, string Second);

public enum BlindChoice
{
    /// <summary>"A é melhor".</summary>
    First,

    /// <summary>"B é melhor".</summary>
    Second,

    /// <summary>"Empate".</summary>
    Tie,

    /// <summary>"Nenhum dos dois".</summary>
    Neither,

    /// <summary>"Pular par": o par fica sem resposta e não conta para ninguém.</summary>
    Skipped,
}

public sealed record BlindAnswer(int Index, BlindChoice Choice);

/// <summary>
/// Uma rodada do banco de provas, gravada em <c>.cache/eval/rounds/</c>: o conjunto sorteado, o relatório de cada configuração, os pares da rodada às cegas
/// com as respostas e a configuração que a pessoa escolheu para o M4.
/// </summary>
/// <param name="GoldLines">Quantas linhas aprovadas o projeto tinha quando a rodada rodou.</param>
/// <param name="RemainingLines">Quantas linhas ainda esperam tradução; o custo e o tempo do jogo todo valem para elas.</param>
/// <param name="JudgeModel">O modelo do juiz de IA; nulo quando o juiz não rodou.</param>
/// <param name="ChosenConfigId">A letra da configuração escolhida para o M4; nula enquanto ninguém escolheu.</param>
public sealed record EvalRound(
    int Version,
    string Id,
    DateTimeOffset At,
    int Seed,
    int GoldLines,
    int RemainingLines,
    string? JudgeModel,
    IReadOnlyList<EvalSample> Sample,
    IReadOnlyList<EvalConfigReport> Reports,
    IReadOnlyList<BlindPair> Pairs,
    IReadOnlyList<BlindAnswer> Answers,
    string? ChosenConfigId = null)
{
    public const int CurrentVersion = 1;

    /// <summary>Os pares que a pessoa respondeu (pular não conta).</summary>
    public int PairsDone => Answers.Count(a => a.Choice != BlindChoice.Skipped);

    /// <summary>O próximo par sem resposta; nulo quando todos foram respondidos ou pulados.</summary>
    public BlindPair? NextPair => Pairs.FirstOrDefault(p => Answers.All(a => a.Index != p.Index));

    /// <summary>Quantos pares a configuração disputou e em quantos a pessoa preferiu o texto dela. Empate e "nenhum dos dois" disputam sem vencer.</summary>
    public (int Wins, int Total) BlindScore(string configId)
    {
        int wins = 0, total = 0;
        foreach (var answer in Answers.Where(a => a.Choice != BlindChoice.Skipped))
        {
            var pair = Pairs.FirstOrDefault(p => p.Index == answer.Index);
            if (pair is null || (pair.First != configId && pair.Second != configId))
            {
                continue;
            }

            total++;
            if ((answer.Choice == BlindChoice.First && pair.First == configId) || (answer.Choice == BlindChoice.Second && pair.Second == configId))
            {
                wins++;
            }
        }

        return (wins, total);
    }

    /// <summary>Quantos pares a configuração <paramref name="winner"/> venceu contra <paramref name="other"/>, e quantos os dois disputaram.</summary>
    public (int Wins, int Total) HeadToHead(string winner, string other)
    {
        int wins = 0, total = 0;
        foreach (var answer in Answers.Where(a => a.Choice != BlindChoice.Skipped))
        {
            var pair = Pairs.FirstOrDefault(p => p.Index == answer.Index);
            if (pair is null || !((pair.First == winner && pair.Second == other) || (pair.First == other && pair.Second == winner)))
            {
                continue;
            }

            total++;
            if ((answer.Choice == BlindChoice.First && pair.First == winner) || (answer.Choice == BlindChoice.Second && pair.Second == winner))
            {
                wins++;
            }
        }

        return (wins, total);
    }

    /// <summary>Grava a resposta do par; responder de novo troca a resposta anterior.</summary>
    public EvalRound WithAnswer(int pairIndex, BlindChoice choice)
    {
        if (Pairs.All(p => p.Index != pairIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(pairIndex), $"A rodada não tem o par {pairIndex}.");
        }

        return this with { Answers = [.. Answers.Where(a => a.Index != pairIndex), new BlindAnswer(pairIndex, choice)] };
    }

    public EvalRound WithChosen(string? configId)
    {
        if (configId is not null && Reports.All(r => r.Config.Id != configId))
        {
            throw new ArgumentException($"A rodada não tem a configuração {configId}.", nameof(configId));
        }

        return this with { ChosenConfigId = configId };
    }
}
