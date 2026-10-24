using Spectre.Console;
using VNForge.Cli;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class CliViewsTests
{
    // Pendente, IA, traduzida, revisada, aprovada: a ordem do StatusCounts do Core.
    private static readonly StatusCounts Counts = new(Untranslated: 124, Machine: 89, Translated: 70, Reviewed: 36, Approved: 34);

    [Theory]
    [InlineData(40)]
    [InlineData(47)]
    [InlineData(10)]
    public void Barra_ocupa_exatamente_a_largura(int width)
    {
        var cells = DistributionBar.Allocate([34, 36, 70, 89, 124], width);

        Assert.Equal(width, cells.Sum());
    }

    [Fact]
    public void Barra_da_o_proporcional_a_cada_status()
    {
        var cells = DistributionBar.Allocate([50, 0, 0, 0, 50], 20);

        Assert.Equal([10, 0, 0, 0, 10], cells);
    }

    [Fact]
    public void Status_com_poucas_linhas_ainda_aparece_na_barra()
    {
        var cells = DistributionBar.Allocate([1, 0, 0, 0, 999], 20);

        Assert.True(cells[0] >= 1);
        Assert.Equal(20, cells.Sum());
    }

    [Fact]
    public void Barra_sem_linhas_fica_toda_pendente()
    {
        var cells = DistributionBar.Allocate([0, 0, 0, 0, 0], 12);

        Assert.Equal([0, 0, 0, 0, 12], cells);
    }

    [Fact]
    public void Barra_em_texto_simples_usa_um_glifo_por_status_e_nenhum_ansi()
    {
        var harness = ConsoleHarness.Plain();

        var text = harness.Render(DistributionBar.Create(Counts, 47)).TrimEnd();

        Assert.Equal(47, text.Length);
        Assert.False(text.Contains('\u001b'));
        Assert.Equal(5, text.Distinct().Count());
        // Aprovadas primeiro, pendentes por último, como na barra do app.
        Assert.Equal('█', text[0]);
        Assert.Equal('·', text[^1]);
    }

    [Fact]
    public void Barra_colorida_usa_a_cor_de_cada_status()
    {
        var harness = ConsoleHarness.Rich();

        var text = harness.Render(DistributionBar.Create(Counts, 47));

        Assert.Contains("38;2;95;191;127m", text); // aprovada
        Assert.Contains("38;2;62;154;174m", text); // revisada
        Assert.Contains("38;2;110;143;203m", text); // traduzida
        Assert.Contains("38;2;154;127;214m", text); // IA
        Assert.Contains("38;2;58;66;80m", text); // pendente
    }

    [Fact]
    public void Legenda_traz_nome_e_contagem_de_cada_status()
    {
        var harness = ConsoleHarness.Plain(width: 120);

        var text = harness.Render(DistributionBar.Legend(Counts));

        Assert.Contains("Aprovadas 34", text);
        Assert.Contains("Revisadas 36", text);
        Assert.Contains("Traduzidas 70", text);
        Assert.Contains("IA 89", text);
        Assert.Contains("Pendentes 124", text);
    }

    [Fact]
    public void Tabela_em_texto_simples_tem_cabecalho_e_cabe_na_largura()
    {
        var harness = ConsoleHarness.Plain(width: 50);
        var table = CliViews.NewTable(CliViews.Column("Script"), CliViews.Column("Nome"), CliViews.Column("Linhas", right: true));
        table.AddRow("yst00001", "Prólogo", "49");
        table.AddRow("yst00002", "Um nome de capítulo bem comprido que precisa quebrar linha na tela", "58");

        harness.Console.Write(table);

        Assert.False(harness.Output.Contains('\u001b'));
        Assert.Contains("Script", harness.Lines[0]);
        Assert.Contains("Linhas", harness.Lines[0]);
        Assert.All(harness.Lines, line => Assert.True(line.Length <= 50, line));
        Assert.Contains(harness.Lines, l => l.Contains("yst00002", StringComparison.Ordinal));
    }

    [Fact]
    public void Tabela_alinha_numeros_a_direita()
    {
        var harness = ConsoleHarness.Plain(width: 50);
        var table = CliViews.NewTable(CliViews.Column("Nome"), CliViews.Column("Linhas", right: true));
        table.AddRow("a", "5");
        table.AddRow("b", "1.254");

        harness.Console.Write(table);

        var five = harness.Lines.Single(l => l.StartsWith('a')).TrimEnd();
        var big = harness.Lines.Single(l => l.StartsWith('b')).TrimEnd();
        Assert.Equal(five.Length, big.Length);
    }

    [Fact]
    public void Painel_mostra_titulo_e_conteudo_na_largura_fixa()
    {
        var harness = ConsoleHarness.Plain(width: 40);

        harness.Console.Write(CliViews.NewPanel("Resultado", new Markup("34 scripts reconstruídos")));

        Assert.Contains("Resultado", harness.Lines[0]);
        Assert.Contains(harness.Lines, l => l.Contains("34 scripts reconstruídos", StringComparison.Ordinal));
        Assert.All(harness.Lines, line => Assert.True(line.Length <= 40, line));
    }

    [Fact]
    public void Painel_colorido_usa_o_destaque_do_app()
    {
        var harness = ConsoleHarness.Rich(width: 40);

        harness.Console.Write(CliViews.NewPanel("Resultado", new Markup("ok")));

        Assert.Contains("38;2;141;176;234m", harness.Output);
    }

    [Fact]
    public void Texto_do_usuario_com_colchetes_nao_vira_markup()
    {
        var harness = ConsoleHarness.Plain();

        harness.Console.MarkupLine(CliViews.Escape("[IA] sugestão"));

        Assert.Equal("[IA] sugestão", harness.Lines[0]);
    }
}
