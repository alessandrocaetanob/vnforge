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
    public async Task Duas_de_tres_linhas_pintam_duas_linhas_de_tinta_a_terceira_pontilhada_e_o_triangulo(string tema)
    {
        await fixture.Session.Dispatch(
            () => Desenhar(Caixa([2]), tema, (caixa, probe) =>
            {
                var cores = Cores(caixa);
                var tinta = probe.RowBands(cores.Ink, Tinta, TextoDe, TextoAte(probe), AbaixoDaPlaca);

                Assert.Equal(2, tinta.Count);
                Assert.Empty(probe.RowBands(cores.Overflow, Tinta, 0, probe.Width));
                var triangulo = Assert.Single(probe.ColumnRuns(ColunaDoTriangulo(probe), cores.Triangle, Triangulo));
                Assert.True(triangulo.Start > tinta[1].End, "o triângulo fica abaixo da última linha");
                Assert.True(triangulo.Start >= probe.Height - 28, "e abaixo da terceira linha, que ficou vaga");

                var pontilhado = Enumerable.Range(tinta[1].End + 1, triangulo.Start - tinta[1].End - 1)
                    .Count(y => probe.CountInRow(y, cores.Dotted, 6, TextoDe, TextoAte(probe)) >= 10);
                Assert.True(pontilhado >= 2, "as linhas sem texto ganham o pontilhado: a que falta da segunda e a vaga");
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
                var cores = Cores(caixa);
                var tinta = probe.RowBands(cores.Ink, Tinta, TextoDe, TextoAte(probe), AbaixoDaPlaca);
                var largura = TextoAte(probe) - TextoDe;
                var textoDeEstouro = Enumerable.Range(AbaixoDaPlaca, probe.Height - AbaixoDaPlaca)
                    .Where(y =>
                    {
                        var pixels = probe.CountInRow(y, cores.Overflow, Tinta, TextoDe, TextoAte(probe));
                        return pixels > 0 && pixels < largura * 0.4;
                    })
                    .ToArray();

                Assert.Equal(3, tinta.Count);
                Assert.NotEmpty(textoDeEstouro);
                Assert.True(textoDeEstouro.Max() - textoDeEstouro.Min() < 14, "a quarta linha é uma só");
                Assert.True(textoDeEstouro.Min() > tinta[2].End, "a quarta linha vem depois das três de tinta");

                var traco = Enumerable.Range(tinta[2].End + 1, textoDeEstouro.Min() - tinta[2].End - 1)
                    .Where(y => probe.CountInRow(y, cores.OverflowSteps, 10, TextoDe - 6, TextoAte(probe) + 6) >= largura / 4)
                    .ToArray();
                Assert.InRange(traco.Length, 1, 2);
                Assert.True(traco.Max() < textoDeEstouro.Min(), "o traço de overflow fica entre a terceira linha e a quarta");
                Assert.DoesNotContain(tinta, b => b.Start > traco.Min());
                var linha = (Start: textoDeEstouro.Min(), End: textoDeEstouro.Max());

                var triangulo = Assert.Single(probe.ColumnRuns(ColunaDoTriangulo(probe), cores.Triangle, Triangulo));
                Assert.True(triangulo.Start > linha.End, "o triângulo vem depois da linha que passou");
            }),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Duas_paginas_pintam_cinco_linhas_e_um_triangulo_ao_fim_de_cada_pagina(string tema)
    {
        await fixture.Session.Dispatch(
            () => Desenhar(Caixa([3, 2]), tema, (caixa, probe) =>
            {
                var cores = Cores(caixa);
                var tinta = probe.RowBands(cores.Ink, Tinta, TextoDe, TextoAte(probe), AbaixoDaPlaca);

                Assert.Equal(5, tinta.Count);
                Assert.Empty(probe.RowBands(cores.Overflow, Tinta, 0, probe.Width));
                var triangulos = probe.ColumnRuns(ColunaDoTriangulo(probe), cores.Triangle, Triangulo);
                Assert.Equal(2, triangulos.Count);
                Assert.True(triangulos[0].Start > tinta[2].End && triangulos[0].End < tinta[3].Start, "o primeiro triângulo fecha a página 1");
                Assert.True(triangulos[1].Start > tinta[4].End, "o segundo fecha a página 2");

                var separador = Enumerable.Range(tinta[2].End + 1, tinta[3].Start - tinta[2].End - 1)
                    .Count(y => probe.CountInRow(y, cores.SeparatorSteps, 4, TextoDe, TextoAte(probe)) >= 10);
                Assert.True(separador >= 1, "o traço tracejado do separador \"página 2\" fica entre as duas páginas");
            }),
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
                    foreach (var (caixa, linhas) in caixas.Zip(new[] { 2, 3, 5 }))
                    {
                        var probe = PixelProbe.Capture(window, caixa);
                        Assert.Equal(linhas, probe.RowBands(Cores(caixa).Ink, Tinta, TextoDe, TextoAte(probe), AbaixoDaPlaca).Count);
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
                        Thread.Sleep(60);
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
                        Thread.Sleep(60);
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
    private const int TextoDe = 20;
    private const int AbaixoDaPlaca = 28;

    private static int TextoAte(PixelProbe probe) => probe.Width - 60;

    private static int ColunaDoTriangulo(PixelProbe probe) => probe.Width - 26;

    private sealed record CoresDaCaixa(Avalonia.Media.Color Ink, Avalonia.Media.Color Overflow, Avalonia.Media.Color Triangle, Avalonia.Media.Color Dotted, IReadOnlyList<Avalonia.Media.Color> OverflowSteps, IReadOnlyList<Avalonia.Media.Color> SeparatorSteps);

    private static CoresDaCaixa Cores(Control host)
    {
        Avalonia.Media.Color Token(string key)
        {
            Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), key);
            return Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(found).Color;
        }

        var (ink, overflow, background) = (Token("Vnf.TextboxInk"), Token("Vnf.TextboxOverflow"), Token("Vnf.TextboxBg"));
        return new CoresDaCaixa(ink, overflow, PixelProbe.Blend(ink, 0.8, background), PixelProbe.Blend(ink, 0.13, background), PixelProbe.Steps(overflow, 1, background), PixelProbe.Steps(ink, 0.22, background));
    }

    private static void Desenhar(TextboxLayout layout, string tema, Action<GameTextbox, PixelProbe> inspecionar)
    {
        var caixa = new GameTextbox { Layout = layout, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
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
