using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Screens;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;

namespace VNForge.App.Tests.Controls;

/// <summary>
/// A caixa do jogo nos casos do desenho: cabe com sobra (2 de 3 linhas), passa (4 linhas para 3, a quarta abaixo do traço) e duas páginas.
/// A grade de linhas sai de <see cref="GameTextbox.Describe"/>; o desenho é conferido renderizando nos dois temas.
/// </summary>
public class GameTextboxTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static TextboxLayout Caixa(int[] linhasPorPagina, int maximo = 3, string? falante = "Misaki")
    {
        var paginas = linhasPorPagina
            .Select(total => new TextboxBox([.. Enumerable.Range(0, total).Select(n =>
            {
                var texto = $"Você não vai acreditar {n + 1}";
                return new TextboxLine(texto, texto.Length, n >= maximo, [new LineSegment(texto, 0, texto.Length, false)]);
            })]))
            .ToArray();
        return new TextboxLayout(falante, 30, maximo, paginas);
    }

    private static GameTextboxRowKind[] Tipos(TextboxLayout layout) => [.. GameTextbox.Describe(layout).Select(r => r.Kind)];

    [Fact]
    public void Duas_de_tres_linhas_deixam_a_terceira_vaga()
    {
        var layout = Caixa([2]);

        Assert.Equal([GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant], Tipos(layout));
    }

    [Fact]
    public void Quatro_linhas_para_tres_levam_a_quarta_para_baixo_do_traco()
    {
        var layout = Caixa([4]);
        var grade = GameTextbox.Describe(layout);

        Assert.Equal([GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Overflow], Tipos(layout));
        Assert.Equal([false, false, false, true], grade.Select(r => r.FirstOverflow));
    }

    [Fact]
    public void Duas_paginas_ganham_um_separador_entre_elas()
    {
        var layout = Caixa([3, 2]);

        Assert.Equal(
            [
                GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text,
                GameTextboxRowKind.PageBreak,
                GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant,
            ],
            Tipos(layout));
        Assert.Equal(1, GameTextbox.Describe(layout).Single(r => r.Kind == GameTextboxRowKind.PageBreak).Page);
    }

    [Theory]
    [InlineData(560, 560, 1)]
    [InlineData(560, 800, 1)]
    [InlineData(560, 280, 0.5)]
    [InlineData(560, double.PositiveInfinity, 1)]
    [InlineData(560, 0, 1)]
    [InlineData(0, 300, 1)]
    public void A_escala_para_caber_nunca_amplia_e_ignora_largura_sem_limite(double conteudo, double disponivel, double esperado) =>
        Assert.Equal(esperado, GameTextbox.ScaleToFit(conteudo, disponivel), 6);

    [Fact]
    public async Task No_palco_da_revisao_a_moldura_leva_a_regua_de_14_35_px_e_o_texto_o_avanco_do_corpo_de_17_6_px()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var layout = Caixa([2], falante: null) with { Columns = 40 };
                var caixa = new GameTextbox { Layout = layout, Size = GameTextboxSize.Review, FontFamily = new Avalonia.Media.FontFamily("avares://VNForge.App/Assets/Fonts#JetBrains Mono") };
                caixa.Measure(Avalonia.Size.Infinity);

                // 40 colunas de 14,35 px mais 26 px de recuo de cada lado: os 626 px do quadro.
                Assert.Equal(626, caixa.DesiredSize.Width, 1);
                // O texto segue o avanço natural do JetBrains Mono em 17,6 px (0,6 em), bem abaixo da régua da moldura.
                var linha = caixa.RowBounds()[0].Bounds;
                Assert.Equal(40 * 17.6 * 0.6, linha.Width, 0);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Com_FitToWidth_a_caixa_de_73_colunas_encolhe_em_proporcao_e_sem_ele_mantem_o_tamanho()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var layout = Caixa([2]) with { Columns = 73 };
                var natural = new GameTextbox { Layout = layout };
                natural.Measure(Avalonia.Size.Infinity);
                var largura = natural.DesiredSize.Width / 2;
                var ajustada = new GameTextbox { Layout = layout, FitToWidth = true };
                ajustada.Measure(new Avalonia.Size(largura, double.PositiveInfinity));
                var folgada = new GameTextbox { Layout = layout, FitToWidth = true };
                folgada.Measure(new Avalonia.Size(natural.DesiredSize.Width + 40, double.PositiveInfinity));

                Assert.True(natural.DesiredSize.Width > 0);
                Assert.Equal(1, natural.FitScale);
                Assert.Equal(largura, ajustada.DesiredSize.Width, 3);
                Assert.Equal(0.5, ajustada.FitScale, 3);
                Assert.True(ajustada.FitScale is > 0 and < 1);
                Assert.Equal(natural.DesiredSize.Height * ajustada.FitScale, ajustada.DesiredSize.Height, 3);
                Assert.Equal(1, folgada.FitScale);
                Assert.Equal(natural.DesiredSize.Width, folgada.DesiredSize.Width, 3);
                Assert.All(ajustada.RowBounds(), r => Assert.True(r.Bounds.Right <= ajustada.DesiredSize.Width + 0.5));
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_altura_acompanha_a_grade_nos_dois_temas(string tema)
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var cabe = Medir(Caixa([2]), tema);
                var passa = Medir(Caixa([4]), tema);
                var paginas = Medir(Caixa([3, 2]), tema);
                var semNome = Medir(Caixa([2], falante: null), tema);

                Assert.True(cabe.Height > 0 && cabe.Width > 0);
                Assert.True(passa.Height > cabe.Height, "a quarta linha, abaixo do traço, aumenta a caixa");
                Assert.True(paginas.Height > passa.Height, "a segunda página e o separador aumentam a caixa");
                Assert.True(cabe.Height > semNome.Height, "a placa do nome sai para fora do alto da caixa");
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Triangulo_fica_parado_quando_o_Windows_pede_menos_movimento()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                using var _ = MotionPreferences.Force(false);
                var caixa = new GameTextbox { Layout = Caixa([2]) };
                var janela = new Window { Width = 500, Height = 300, Content = caixa };
                Motion.Follow(janela);
                try
                {
                    janela.Show();
                    for (var n = 0; n < 6; n++)
                    {
                        SpinWait.SpinUntil(static () => false, 60);
                        ScreenHost.Settle(janela);
                    }

                    Assert.DoesNotContain(Motion.MotionClass, janela.Classes);
                    Assert.Equal(0, caixa.NextOffset);
                }
                finally
                {
                    janela.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Triangulo_oscila_quando_o_movimento_esta_ligado()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                using var _ = MotionPreferences.Force(true);
                var caixa = new GameTextbox { Layout = Caixa([2]) };
                var janela = new Window { Width = 500, Height = 300, Content = caixa };
                Motion.Follow(janela);
                try
                {
                    janela.Show();
                    var moveu = false;
                    for (var n = 0; n < 40 && !moveu; n++)
                    {
                        SpinWait.SpinUntil(static () => false, 60);
                        ScreenHost.Settle(janela);
                        moveu = caixa.NextOffset > 0;
                    }

                    Assert.Contains(Motion.MotionClass, janela.Classes);
                    Assert.True(moveu, "o triângulo deveria ter saído da posição inicial");
                }
                finally
                {
                    janela.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    private static Avalonia.Size Medir(TextboxLayout layout, string tema, GameTextboxSize tamanho = GameTextboxSize.Medium)
    {
        var caixa = new GameTextbox { Layout = layout, Size = tamanho };
        var janela = new Window { Width = 800, Height = 600, RequestedThemeVariant = Variant(tema), Content = caixa };
        try
        {
            janela.Show();
            ScreenHost.Settle(janela);
            return caixa.DesiredSize;
        }
        finally
        {
            janela.Close();
        }
    }
}
