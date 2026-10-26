using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// Quem recebe o andamento de uma operação longa. Sem terminal interativo é <see cref="Disabled"/> e não faz nada: o
/// log de sempre (progresso no erro padrão em <c>translate</c> e <c>review</c>) continua como estava.
/// </summary>
public sealed class ProgressSink
{
    private readonly ProgressTask? _task;
    private bool _completeOnFinish;

    private ProgressSink(ProgressTask? task) => _task = task;

    public static ProgressSink Disabled { get; } = new(null);

    /// <summary>A barra está na tela; os comandos usam para trocar o log do erro padrão pela barra.</summary>
    public bool IsLive => _task is not null;

    internal static ProgressSink For(ProgressTask task) => new(task);

    public void Report(double done, string detail)
    {
        if (_task is not { } task)
        {
            return;
        }

        task.Value = done;

        // O Spectre recusa descrição vazia.
        if (detail.Length > 0)
        {
            task.Description = detail;
        }
    }

    /// <summary>
    /// Andamento por script (<c>extract</c> e <c>build</c>); o primeiro relato fixa o total. O relato final (nome vazio) não completa a barra,
    /// porque ainda falta trabalho depois dos scripts: ela volta a girar com o texto <paramref name="finishing"/> e só completa em <see cref="Finish"/>.
    /// </summary>
    public void Scripts(ScriptWorkProgress progress, string finishing)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (_task is not { } task)
        {
            return;
        }

        if (!_completeOnFinish)
        {
            _completeOnFinish = true;
            task.MaxValue = Math.Max(progress.Total, 1);
            task.IsIndeterminate = false;
        }

        if (progress.Name.Length == 0)
        {
            task.IsIndeterminate = true;
            task.Description = finishing;
            return;
        }

        Report(progress.Done, $"{LiveProgressText.Number(progress.Done)}/{LiveProgressText.Number(progress.Total)} scripts  {progress.Name}");
    }

    /// <summary>O trabalho acabou sem erro: a barra por script (que ficou girando no fim) chega a 100%.</summary>
    internal void Finish()
    {
        if (_task is { } task && _completeOnFinish)
        {
            task.IsIndeterminate = false;
            task.Value = task.MaxValue;
        }
    }
}

/// <summary>Barra de progresso do Spectre, só em terminal interativo (<see cref="VnfCli.CanAnimate"/>); fora dele o trabalho roda sem barra.</summary>
public static class LiveProgress
{
    /// <param name="live">Desenha a barra; <c>false</c> roda <paramref name="work"/> com um <see cref="ProgressSink.Disabled"/>.</param>
    /// <param name="description">Texto da barra até o primeiro relato.</param>
    /// <param name="total">Total já conhecido, ou <c>null</c> para a barra indeterminada até o primeiro relato.</param>
    public static async Task<T> RunAsync<T>(IAnsiConsole console, bool live, string description, double? total, Func<ProgressSink, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(work);

        if (!live)
        {
            return await work(ProgressSink.Disabled).ConfigureAwait(false);
        }

        var result = default(T)!;
        await console.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new SpinnerColumn(Spinner.Known.Dots) { Style = new Style(CliPalette.Accent) },
                new ProgressBarColumn
                {
                    Width = 28,
                    CompletedStyle = new Style(CliPalette.Accent),
                    FinishedStyle = new Style(CliPalette.Approved),
                    RemainingStyle = new Style(CliPalette.Pending),
                    IndeterminateStyle = new Style(CliPalette.Accent),
                },
                new PercentageColumn { Style = new Style(CliPalette.Accent), CompletedStyle = new Style(CliPalette.Approved) },
                new DetailColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask(description, new ProgressTaskSettings { MaxValue = total ?? 1 });
                task.IsIndeterminate = total is null;
                var sink = ProgressSink.For(task);
                result = await work(sink).ConfigureAwait(false);
                sink.Finish();
            })
            .ConfigureAwait(false);
        return result;
    }

    private sealed class DetailColumn : ProgressColumn
    {
        protected override bool NoWrap => true;

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime) =>
            new Text(task.Description ?? string.Empty);
    }
}

/// <summary><see cref="IProgress{T}"/> que entrega na hora, na thread de quem relata (o <see cref="Progress{T}"/> do .NET posta para outra thread e perde a ordem).</summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/// <summary>A barra por lote de <c>translate</c> e <c>review</c>: o texto sai de <see cref="BatchProgressText"/> a cada lote que volta.</summary>
internal sealed class BatchReporter(string model, int totalLines, int batchSize, Func<ClaudeUsage?> usage)
{
    private readonly int _batches = BatchProgressText.BatchCount(totalLines, batchSize);
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private ProgressSink _sink = ProgressSink.Disabled;
    private int _batchesDone;

    /// <summary>A barra está na tela, então o log do erro padrão fica de fora.</summary>
    public bool IsLive => _sink.IsLive;

    public Task<T> RunAsync<T>(IAnsiConsole console, bool live, string description, Func<Task<T>> work) =>
        LiveProgress.RunAsync(console, live, description, totalLines, sink =>
        {
            _sink = sink;
            _clock.Restart();
            sink.Report(0, DetailFor(0));
            return work();
        });

    /// <summary>Chame depois de gravar cada lote, sob o lock do comando.</summary>
    public void BatchDone(int doneLines)
    {
        _batchesDone++;
        _sink.Report(doneLines, DetailFor(doneLines));
    }

    private string DetailFor(int doneLines)
    {
        var snapshot = usage();
        decimal? dollars = null;
        if (snapshot is not null)
        {
            try
            {
                dollars = RunCostTracker.DollarsFor(model, snapshot);
            }
            catch (ArgumentException)
            {
                // Modelo sem preço na tabela: o custo fica fora da linha em vez de aparecer como zero.
            }
        }

        return BatchProgressText.Detail(_batchesDone, _batches, doneLines, totalLines, snapshot, dollars, _clock.Elapsed);
    }
}

/// <summary>
/// A linha de andamento de <c>translate</c> e <c>review</c>: lote, linhas, tokens, custo acumulado e tempo restante. É texto puro, sem
/// markup, e recebe o tempo decorrido de fora, então os testes não dependem do relógio.
/// </summary>
public static class BatchProgressText
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static int BatchCount(int lines, int batchSize) => lines <= 0 ? 0 : (lines + batchSize - 1) / batchSize;

    public static string Detail(int batch, int batches, int doneLines, int totalLines, ClaudeUsage? usage, decimal? dollars, TimeSpan elapsed)
    {
        var parts = new List<string>
        {
            $"lote {LiveProgressText.Number(batch)}/{LiveProgressText.Number(batches)}",
            $"{LiveProgressText.Number(doneLines)}/{LiveProgressText.Number(totalLines)} linhas",
        };
        if (usage is not null)
        {
            parts.Add(AiCommandSupport.Tokens(usage.InputTokens + usage.OutputTokens));
        }

        if (dollars is { } cost)
        {
            parts.Add(AiCommandSupport.Dollars(cost));
        }

        if (doneLines < totalLines)
        {
            parts.Add(Remaining(elapsed, doneLines, totalLines) is { } left ? "~" + FormatRemaining(left) : "calculando o tempo");
        }

        return string.Join("  ", parts);
    }

    /// <summary>Tempo que falta pelo ritmo até agora; <c>null</c> enquanto nenhuma linha voltou (não há ritmo para medir).</summary>
    public static TimeSpan? Remaining(TimeSpan elapsed, int done, int total)
    {
        if (done <= 0)
        {
            return null;
        }

        if (done >= total)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)(elapsed.Ticks / (double)done * (total - done)));
    }

    /// <summary>"45 s", "2 min" ou "1 h 05 min"; arredonda para cima, para a estimativa nunca prometer menos do que falta.</summary>
    public static string FormatRemaining(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(span.TotalSeconds);
        if (seconds < 60)
        {
            return string.Create(PortugueseBrazil, $"{Math.Max(seconds, 1)} s");
        }

        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return minutes < 60
            ? string.Create(PortugueseBrazil, $"{minutes} min")
            : string.Create(PortugueseBrazil, $"{minutes / 60} h {minutes % 60:00} min");
    }
}

public static class LiveProgressText
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static string Number(int value) => value.ToString("N0", PortugueseBrazil);
}
