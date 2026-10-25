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
        Assert.Equal("Cabe · 2 de 3 linhas", TextboxPreviewViewModel.FitText(layout));
    }

    [Fact]
    public void Quatro_linhas_para_tres_levam_a_quarta_para_baixo_do_traco()
    {
        var layout = Caixa([4]);
        var grade = GameTextbox.Describe(layout);

        Assert.Equal([GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Text, GameTextboxRowKind.Overflow], Tipos(layout));
        Assert.Equal([false, false, false, true], grade.Select(r => r.FirstOverflow));
        Assert.Equal("Passa 1 linha", TextboxPreviewViewModel.FitText(layout));
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
        Assert.Equal("Cabe · 3 de 3 linhas", TextboxPreviewViewModel.FitText(layout));
    }

    [Fact]
    public void Sem_limite_de_linhas_nao_ha_veredito()
    {
        Assert.Null(TextboxPreviewViewModel.FitText(Caixa([2]) with { MaxLines = null }));
        Assert.Equal("Passa 5 linhas", TextboxPreviewViewModel.FitText(Caixa([4, 5], maximo: 2) with { MaxLines = 2 }));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Cada_caso_desenha_nos_dois_temas_e_a_altura_acompanha_a_grade(string tema)
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
                    Assert.All(window.GetVisualDescendants().OfType<GameTextbox>(), c => Assert.True(c.Bounds.Height > 0)));
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
