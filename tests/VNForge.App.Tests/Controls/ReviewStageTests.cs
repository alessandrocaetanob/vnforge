using Avalonia;
using Avalonia.Controls;
using VNForge.App.Controls;
using VNForge.App.Tests.Rendering;

namespace VNForge.App.Tests.Controls;

/// <summary>A conta do palco 16:9 e a pilha que o acompanha (<see cref="StageColumn"/>), sem janela: só medida e arranjo.</summary>
public sealed class ReviewStageTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const double Aspect = 16d / 9d;

    [Theory]
    [InlineData(800, 600, 800)]
    [InlineData(800, 300, 300 * Aspect)]
    [InlineData(0, 300, 0)]
    [InlineData(800, 0, 0)]
    [InlineData(0, 0, 0)]
    public void Fit_cabe_nos_dois_eixos_e_nunca_passa_de_nenhum(double width, double height, double expectedWidth)
    {
        var fitted = ReviewStage.Fit(new Size(width, height));

        Assert.Equal(expectedWidth, fitted.Width, 3);
        Assert.Equal(expectedWidth / Aspect, fitted.Height, 3);
    }

    [Fact]
    public void Fit_com_um_eixo_sem_limite_vale_o_outro()
    {
        var byWidth = ReviewStage.Fit(new Size(640, double.PositiveInfinity));
        Assert.Equal(new Size(640, 360), byWidth);

        var byHeight = ReviewStage.Fit(new Size(double.PositiveInfinity, 270));
        Assert.Equal(270 * Aspect, byHeight.Width, 3);
        Assert.Equal(270, byHeight.Height, 3);
    }

    [Fact]
    public void Fit_sem_limite_nenhum_usa_a_largura_padrao()
    {
        var fitted = ReviewStage.Fit(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(ReviewStage.UnboundedWidth, fitted.Width);
        Assert.Equal(ReviewStage.UnboundedWidth / Aspect, fitted.Height, 3);
    }

    [Fact]
    public Task A_pilha_de_baixo_nao_tira_o_palco_do_minimo_e_nao_passa_da_altura()
    {
        return fixture.Session.Dispatch(
            () =>
            {
                var (column, stage, below) = Build(belowHeight: 900);
                column.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                column.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                var window = new Window { Width = 800, Height = 500, Content = column };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);

                    Assert.True(stage.Bounds.Height >= column.MinStageHeight - 0.5, $"o palco tem {stage.Bounds.Height} px");
                    Assert.True(column.Bounds.Height <= 500 + 0.5, $"a coluna tem {column.Bounds.Height} px de 500");
                    Assert.True(below.Bounds.Bottom <= 500 + 0.5, $"baixo termina em {below.Bounds.Bottom}");
                    Assert.True(below.Bounds.Top >= stage.Bounds.Bottom, $"baixo começa em {below.Bounds.Top}, palco termina em {stage.Bounds.Bottom}");
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Sem_aviso_nenhum_a_pilha_nao_deixa_vao_de_espaco_sob_o_palco()
    {
        return fixture.Session.Dispatch(
            () =>
            {
                var (column, stage, below) = Build(belowHeight: 0);
                column.Measure(new Size(800, 600));
                column.Arrange(new Rect(0, 0, column.DesiredSize.Width, column.DesiredSize.Height));

                Assert.Equal(stage.DesiredSize.Height, column.DesiredSize.Height, 3);
                Assert.Equal(stage.DesiredSize.Width, column.DesiredSize.Width, 3);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task O_texto_de_baixo_e_medido_e_organizado_na_mesma_largura_do_palco()
    {
        return fixture.Session.Dispatch(
            () =>
            {
                var text = new TextBlock { Text = string.Join(' ', Enumerable.Repeat("aviso comprido que quebra em mais linhas quando a coluna fica estreita", 3)), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                var stage = new ReviewStage();
                var column = new StageColumn { Children = { stage, text } };
                column.Measure(new Size(1000, 330));
                column.Arrange(new Rect(0, 0, column.DesiredSize.Width, column.DesiredSize.Height));

                Assert.True(stage.Bounds.Width < 1000, "o palco é limitado pela altura");
                Assert.Equal(stage.Bounds.Width, text.Bounds.Width, 0.5);
                Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5);
                Assert.True(column.DesiredSize.Height <= 330 + 0.5, $"a coluna tem {column.DesiredSize.Height} px de 330");
            },
            TestContext.Current.CancellationToken);
    }

    private static (StageColumn Column, ReviewStage Stage, ScrollViewer Below) Build(double belowHeight)
    {
        var stage = new ReviewStage();
        var below = new ScrollViewer { Content = new Border { Height = belowHeight } };
        var column = new StageColumn { Children = { stage, below } };
        return (column, stage, below);
    }
}
