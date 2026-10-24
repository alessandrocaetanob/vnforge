using Spectre.Console;
using VNForge.Cli;

namespace VNForge.Cli.Tests;

public sealed class RichOutputTests : IDisposable
{
    private const string Escape = "\u001b[";
    private const string ApprovedTrueColor = "38;2;95;191;127";
    private const string Saida = "saida";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static GlobalSettings Settings(bool json = false, bool quiet = false, bool noColor = false) =>
        new(null, json, false, quiet, false, noColor);

    [Fact]
    public void Terminal_interativo_sem_opcoes_usa_cor()
    {
        Assert.True(CliConsole.IsRich(Settings(), CliTerminal.TrueColor(80)));
    }

    [Fact]
    public void Saida_redirecionada_e_texto_simples()
    {
        Assert.False(CliConsole.IsRich(Settings(), CliTerminal.Plain(80)));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Json_quiet_e_no_color_desligam_a_cor_no_terminal_interativo(bool json, bool quiet, bool noColor)
    {
        Assert.False(CliConsole.IsRich(Settings(json, quiet, noColor), CliTerminal.TrueColor(80)));
    }

    [Fact]
    public void Variavel_NO_COLOR_desliga_a_cor()
    {
        var terminal = CliTerminal.TrueColor(80) with { NoColorEnvironment = true };

        Assert.False(CliConsole.IsRich(Settings(), terminal));
    }

    [Fact]
    public void Console_rico_escreve_ansi_com_a_cor_da_paleta()
    {
        var harness = ConsoleHarness.Rich();

        harness.Console.MarkupLine($"[{CliPalette.Hex(CliPalette.Approved)}]ok[/]");

        Assert.Contains(Escape + ApprovedTrueColor + "m", harness.Output);
    }

    [Fact]
    public void Console_simples_descarta_ansi_e_mantem_o_texto()
    {
        var harness = ConsoleHarness.Plain();

        harness.Console.MarkupLine($"[{CliPalette.Hex(CliPalette.Approved)}]ok[/]");

        Assert.DoesNotContain(Escape, harness.Output);
        Assert.Equal("ok", harness.Lines[0]);
    }

    [Fact]
    public void Console_simples_respeita_a_largura_fixa()
    {
        var harness = ConsoleHarness.Plain(width: 40);

        Assert.Equal(40, harness.Console.Profile.Width);
    }

    [Fact]
    public void Paleta_e_a_do_app()
    {
        Assert.Equal("#5FBF7F", CliPalette.Hex(CliPalette.Approved));
        Assert.Equal("#3E9AAE", CliPalette.Hex(CliPalette.Reviewed));
        Assert.Equal("#6E8FCB", CliPalette.Hex(CliPalette.Translated));
        Assert.Equal("#9A7FD6", CliPalette.Hex(CliPalette.Ai));
        Assert.Equal("#3A4250", CliPalette.Hex(CliPalette.Pending));
        Assert.Equal("#F2C46D", CliPalette.Hex(CliPalette.Warning));
        Assert.Equal("#F59AB2", CliPalette.Hex(CliPalette.Error));
        Assert.Equal("#8DB0EA", CliPalette.Hex(CliPalette.Accent));
    }

    [Fact]
    public async Task No_color_e_uma_opcao_global_que_chega_ao_comando()
    {
        GlobalSettings? seen = null;
        _cli.AddCommand(Saida, parseResult =>
        {
            seen = _cli.Cli.Options.Read(parseResult);
            return 0;
        });

        var result = await _cli.RunAsync([Saida, "--no-color"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.True(seen?.NoColor);
    }

    [Fact]
    public async Task Ajuda_lista_no_color()
    {
        var result = await _cli.RunAsync(["--help"], TestContext.Current.CancellationToken);

        Assert.Contains("--no-color", result.Output);
    }

    [Fact]
    public async Task Comando_escreve_pelo_console_da_cli_no_mesmo_writer()
    {
        _cli.AddCommand(Saida, _ =>
        {
            _cli.Cli.Ansi.MarkupLine("[#5FBF7F]feito[/]");
            return 0;
        });

        var result = await _cli.RunAsync([Saida], TestContext.Current.CancellationToken);

        Assert.Equal("feito" + Environment.NewLine, result.Output);
        Assert.False(_cli.Cli.RichOutput);
    }

    [Fact]
    public async Task Terminal_interativo_colore_a_saida_do_comando()
    {
        using var rich = new CliHarness(terminal: CliTerminal.TrueColor(100));
        rich.AddCommand(Saida, _ =>
        {
            rich.Cli.Ansi.MarkupLine("[#5FBF7F]feito[/]");
            return 0;
        });

        var result = await rich.RunAsync([Saida], TestContext.Current.CancellationToken);

        Assert.True(rich.Cli.RichOutput);
        Assert.Contains(Escape + ApprovedTrueColor + "m", result.Output);
    }

    [Theory]
    [InlineData("--no-color")]
    [InlineData("--json")]
    [InlineData("--quiet")]
    public async Task Opcao_global_tira_a_cor_mesmo_em_terminal_interativo(string option)
    {
        using var rich = new CliHarness(terminal: CliTerminal.TrueColor(100));
        rich.AddCommand(Saida, _ =>
        {
            rich.Cli.Ansi.MarkupLine("[#5FBF7F]feito[/]");
            return 0;
        });

        var result = await rich.RunAsync([Saida, option], TestContext.Current.CancellationToken);

        Assert.False(rich.Cli.RichOutput);
        Assert.DoesNotContain(Escape, result.Output);
        Assert.Contains("feito", result.Output);
    }

    [Fact]
    public async Task Writer_que_nao_e_o_console_do_processo_nunca_e_interativo()
    {
        using var plain = new CliHarness();
        plain.AddCommand(Saida, _ => 0);

        await plain.RunAsync([Saida], TestContext.Current.CancellationToken);

        Assert.False(CliTerminal.ForWriter(new StringWriter()).IsInteractive);
        Assert.False(plain.Cli.RichOutput);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ForWriter_le_NO_COLOR_do_ambiente(string? value, bool expected)
    {
        var terminal = CliTerminal.ForWriter(new StringWriter(), name => name == "NO_COLOR" ? value : null);

        Assert.Equal(expected, terminal.NoColorEnvironment);
    }

    [Fact]
    public void Console_do_ansi_nao_recebe_prompts()
    {
        var harness = ConsoleHarness.Plain();

        Assert.False(harness.Console.Profile.Capabilities.Interactive);
        Assert.Equal(ColorSystem.NoColors, harness.Console.Profile.Capabilities.ColorSystem);
    }
}
