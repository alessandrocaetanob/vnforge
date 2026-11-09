using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Eval;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Painel na aba "Provas" (#529), a 1440×1000, nos dois temas, com os números do quadro: <c>provas-relatorio</c> (a tabela, o gráfico e a leitura),
/// <c>provas-as-cegas</c> (o par 7 de 12) e <c>provas-rodar</c> (o diálogo com o custo antes de rodar). Os PNGs saem em <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class EvalRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Remaining = 18320;

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    /// <summary>Um relatório com as medidas do quadro: o custo e o tempo do jogo todo (em reais, a 5,40) e a média de chrF e de juiz.</summary>
    private static EvalConfigReport Report(EvalConfig config, double brl, double minutes, double chrf, double judge, string[] ids)
    {
        var lines = ids.Select(id => new EvalLineResult(id, config.Id switch
        {
            "B" => "Aizawa, Sakura. Os dois, cinco minutos atrasados.",
            "C" => "Aizawa, Sakura. Vocês dois chegaram cinco minutos atrasados.",
            _ => $"Texto de {config.Id} para {id}",
        }, chrf, judge)).ToList();
        var share = (decimal)ids.Length / Remaining;
        return new EvalConfigReport(config, lines, 0, 0, 0, 0, (decimal)brl / 5.40m * share, 0m, minutes * 60 * ids.Length / Remaining);
    }

    private static readonly EvalConfig[] Configs =
    [
        new("A", "claude-opus-5-5", "high", 15, true),
        new("B", "claude-opus-5-5", "medium", 15, true),
        new("C", "claude-sonnet-5-5", "medium", 15, true),
        new("D", "claude-sonnet-5-5", "medium", 2, false),
        new("E", "claude-haiku-5-5", "low", 15, false),
    ];

    private static EvalRound Round(string[] ids, IReadOnlyList<EvalSample> sample)
    {
        var configs = Configs;
        EvalConfigReport[] reports =
        [
            Report(configs[0], 156.70, 41, 61.4, 8.7, ids),
            Report(configs[1], 128.10, 34, 60.9, 8.5, ids),
            Report(configs[2], 78.40, 27, 59.8, 8.1, ids),
            Report(configs[3], 71.20, 22, 54.3, 7.2, ids),
            Report(configs[4], 3.90, 6, 47.0, 5.9, ids),
        ];
        BlindPair[] pairs = [.. Enumerable.Range(1, 12).Select(i => new BlindPair(i, ids[i % ids.Length], i % 2 == 0 ? "A" : "B", i % 3 == 0 ? "D" : "C"))];
        var answers = pairs.Take(6).Select(p => new BlindAnswer(p.Index, p.Index % 2 == 0 ? BlindChoice.Second : BlindChoice.First)).ToList();
        return new EvalRound(
            EvalRound.CurrentVersion, "20261108-120000", new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero), 1, 1240, Remaining, "claude-sonnet-5-5",
            sample, reports, pairs, answers, "C");
    }

    private static string[] Ids => [.. Enumerable.Range(1, 12).Select(i => $"yst00001:{i:D6}")];

    private static EvalSample[] Sample() =>
    [
        .. Ids.Select((id, i) => new EvalSample(id, "yst00001", "美咲", "「相沢、佐倉。二人そろって五分遅刻だ」", "Aizawa, Sakura. Os dois, cinco minutos atrasados.", "Kenta, \"Ehehe... Desculpa.\"", "Misaki, \"Aham.\"")),
    ];

    private async Task<(DashboardViewModel Vm, TempFolder Temp)> Open(bool withRound = true)
    {
        var temp = new TempFolder();
        var statuses = Enumerable.Repeat(TranslationStatus.Approved, 20).Concat(Enumerable.Repeat(TranslationStatus.Untranslated, 10)).ToArray();
        var root = SampleProjects.Create(temp.SubFolder("projeto"), "Amaoto no Hakoniwa", statuses);
        var folder = ProjectFolder.Open(root);
        var texts = folder.LoadText("yst00001").Select(u => (u.Id == "yst00001:000001" ? u with { Translation = "Aizawa, Sakura. Os dois, cinco minutos atrasados." } : u) with { Speaker = "美咲" }).ToList();
        folder.SaveText("yst00001", texts);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("相沢", "Aizawa"), new CharacterEntry("佐倉", "Sakura")]);
        folder.SaveBible(GameBible.From([new BibleScene("yst00001") { Characters = ["Misaki", "Aizawa", "Sakura"] }]));
        AiUsageLog.TryRecord(folder, new AiUsageTotals(1_500_000, 600_000, 0, 0, 26.40m, 1));
        if (withRound)
        {
            EvalStore.SaveRound(folder, Round(Ids, Sample()));
        }
        else
        {
            EvalStore.SaveConfigs(folder, Configs);
        }

        var vm = new DashboardViewModel
        {
            Project = ProjectSummaryReader.Read(root),
            Context = () => new DashboardContext(ExchangeRate.Placeholder, new BudgetLimits(300m, 250m), true),
        };
        await vm.ReloadAsync();
        vm.SelectedTab = 1;
        await vm.Eval.Loading;
        return (vm, temp);
    }

    private static IEnumerable<string?> Texts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_aba_Provas_mostra_a_tabela_o_grafico_e_a_leitura_da_rodada(string themeName)
    {
        var (vm, temp) = await Open();
        using (temp)
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("provas-relatorio", Variant(themeName)));
                    ScreenRenderer.Render(new DashboardView { DataContext = vm }, Variant(themeName), path, inspect: window =>
                    {
                        Assert.True(new FileInfo(path).Length > 10_000);
                        var texts = Texts(window).ToList();
                        Assert.Equal(5, window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("evalrow")));
                        Assert.Single(window.GetVisualDescendants().OfType<EvalChart>());
                        Assert.Contains("R$ 156,70", texts);
                        Assert.Contains("lote R$ 78,35", texts);
                        Assert.Contains("41 min", texts);
                        Assert.Contains("61,4", texts);
                        Assert.Contains("8,7", texts);
                        Assert.DoesNotContain("Escolhida para o M4", texts);
                        Assert.Contains("Leitura da rodada", texts.Select(t => t?.ToLowerInvariant() == "leitura da rodada" ? "Leitura da rodada" : t));
                        Assert.Contains("Usar C no M4", texts);
                        Assert.Contains("Continuar às cegas", texts);
                        Assert.Contains("Rodar provas", texts);
                        Assert.Contains("Rodar no terminal", texts);
                        Assert.Equal(5, window.GetVisualDescendants().OfType<ShareBar>().Count(b => Math.Abs(b.Bounds.Width - 70) <= 2));
                        Assert.Contains("chrF: quanto a tradução se parece com a aprovada, de 0 a 100", texts);
                        Assert.Contains("Juiz de IA: nota de 0 a 10, sem saber qual configuração gerou o texto", texts);
                        Assert.Contains(texts, t => t is not null && t.StartsWith("C chega a 97% do chrF de A por metade do preço", StringComparison.Ordinal));
                        Assert.Contains("Jogo todo, estimado", texts);
                        Assert.Contains("Sua escolha às cegas", texts);
                        // O cabeçalho das colunas fica em uma linha só, como no quadro.
                        Assert.All(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("column-header")), t => Assert.True(t.Bounds.Height <= 16.5, $"\"{t.Text}\" quebrou em {t.Bounds.Height} px"));
                        Assert.DoesNotContain(texts, t => t is not null && (t.Contains('—') || t.Contains('–')));
                    });
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_rodada_as_cegas_mostra_o_original_e_dois_textos_sem_a_configuracao(string themeName)
    {
        var (vm, temp) = await Open();
        using (temp)
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    vm.Eval.BlindStartCommand.Execute(null);
                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("provas-as-cegas", Variant(themeName)));
                    ScreenRenderer.Render(new DashboardView { DataContext = vm }, Variant(themeName), path, inspect: window =>
                    {
                        Assert.True(new FileInfo(path).Length > 10_000);
                        var texts = Texts(window).ToList();
                        Assert.Contains("Rodada às cegas", texts);
                        Assert.Contains("Texto A", texts);
                        Assert.Contains("Texto B", texts);
                        Assert.Contains("「相沢、佐倉。二人そろって五分遅刻だ」", texts);
                        Assert.Equal(2, texts.Count(t => t == "configuração escondida"));
                        Assert.Contains(texts, t => t is not null && t.StartsWith("Par 7 de 12", StringComparison.Ordinal));
                        var buttons = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content).ToList();
                        Assert.Contains("A é melhor", buttons);
                        Assert.Contains("B é melhor", buttons);
                        Assert.Contains("Empate", buttons);
                        Assert.Contains("Nenhum dos dois", buttons);
                        Assert.Contains("Pular par", texts);
                        Assert.DoesNotContain("Voltar ao relatório", texts);
                        Assert.Contains("Misaki → Aizawa e Sakura", texts);
                        Assert.Contains("ORIGINAL · CENA 1, LINHA 0008", texts);
                        Assert.DoesNotContain(window.GetVisualDescendants().OfType<EvalChart>(), c => c.IsEffectivelyVisible);
                        Assert.DoesNotContain(texts, t => t is not null && t.Contains("Opus", StringComparison.Ordinal));
                    });
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Esc_na_rodada_as_cegas_volta_ao_relatorio_sem_o_link_do_quadro_antigo()
    {
        var (vm, temp) = await Open();
        using (temp)
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    var window = new Window { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, Content = new DashboardView { DataContext = vm } };
                    try
                    {
                        window.Show();
                        vm.Eval.BlindStartCommand.Execute(null);
                        ScreenRenderer.Settle(window);
                        window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Empate")).Focus();

                        Assert.True(vm.Eval.IsBlind);
                        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

                        Assert.False(vm.Eval.IsBlind);
                        Assert.True(vm.Eval.ShowReport);
                    }
                    finally
                    {
                        window.Close();
                    }

                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_dialogo_de_rodar_mostra_o_custo_de_cada_configuracao_antes_de_gastar(string themeName)
    {
        var (vm, temp) = await Open(withRound: false);
        using (temp)
        {
            await fixture.Session.Dispatch(
                async () =>
                {
                    var window = new Window { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = Variant(themeName), Content = new DashboardView { DataContext = vm } };
                    window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Vnf.BgLayer"));
                    try
                    {
                        window.Show();
                        ScreenRenderer.Settle(window);
                        await vm.Eval.RunOpenCommand.ExecuteAsync(null);
                        vm.Eval.RunItems[^1].IsSelected = false;
                        ScreenRenderer.Settle(window);
                        var path = RenderOutput.PathFor(ScreenRenderer.FileName("provas-rodar", Variant(themeName)));
                        ScreenRenderer.Save(window, path);
                        Assert.True(new FileInfo(path).Length > 10_000);

                        var overlay = OverlayLayer.GetOverlayLayer(window)!.Children.OfType<Control>().Single(c => c.GetVisualDescendants().OfType<BudgetBar>().Any());
                        var texts = Texts(overlay).ToList();
                        Assert.Contains("Rodar o banco de provas", texts);
                        Assert.Equal(5, overlay.GetVisualDescendants().OfType<CheckBox>().Count(c => c.Content is null));
                        Assert.Contains(texts, t => t is not null && t.StartsWith("Custo (aproximado): R$ ", StringComparison.Ordinal));
                        Assert.Contains(texts, t => t is not null && t.StartsWith("Já gasto no projeto: R$ 142,56 de R$ 300,00", StringComparison.Ordinal));
                        Assert.Contains("Rodar 4 configurações", texts);
                        Assert.DoesNotContain(texts, t => t is not null && t.StartsWith("Para mudar as configurações", StringComparison.Ordinal));
                        var judge = overlay.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Juiz de IA cego, uma nota por texto"));
                        Assert.Same(overlay.GetVisualDescendants().OfType<BudgetBar>().Single().GetVisualParent(), judge.GetVisualParent());
                        Assert.Contains("Cancelar", overlay.GetVisualDescendants().OfType<Button>().Select(b => b.Content));
                        Assert.Contains(overlay.GetVisualDescendants().OfType<CheckBox>(), c => Equals(c.Content, "Juiz de IA cego, uma nota por texto") && c.IsChecked == true);

                        // Medidas do quadro: botão principal de 40 px, Cancelar de 87 px e cartão de custo de 94 px.
                        var cancel = overlay.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancelar"));
                        var run = overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("accent") && b.IsEffectivelyVisible);
                        var costCard = Assert.IsType<Border>(judge.GetVisualParent()!.GetVisualParent());
                        Assert.Equal(87, cancel.Bounds.Width, 1);
                        Assert.Equal(40, run.Bounds.Height, 1);
                        Assert.Equal(94, costCard.Bounds.Height, 1);
                    }
                    finally
                    {
                        window.Close();
                    }
                },
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Sem_rodada_a_aba_convida_a_rodar_e_sem_linhas_aprovadas_diz_o_que_fazer()
    {
        var (vm, temp) = await Open(withRound: false);
        using (temp)
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    ScreenRenderer.Render(new DashboardView { DataContext = vm }, ThemeVariant.Dark, RenderOutput.PathFor(ScreenRenderer.FileName("provas-sem-rodada", ThemeVariant.Dark)), inspect: window =>
                    {
                        var texts = Texts(window).ToList();
                        Assert.Contains("Nenhuma rodada ainda", texts);
                        Assert.Contains("Rodar provas", texts);
                        Assert.DoesNotContain(window.GetVisualDescendants().OfType<EvalChart>(), c => c.IsEffectivelyVisible);
                    });
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task O_grafico_desenha_um_ponto_por_configuracao()
    {
        var points = new[] { new EvalChartPoint("A", 156.7, 61.4, false), new EvalChartPoint("C", 78.4, 59.8, true), new EvalChartPoint("E", 3.9, 47, false) };

        await fixture.Session.Dispatch(
            () =>
            {
                var chart = new EvalChart { ItemsSource = points, Width = 380, Height = 220 };
                var path = RenderOutput.PathFor(ScreenRenderer.FileName("provas-grafico", ThemeVariant.Dark));
                ScreenRenderer.Render(chart, ThemeVariant.Dark, path, 400, 260, window => Assert.Same(points, window.GetVisualDescendants().OfType<EvalChart>().Single().ItemsSource));
                Assert.True(new FileInfo(path).Length > 1_000);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
    }
}
