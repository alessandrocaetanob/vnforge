using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Tests.Rendering;

namespace VNForge.App.Tests.Controls;

/// <summary>Os controles novos da interface v3 (issue #302): lógica, estados e renderização de cada um, nos dois temas.</summary>
public class V3ControlsTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private Task OnUi(Action test) => fixture.Session.Dispatch(test, TestContext.Current.CancellationToken);

    [Fact]
    public void Anel_e_barra_somam_pronto_e_revisao_sem_passar_do_total()
    {
        var anel = new ProgressRing { Done = 18592, Review = 10936, Total = 48211 };
        var barra = new Seg3Bar { Done = 40, Review = 80, Total = 100 };

        Assert.Equal(38, anel.Percent);
        Assert.Equal("38%", anel.PercentText);
        Assert.Equal(0.4, barra.DoneFraction, 3);
        Assert.Equal(0.6, barra.ReviewFraction, 3);
        Assert.Equal(0, new ProgressRing().Percent);
        Assert.Equal(99, new ProgressRing { Done = 999, Total = 1000 }.Percent);
    }

    [Fact]
    public void Dinheiro_usa_o_formato_pt_BR_e_so_mostra_o_dolar_quando_pedido()
    {
        var soReal = new CostLabel { Brl = 142.56m };
        var comDolar = new CostLabel { Brl = 1142.5m, Usd = 26.4m };

        Assert.Equal("R$ 142,56", soReal.BrlText);
        Assert.Null(soReal.UsdText);
        Assert.Equal("R$ 1.142,50", comDolar.BrlText);
        Assert.Equal("US$ 26,40", comDolar.UsdText);
    }

    [Fact]
    public void Orcamento_passa_do_limite_quando_gasto_mais_pedido_ultrapassam_o_limite()
    {
        var dentro = new BudgetBar { Spent = 142.56, Ask = 58.1, Limit = 300 };
        var fora = new BudgetBar { Spent = 142.56, Ask = 180, Limit = 300 };

        Assert.False(dentro.IsOver);
        Assert.DoesNotContain(":over", dentro.Classes);
        Assert.True(fora.IsOver);
        Assert.False(new BudgetBar { Spent = 500, Limit = 0 }.IsOver);
    }

    [Fact]
    public void Capitulo_calcula_a_parte_pronta_e_o_texto_das_linhas()
    {
        var capitulo = new ChapterRow { Done = 2840, Review = 1210, Total = 4100 };

        Assert.Equal("69%", capitulo.PercentText);
        Assert.Equal("4.100 linhas", capitulo.LinesText);
    }

    [Fact]
    public async Task Capitulo_aberto_e_o_extra_viram_pseudo_classes()
    {
        await OnUi(() =>
        {
            var aqui = new ChapterRow { IsHere = true };
            var extra = new ChapterRow { IsExtra = true };

            Assert.Contains(":here", aqui.Classes);
            Assert.Contains(":extra", extra.Classes);
        });
    }

    [Fact]
    public async Task Selo_mostra_o_nome_em_portugues_e_nunca_a_barra_invertida()
    {
        await OnUi(() =>
        {
            var selo = new MarkerChip { Marker = @"\p" };
            var janela = new Window { Width = 300, Height = 100, Content = selo };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);
                var textos = selo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

                Assert.Equal("virar a página", selo.Label);
                Assert.Contains("virar a página", textos);
                Assert.DoesNotContain(textos, t => t?.Contains('\\') == true);
                Assert.Equal(20, selo.GetVisualDescendants().OfType<Border>().First().Bounds.Height);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public async Task Texto_so_de_leitura_leva_o_selo_num_InlineUIContainer()
    {
        await OnUi(() =>
        {
            var texto = new TextBlock { TextWrapping = TextWrapping.Wrap };
            texto.Inlines!.Add(new Avalonia.Controls.Documents.Run("Foi incrível."));
            texto.Inlines.Add(new Avalonia.Controls.Documents.InlineUIContainer(new MarkerChip { Marker = @"\p" }));
            var janela = new Window { Width = 400, Height = 100, Content = texto };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);

                var selo = Assert.Single(texto.GetVisualDescendants().OfType<MarkerChip>());
                Assert.True(selo.Bounds.Width > 40);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public async Task Selo_faltando_ganha_a_pseudo_classe_de_erro()
    {
        await OnUi(() =>
        {
            var selo = new MarkerChip { Marker = @"\n", IsMissing = true };

            Assert.Contains(":missing", selo.Classes);
            Assert.Equal("quebra de linha", selo.Label);
            Assert.Equal("↵", selo.Glyph);
        });
    }

    [Fact]
    public async Task Escape_fecha_o_dialogo_e_avisa_quem_ouve()
    {
        await OnUi(() =>
        {
            var botao = new Button { Content = "Traduzir" };
            var dialogo = new ScreenDialog { Title = "Traduzir com IA", Content = botao, IsOpen = true };
            var fechou = 0;
            dialogo.Closed += (_, _) => fechou++;
            var janela = new Window { Width = 800, Height = 600, Content = new Grid { Children = { dialogo } } };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);
                Assert.True(dialogo.IsVisible);
                botao.Focus();

                janela.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                ScreenRenderer.Settle(janela);

                Assert.False(dialogo.IsOpen);
                Assert.False(dialogo.IsVisible);
                Assert.Equal(1, fechou);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public async Task Botao_de_fechar_do_dialogo_tambem_fecha()
    {
        await OnUi(() =>
        {
            var dialogo = new ScreenDialog { Title = "Traduzir com IA", IsOpen = true };
            var janela = new Window { Width = 800, Height = 600, Content = new Grid { Children = { dialogo } } };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);

                var fechar = dialogo.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Close");
                fechar.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

                Assert.False(dialogo.IsOpen);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public async Task Detalhes_tecnicos_comecam_fechados_e_o_grupo_de_cenas_abre_e_fecha()
    {
        await OnUi(() =>
        {
            var detalhes = new TechnicalDetails { Content = new TextBlock { Text = "motor" } };
            var grupo = new ScriptGroup { Title = "Capítulo 1", CountText = "14", Content = new TextBlock { Text = "cenas" } };
            var painel = new StackPanel { Children = { detalhes, grupo } };
            var janela = new Window { Width = 500, Height = 400, Content = painel };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);

                Assert.False(detalhes.IsExpanded);
                Assert.DoesNotContain(detalhes.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "motor" && t.IsEffectivelyVisible);
                Assert.True(grupo.IsExpanded);

                detalhes.IsExpanded = true;
                grupo.IsExpanded = false;
                ScreenRenderer.Settle(janela);

                Assert.Contains(detalhes.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "motor" && t.IsEffectivelyVisible);
                Assert.DoesNotContain(grupo.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "cenas" && t.IsEffectivelyVisible);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public async Task Cartao_de_qualidade_mostra_o_preco_em_reais_e_marca_um_por_grupo()
    {
        await OnUi(() =>
        {
            var rapida = new QualityCard { Title = "Rápida", Price = 2.92m, GroupName = "q", IsChecked = true };
            var cara = new QualityCard { Title = "Caprichada", Price = 116.19m, GroupName = "q" };
            var janela = new Window { Width = 600, Height = 300, Content = new StackPanel { Children = { rapida, cara } } };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);
                Assert.Equal("R$ 2,92", rapida.PriceText);
                Assert.Equal("R$ 116,19", cara.PriceText);

                cara.IsChecked = true;

                Assert.False(rapida.IsChecked);
                Assert.Contains(":checked", cara.Classes);
            }
            finally
            {
                janela.Close();
            }
        });
    }

    [Fact]
    public void Item_de_pendencia_e_estado_do_jogo_seguem_a_gravidade_e_o_estado()
    {
        var erro = new TodoItem { Severity = SeverityLevel.Error };
        var aviso = new TodoItem();
        var banner = new GameStateBanner { State = GameState.Blocked };

        Assert.Contains(":error", erro.Classes);
        Assert.Contains(":warning", aviso.Classes);
        Assert.DoesNotContain(":error", aviso.Classes);
        Assert.Contains(":blocked", banner.Classes);
        banner.State = GameState.Translated;
        Assert.Contains(":translated", banner.Classes);
        Assert.DoesNotContain(":blocked", banner.Classes);
    }

    public static TheoryData<string, string> EachControl
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in Factories.Keys)
            {
                data.Add(name, "Dark");
                data.Add(name, "Light");
            }

            return data;
        }
    }

    private static readonly Dictionary<string, Func<Control>> Factories = new()
    {
        ["anel"] = () => new ProgressRing { Done = 18592, Review = 10936, Total = 48211 },
        ["anel-pequeno"] = () => new ProgressRing { Classes = { "sm" }, Done = 62, Review = 20, Total = 100 },
        ["barra-de-tres-estados"] = () => new Seg3Bar { Width = 300, Done = 18592, Review = 10936, Total = 48211 },
        ["capitulo"] = () => new ChapterRow { Width = 900, Number = 1, Title = "Manhã de chuva", Done = 2840, Review = 1210, Total = 4100, Note = "Você está aqui · 1 erro em Manhã de chuva", IsHere = true },
        ["custo"] = () => new CostLabel { Brl = 142.56m, Usd = 26.4m },
        ["orcamento"] = () => new BudgetBar { Width = 400, Spent = 142.56, Ask = 180, Limit = 300 },
        ["qualidade"] = () => new QualityCard { Width = 260, Title = "Equilibrada", Model = "Sonnet", Price = 58.1m, Duration = "cerca de 78 min", Description = "O meio-termo.", IsChecked = true },
        ["contexto"] = () => new ContextLines { Width = 400, ItemsSource = new[] { new ContextLine("Kenta", "「おはよう、美咲」", "Bom dia, Misaki.") } },
        ["explicacao"] = () => new ExplainCard { Width = 400, Model = "Claude Haiku", Footnote = "Só para você ler.", ItemsSource = new[] { new ExplainPart("今日も", "de novo hoje") } },
        ["grupo-de-cenas"] = () => new ScriptGroup { Width = 260, Title = "Capítulo 1", CountText = "14", Content = new TextBlock { Text = "yst00001" } },
        ["estado-do-jogo"] = () => new GameStateBanner { Width = 700, State = GameState.Translated, Title = "O jogo está com a tradução de 22/10", Message = "A tradução continua no projeto.", Content = new Button { Content = "Voltar ao jogo original" } },
        ["pendencia"] = () => new TodoItem { Width = 600, Severity = SeverityLevel.Error, Title = "2 erros travam o teste", Message = "Marcador faltando em Manhã de chuva", ActionText = "Corrigir" },
        ["detalhes-tecnicos"] = () => new TechnicalDetails { Width = 400, IsExpanded = true, Content = new TextBlock { Text = "motor · YU-RIS" } },
        ["selo"] = () => new MarkerChip { Marker = @"\p" },
        ["dialogo"] = () => new Grid { Children = { new TextBlock { Text = "tela" }, new ScreenDialog { Title = "Traduzir com IA", IsOpen = true, Content = new TextBlock { Text = "corpo" } } } },
    };

    [Theory]
    [MemberData(nameof(EachControl))]
    public async Task Cada_controle_renderiza_nos_dois_temas(string nome, string tema)
    {
        var variant = Variant(tema);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName($"v3-{nome}", variant));
        await OnUi(() =>
        {
            var controle = Factories[nome]();
            Control host = nome == "dialogo"
                ? controle
                : new Border { Padding = new Thickness(24), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = controle };
            ScreenRenderer.Render(host, variant, path, 800, 400, _ =>
            {
                Assert.True(controle.Bounds.Width > 0 && controle.Bounds.Height > 0, $"{nome} sem tamanho");
                Assert.True(controle.IsEffectivelyVisible);
            });
        });

        Assert.True(new FileInfo(path).Length > 1_000);
    }

    [Fact]
    public async Task Anel_pinta_os_tres_estados_com_as_cores_dos_tokens()
    {
        await OnUi(() =>
        {
            var anel = new ProgressRing { Done = 50, Review = 25, Total = 100 };
            var janela = new Window { Width = 300, Height = 300, RequestedThemeVariant = ThemeVariant.Dark, Content = anel };
            try
            {
                janela.Show();
                ScreenRenderer.Settle(janela);
                using var quadro = janela.CaptureRenderedFrame()!;
                var centro = anel.TranslatePoint(new Point(anel.Bounds.Width / 2, anel.Bounds.Height / 2), janela)!.Value;
                var raio = (Math.Min(anel.Bounds.Width, anel.Bounds.Height) / 2) - 6;
                Color Em(double dx, double dy)
                {
                    var p = new PixelPoint((int)(centro.X + dx), (int)(centro.Y + dy));
                    return PixelAt(quadro, p);
                }

                Assert.Equal(Token(anel, "Vnf.SegApproved"), Em(raio, 0));
                Assert.Equal(Token(anel, "Vnf.SegAi"), Em(-raio * 0.7071, raio * 0.7071));
                Assert.Equal(Token(anel, "Vnf.SegPending"), Em(-raio * 0.7071, -raio * 0.7071));
            }
            finally
            {
                janela.Close();
            }
        });
    }

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found));
        return Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
    }

    private static Color PixelAt(Avalonia.Media.Imaging.WriteableBitmap frame, PixelPoint point)
    {
        using var buffer = frame.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (point.Y * buffer.RowBytes) + (point.X * 4), bytes, 0, 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromArgb(255, bytes[2], bytes[1], bytes[0])
            : Color.FromArgb(255, bytes[0], bytes[1], bytes[2]);
    }
}
