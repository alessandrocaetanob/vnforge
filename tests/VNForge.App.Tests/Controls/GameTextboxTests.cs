using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
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

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Duas_de_tres_linhas_pintam_tinta_nas_duas_primeiras_pontilham_a_terceira_e_fecham_com_o_triangulo(string tema)
    {
        await fixture.Session.Dispatch(
            () => Desenhar(Caixa([2]), tema, (caixa, probe) =>
            {
                ConferirLinhas(caixa, probe, [GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant]);
                ConferirTriangulos(caixa, probe, 1);
            }),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Quatro_linhas_para_tres_pintam_a_quarta_na_cor_de_overflow_abaixo_do_traco(string tema)
    {
        await fixture.Session.Dispatch(
            () => Desenhar(Caixa([4]), tema, (caixa, probe) =>
            {
                ConferirLinhas(caixa, probe, [GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Overflow]);
                ConferirTriangulos(caixa, probe, 1);
            }),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Duas_paginas_pintam_o_separador_e_um_triangulo_ao_fim_de_cada_pagina(string tema)
    {
        await fixture.Session.Dispatch(
            () => Desenhar(Caixa([3, 2]), tema, (caixa, probe) =>
            {
                ConferirLinhas(
                    caixa,
                    probe,
                    [
                        GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text,
                        GameTextboxRowKind.PageBreak,
                        GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant,
                    ]);
                ConferirTriangulos(caixa, probe, 2);
            }),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Com_outra_fonte_os_glifos_mudam_mas_a_tinta_o_traco_e_o_triangulo_ficam_onde_a_grade_manda()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var proporcional = new Avalonia.Media.FontFamily("Segoe UI, Arial, sans-serif");
                Desenhar(Caixa([4]), "Dark", (caixa, probe) =>
                {
                    ConferirLinhas(caixa, probe, [GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Overflow]);
                    ConferirTriangulos(caixa, probe, 1);
                }, proporcional);
                Desenhar(Caixa([3, 2]), "Dark", (caixa, probe) => ConferirTriangulos(caixa, probe, 2), proporcional);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Os_dois_temas_desenham_os_mesmos_pixels_porque_os_tokens_da_caixa_nao_mudam()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                foreach (var layout in new[] { Caixa([2]), Caixa([4]), Caixa([3, 2]) })
                {
                    PixelProbe? escuro = null;
                    PixelProbe? claro = null;
                    Desenhar(layout, "Dark", (_, probe) => escuro = probe);
                    Desenhar(layout, "Light", (_, probe) => claro = probe);

                    Assert.True(escuro!.SameAs(claro!), "a caixa do jogo é a mesma nos dois temas");
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Tamanhos_pequeno_medio_e_grande_crescem_nessa_ordem(string tema)
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var altura = Enum.GetValues<GameTextboxSize>().ToDictionary(t => t, t => Medir(Caixa([2]), tema, t).Height);

                Assert.True(altura[GameTextboxSize.Small] < altura[GameTextboxSize.Medium]);
                Assert.True(altura[GameTextboxSize.Medium] < altura[GameTextboxSize.Large]);
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_os_tres_casos_em_PNG(string tema)
    {
        var variant = Variant(tema);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName("caixa-do-jogo", variant));
        await fixture.Session.Dispatch(
            () =>
            {
                var painel = new StackPanel { Spacing = 32, Margin = new Avalonia.Thickness(40), Width = 560 };
                foreach (var layout in new[] { Caixa([2]), Caixa([4]), Caixa([3, 2]) })
                {
                    painel.Children.Add(new GameTextbox { Layout = layout, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
                }

                painel.Children.Add(new GameTextbox { Layout = Caixa([2]), Size = GameTextboxSize.Large, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
                painel.Children.Add(new GameTextbox { Layout = Caixa([2]), Size = GameTextboxSize.Small, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
                ScreenRenderer.Render(new ScrollViewer { Content = painel }, variant, path, 700, 1500, window =>
                {
                    var caixas = window.GetVisualDescendants().OfType<GameTextbox>().ToArray();
                    Assert.All(caixas, c => Assert.True(c.Bounds.Height > 0));
                    var casos = new[]
                    {
                        new[] { GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant },
                        [GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Overflow],
                        [
                            GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text,
                            GameTextboxRowKind.PageBreak,
                            GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Vacant,
                        ],
                    };
                    foreach (var (caixa, esperadas) in caixas.Zip(casos))
                    {
                        ConferirLinhas(caixa, PixelProbe.Capture(window, caixa), esperadas);
                    }
                });
            },
            TestContext.Current.CancellationToken);

        Assert.True(new FileInfo(path).Length > 10_000);
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
                        ScreenRenderer.Settle(janela);
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
                        ScreenRenderer.Settle(janela);
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

    private const int Tinta = 24;
    private const int Triangulo = 10;
    private const int MinimoDeTinta = 3;

    private static int ColunaDoTriangulo(PixelProbe probe) => probe.Width - 26;

    private sealed record CoresDaCaixa(
        Avalonia.Media.Color Ink,
        Avalonia.Media.Color Overflow,
        Avalonia.Media.Color Triangle,
        IReadOnlyList<Avalonia.Media.Color> DottedSteps,
        IReadOnlyList<Avalonia.Media.Color> OverflowSteps,
        IReadOnlyList<Avalonia.Media.Color> SeparatorSteps);

    private static CoresDaCaixa Cores(Control host)
    {
        Avalonia.Media.Color Token(string key)
        {
            Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), key);
            return Assert.IsType<Avalonia.Media.ISolidColorBrush>(found, exactMatch: false).Color;
        }

        var (ink, overflow, background) = (Token("Vnf.TextboxInk"), Token("Vnf.TextboxOverflow"), Token("Vnf.TextboxBg"));
        return new CoresDaCaixa(
            ink,
            overflow,
            PixelProbe.Blend(ink, 0.8, background),
            PixelProbe.Steps(ink, 0.13, background),
            PixelProbe.Steps(overflow, 1, background),
            PixelProbe.Steps(ink, 0.22, background));
    }

    /// <summary>
    /// Confere o que foi pintado dentro do retângulo de cada linha, sem contar traços de glifo (que dependem da fonte): tinta nas linhas com texto, só a cor de overflow
    /// nas que passam, nada nas vagas além do pontilhado, o traço de overflow no espaço acima da primeira linha que passa e o separador entre as páginas.
    /// </summary>
    private static void ConferirLinhas(GameTextbox caixa, PixelProbe probe, GameTextboxRowKind[] esperadas)
    {
        var cores = Cores(caixa);
        var linhas = caixa.RowBounds();
        Assert.Equal(esperadas, linhas.Select(l => l.Row.Kind));

        foreach (var (row, area, gap) in linhas)
        {
            var tinta = probe.CountInArea(area, cores.Ink, Tinta);
            var estouro = probe.CountInArea(area, cores.Overflow, Tinta);
            switch (row.Kind)
            {
                case GameTextboxRowKind.Text:
                    Assert.True(tinta >= MinimoDeTinta, $"linha {row.Line} da página {row.Page + 1} sem tinta");
                    Assert.Equal(0, estouro);
                    Assert.True(probe.CountInArea(new Avalonia.Rect(area.X, area.Bottom - 2, area.Width, 3), cores.DottedSteps, 3) >= 10, "e a linha tem o pontilhado embaixo");
                    break;
                case GameTextboxRowKind.Vacant:
                    Assert.Equal(0, tinta);
                    Assert.Equal(0, estouro);
                    Assert.True(probe.CountInArea(new Avalonia.Rect(area.X, area.Bottom - 2, area.Width, 3), cores.DottedSteps, 3) >= 10, "a linha vaga só tem o pontilhado");
                    break;
                case GameTextboxRowKind.Overflow:
                    Assert.True(estouro >= MinimoDeTinta, "a linha que passa vai na cor de overflow");
                    Assert.Equal(0, tinta);
                    break;
                case GameTextboxRowKind.PageBreak:
                    Assert.True(probe.CountInArea(area, cores.SeparatorSteps, 4) >= 10, "o separador de página é um traço tracejado");
                    break;
            }

            if (gap > 0)
            {
                var traco = new Avalonia.Rect(area.X - 6, area.Y - gap, area.Width + 12, gap);
                Assert.True(probe.CountInArea(traco, cores.OverflowSteps, 10) >= area.Width / 4, "o traço de overflow fica acima da primeira linha que passa");
            }
        }
    }

    /// <summary>Um triângulo por página: o das páginas que viram dentro do separador e o último abaixo da última linha.</summary>
    private static void ConferirTriangulos(GameTextbox caixa, PixelProbe probe, int paginas)
    {
        var cores = Cores(caixa);
        var linhas = caixa.RowBounds();
        var triangulos = probe.ColumnRuns(ColunaDoTriangulo(probe), cores.Triangle, Triangulo);
        Assert.Equal(paginas, triangulos.Count);

        var separadores = linhas.Where(l => l.Row.Kind == GameTextboxRowKind.PageBreak).ToArray();
        for (var n = 0; n < separadores.Length; n++)
        {
            Assert.True(triangulos[n].Start >= separadores[n].Bounds.Top && triangulos[n].End <= separadores[n].Bounds.Bottom, $"o triângulo da página {n + 1} fica no separador");
        }

        Assert.True(triangulos[^1].Start >= linhas[^1].Bounds.Bottom, "o último triângulo fica abaixo da última linha");
    }

    private static void Desenhar(TextboxLayout layout, string tema, Action<GameTextbox, PixelProbe> inspecionar, Avalonia.Media.FontFamily? fonte = null)
    {
        var caixa = new GameTextbox { Layout = layout, FontFamily = fonte ?? GameTextbox.GameFont, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var janela = new Window { Width = 700, Height = 600, Background = Avalonia.Media.Brushes.Black, RequestedThemeVariant = Variant(tema), Content = caixa };
        try
        {
            janela.Show();
            ScreenRenderer.Settle(janela);
            inspecionar(caixa, PixelProbe.Capture(janela, caixa));
        }
        finally
        {
            janela.Close();
        }
    }

    private static Avalonia.Size Medir(TextboxLayout layout, string tema, GameTextboxSize tamanho = GameTextboxSize.Medium)
    {
        var caixa = new GameTextbox { Layout = layout, Size = tamanho };
        var janela = new Window { Width = 800, Height = 600, RequestedThemeVariant = Variant(tema), Content = caixa };
        try
        {
            janela.Show();
            ScreenRenderer.Settle(janela);
            return caixa.DesiredSize;
        }
        finally
        {
            janela.Close();
        }
    }
}
