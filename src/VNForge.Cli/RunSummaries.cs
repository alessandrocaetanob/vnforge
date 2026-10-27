using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// O que a CLI desenha no terminal interativo ao fim de <c>extract</c> e <c>build</c> (painel de resumo) e antes da confirmação de
/// <c>inject</c> e <c>restore</c> (tabela do que vai mudar). Cada função devolve um <see cref="IRenderable"/>, e o texto não leva cor
/// fixa: os testes imprimem num console sem ANSI e comparam as linhas.
/// </summary>
public static class RunSummaries
{
    private const string Dash = "-";

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static IRenderable ExtractPanel(ExtractionResult result, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(result);

        var grid = NewGrid();
        grid.AddRow(Label("Scripts"), Value(Number(result.Scripts.Count)));
        var kinds = ExtractCommand.KindsText(result);
        grid.AddRow(Label("Linhas"), Value(kinds.Length == 0 ? Number(result.TotalLines) : $"{Number(result.TotalLines)} ({kinds})"));
        foreach (var archive in result.Archives)
        {
            var size = SizeOf(Path.Combine(result.GameDirectory, archive.GamePath));
            var files = $"{Number(archive.FileCount)} {(archive.FileCount == 1 ? "arquivo" : "arquivos")}";
            grid.AddRow(Label(archive.GamePath), Value(size is { } bytes ? $"{FormatBytes(bytes)} · {files}" : files));
        }

        if (result.GameTitle is { } gameTitle)
        {
            grid.AddRow(Label("Título do jogo"), Value(gameTitle));
        }

        if (result.Reextraction is { } r)
        {
            grid.AddRow(Label("Re-extração"), Value($"{Number(r.Kept)} mantidas · {Number(r.Outdated)} desatualizadas · {Number(r.Added)} novas · {Number(r.Removed)} removidas"));
        }

        if (result.Warnings.Count > 0)
        {
            grid.AddRow(Label("Avisos"), Value(Number(result.Warnings.Count)));
        }

        grid.AddRow(Label("Duração"), Value(FormatDuration(elapsed)));
        return CliViews.NewPanel("Resultado da extração", grid);
    }

    public static IRenderable BuildPanel(BuildResult result, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(result);

        var grid = NewGrid();
        var scripts = $"{Number(result.RebuiltScripts.Count)} reconstruídos de {Number(result.ScriptCount)}";
        grid.AddRow(Label("Scripts"), Value(result.ReusedScripts > 0 ? $"{scripts} · {Number(result.ReusedScripts)} reaproveitados" : scripts));
        grid.AddRow(Label("Linhas"), Value($"{Number(result.TotalLines)} trocadas"));
        foreach (var gamePath in result.Archives.Select(archive => archive.GamePath))
        {
            var built = SizeOf(Path.Combine(result.BuildDirectory, gamePath));
            var original = SizeOf(Path.Combine(result.GameDirectory, gamePath));
            var text = built is { } size ? FormatBytes(size) : "não gravado";
            grid.AddRow(Label(gamePath), Value(original is { } before ? $"{text} (original {FormatBytes(before)})" : text));
        }

        var replaced = result.Encoding.Replaced.Count;
        var swaps = replaced switch
        {
            0 => "nenhum caractere trocado",
            1 => "1 caractere trocado",
            _ => $"{Number(replaced)} caracteres trocados",
        };
        var accents = $"{result.Encoding.Strategy} · {swaps}";
        grid.AddRow(Label("Acentuação"), Value(accents));
        if (result.GameTitle is { } gameTitle)
        {
            grid.AddRow(Label("Título do jogo"), Value(gameTitle));
        }

        grid.AddRow(Label("Duração"), Value(FormatDuration(elapsed)));
        return CliViews.NewPanel("Resultado do build", grid);
    }

    /// <summary>Arquivos que o <c>inject</c> grava no jogo: o que acontece com cada um e o tamanho de antes e de depois.</summary>
    public static IRenderable InjectTable(InjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var table = CliViews.NewTable(
            CliViews.Column("Arquivo"), CliViews.Column("O que acontece"), CliViews.Column("Atual", right: true), CliViews.Column("Depois", right: true));
        foreach (var file in plan.Files)
        {
            table.AddRow(
                CliViews.Escape(file.RelativePath),
                file.ReplacesExisting ? "substitui o original" : "arquivo novo",
                SizeText(file.TargetPath),
                SizeText(file.BuildPath));
        }

        return WithHeader($"Jogo: {plan.GameDirectory}", table, plan.UnchangedCount > 0 ? $"{Number(plan.UnchangedCount)} arquivo(s) de build/ já estão iguais no jogo e ficam de fora." : null);
    }

    /// <summary>Arquivos que o <c>restore</c> devolve ao jogo: o original do backup que volta, ou o arquivo que a injeção criou e sai.</summary>
    public static IRenderable RestoreTable(RestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var table = CliViews.NewTable(
            CliViews.Column("Arquivo"), CliViews.Column("O que acontece"), CliViews.Column("Atual", right: true), CliViews.Column("Depois", right: true));
        foreach (var entry in plan.Manifest.Files)
        {
            var backup = plan.BackupDirectories.TryGetValue(entry.Path, out var directory) ? directory : plan.BackupDirectory;
            var current = Path.Combine(plan.GameDirectory, entry.Path);
            var (action, after) = entry.Existed
                ? ("volta ao original", SizeText(Path.Combine(backup, entry.Path)))
                : CreatedByInjection(current, entry);
            table.AddRow(CliViews.Escape(entry.Path), action, SizeText(current), after);
        }

        return WithHeader($"Backup: {plan.BackupDirectory}{Environment.NewLine}Jogo: {plan.GameDirectory}", table, null);
    }

    /// <summary>
    /// Arquivo que a injeção criou: o <c>restore</c> só apaga se o conteúdo ainda é o injetado, mantém se mudou depois e não faz
    /// nada se já não existe (mesma regra do <see cref="GameInjector.Restore"/>).
    /// </summary>
    private static (string Action, string After) CreatedByInjection(string current, BackupEntry entry)
    {
        if (!File.Exists(current))
        {
            return ("nada a fazer (já não existe)", Dash);
        }

        return HashOf(current) == entry.InjectedSha256
            ? ("removido (criado pela injeção)", Dash)
            : ("mantido (alterado depois da injeção)", SizeText(current));
    }

    private static string? HashOf(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"512 B", "3,4 KB", "41,2 MB" ou "1,3 GB", com vírgula decimal.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(PortugueseBrazil, $"{bytes} B")
            : string.Create(PortugueseBrazil, $"{value:0.0} {units[unit]}");
    }

    /// <summary>"6,8 s" até um minuto, depois "1 min 12 s" e "1 h 05 min".</summary>
    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
        {
            return string.Create(PortugueseBrazil, $"{elapsed.TotalSeconds:0.0} s");
        }

        if (elapsed.TotalMinutes < 60)
        {
            return string.Create(PortugueseBrazil, $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds:00} s");
        }

        return string.Create(PortugueseBrazil, $"{(int)elapsed.TotalHours} h {elapsed.Minutes:00} min");
    }

    private static Grid NewGrid()
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        return grid;
    }

    private static Markup Label(string text) => new($"[{CliPalette.Hex(CliPalette.Accent)}]{CliViews.Escape(text)}[/]");

    private static Text Value(string text) => new(text);

    private static Rows WithHeader(string header, Table table, string? footer)
    {
        var rows = new List<IRenderable> { new Text(header), table };
        if (footer is not null)
        {
            rows.Add(new Text(footer));
        }

        return new Rows(rows);
    }

    private static string SizeText(string path) => SizeOf(path) is { } bytes ? FormatBytes(bytes) : Dash;

    private static long? SizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);
}
