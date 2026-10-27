using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.AI.Claude;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Ajustes de verdade (tema, estilos, recursos): as três abas existem, os cartões escolhem o modelo e a acentuação, e o cartão da chave troca o campo.</summary>
public class SettingsViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly string[] JargaoDosAcentos = ["build", "byte", "sjis", "remap", "Shift-JIS"];

    private static Window Abrir(SettingsViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 1000, Content = new SettingsView { DataContext = vm } };
        window.Show();
        Assentar(window);
        return window;
    }

    private static void Assentar(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static SettingsViewModel Criar(TempFolder temp, string? environmentKey = null) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), name => name == "ANTHROPIC_API_KEY" ? environmentKey : null,
            _ => new NoopClient());

    private static Button Botao(Window window, string texto) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == texto);

    private sealed class NoopClient : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => Task.FromResult(new ClaudeReply("end_turn", string.Empty));
    }

    /// <summary>A sobrancelha diz "a chave do Claude": o nome do produto, não o do modelo, e a frase fixa do desenho.</summary>
    private const string Sobrancelha = "Valem só para este projeto. A chave do Claude fica guardada no Windows.";

    [Fact]
    public async Task A_aba_IA_e_gasto_nao_mostra_modelo_variavel_nem_DPAPI_fora_dos_detalhes_tecnicos()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var vm = Criar(temp, environmentKey: "sk-ant-chave-do-teste");
        vm.Load(VNForge.Core.Projects.ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        vm.ExchangeRateText = "5,40";
        vm.SpendLimitText = "300";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        vm.ChangeKeyCommand.Execute(null);
        Assert.True(vm.ConnectionOk);
        string[] proibidos = ["Claude ", "Haiku", "Sonnet", "Opus", "DPAPI", "ANTHROPIC", "token"];

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var textos = window.GetVisualDescendants()
                    .Where(v => !v.GetVisualAncestors().OfType<TechnicalDetails>().Any() && v is not TechnicalDetails)
                    .SelectMany(TextosVisiveis)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                Assert.Contains(Sobrancelha, textos);
                textos.Remove(Sobrancelha);

                Assert.Contains("Conexão funcionando.", textos);
                Assert.Contains("Qualidade padrão", textos);
                foreach (var proibido in proibidos)
                {
                    Assert.DoesNotContain(textos, t => t!.Contains(proibido, StringComparison.OrdinalIgnoreCase));
                }
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData("sjis", "o teste no jogo recusa as letras acentuadas")]
    [InlineData("remap", "A fonte modificada ainda não está pronta.")]
    public async Task Acentos_com_estrategia_que_o_teste_recusa_avisa_sem_jargao_e_desliga_o_botao_de_teste(string strategy, string esperado)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = VNForge.Core.Projects.ProjectFolder.Open(root);
        folder.SaveConfig(folder.LoadConfig() with { Encoding = new VNForge.Core.Projects.EncodingConfig(strategy) });
        var vm = Criar(temp);
        vm.Load(VNForge.Core.Projects.ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        vm.SelectedTab = 3;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var textos = window.GetVisualDescendants()
                    .Where(v => !v.GetVisualAncestors().OfType<TechnicalDetails>().Any() && v is not TechnicalDetails)
                    .SelectMany(TextosVisiveis)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                Assert.Contains(textos, t => t!.Contains(esperado, StringComparison.Ordinal));
                foreach (var proibido in JargaoDosAcentos)
                {
                    Assert.DoesNotContain(textos, t => t!.Contains(proibido, StringComparison.OrdinalIgnoreCase));
                }

                Assert.False(Botao(window, "Abrir Testar no jogo").IsEnabled);
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(760)]
    public async Task Detalhes_tecnicos_dos_acentos_listam_cada_estrategia_na_largura_do_cartao_sem_sobreposicao(int largura)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Criar(temp);
        vm.Load(VNForge.Core.Projects.ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        vm.SelectedTab = 3;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                window.Width = largura;
                var detalhes = window.GetVisualDescendants().OfType<TechnicalDetails>().Single(d => d.IsEffectivelyVisible);
                detalhes.IsExpanded = true;
                Assentar(window);

                var lista = detalhes.GetVisualDescendants().OfType<ItemsControl>().Single(i => Avalonia.Automation.AutomationProperties.GetName(i) == "Nomes técnicos das estratégias");
                var nota = detalhes.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.EncodingCurrentNote);
                var linhas = lista.GetVisualDescendants().OfType<Grid>().Where(g => g.Children.Count == 4).ToList();
                Assert.Equal(vm.EncodingTechnicalRows.Count, linhas.Count);

                Rect Caixa(Control c) => new(c.TranslatePoint(default, detalhes) ?? default, c.Bounds.Size);
                var xNota = Caixa(nota).X;
                foreach (var linha in linhas)
                {
                    var textos = linha.Children.OfType<TextBlock>().Select(t => (t.Text, Box: Caixa(t))).ToList();
                    var (id, nome, exemplo, descricao) = (textos[0].Box, textos[1].Box, textos[2].Box, textos[3].Box);

                    // Nenhum texto pisa em outro: cada um tem a sua caixa.
                    for (var i = 0; i < textos.Count; i++)
                    {
                        for (var j = i + 1; j < textos.Count; j++)
                        {
                            Assert.False(textos[i].Box.Intersects(textos[j].Box), $"'{textos[i].Text}' se sobrepõe a '{textos[j].Text}' a {largura}px.");
                        }
                    }

                    // Valor gravado, nome e exemplo na mesma linha, da esquerda para a direita; a descrição embaixo do nome e na largura do cartão.
                    Assert.Equal(xNota, id.X, 1);
                    Assert.True(id.Right <= nome.X && nome.Right <= exemplo.X, "Valor gravado, nome e exemplo deviam seguir da esquerda para a direita.");
                    Assert.True(descricao.Y >= nome.Bottom - 1, "A descrição devia ficar embaixo do nome.");
                    Assert.Equal(nome.X, descricao.X, 1);
                    Assert.True(descricao.Width >= linha.Bounds.Width * 0.7, $"A descrição ficou estreita ({descricao.Width:0}px de {linha.Bounds.Width:0}px) a {largura}px.");
                }
            },
            CancellationToken.None);
    }

    private static IEnumerable<string?> TextosVisiveis(Avalonia.Visual visual) => visual switch
    {
        TextBlock { IsEffectivelyVisible: true } block => [block.Text],
        TextBox { IsEffectivelyVisible: true } box => [box.PlaceholderText],
        ContentControl { IsEffectivelyVisible: true, Content: string content } => [content],
        _ => [],
    };

    [Fact]
    public async Task Tem_as_abas_IA_Estilo_Formatacao_Acentos_e_Aparencia_e_trocar_de_aba_muda_o_indice_do_ViewModel()
    {
        using var temp = new TempFolder();
        var vm = Criar(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                Assert.Equal(["IA e gasto", "Estilo", "Formatação", "Acentos", "Aparência"], tabs.Items.OfType<TabItem>().Select(t => t.Header as string));

                tabs.SelectedIndex = 3;
                Assentar(window);

                Assert.Equal(3, vm.SelectedTab);
                Assert.Equal(3, window.GetVisualDescendants().OfType<RadioButton>().Count(r => r.Classes.Contains("option")));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Clicar_no_cartao_do_modelo_escolhe_o_modelo_e_o_cartao_marcado_e_so_um()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var vm = Criar(temp);
        vm.Load(VNForge.Core.Projects.ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var cards = window.GetVisualDescendants().OfType<QualityCard>().ToList();
                Assert.Equal(["Rápida", "Equilibrada", "Caprichada"], cards.Select(c => c.Title));
                Assert.Equal([false, false, true], cards.Select(c => c.IsChecked == true));

                cards[0].IsChecked = true;
                Assentar(window);

                Assert.Equal(ClaudeModels.Haiku, vm.SelectedModel?.Id);
                Assert.Equal([true, false, false], cards.Select(c => c.IsChecked == true));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Cartao_da_chave_so_mostra_o_campo_depois_de_Trocar_chave()
    {
        using var temp = new TempFolder();
        var vm = Criar(temp, environmentKey: "sk-ant-segredo-do-teste-1234567890");

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var campo = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PasswordChar == '●');
                Assert.False(campo.IsEffectivelyVisible);
                Assert.True(Botao(window, "Testar conexão").IsEffectivelyEnabled);

                Botao(window, "Trocar chave").Command!.Execute(null);
                Assentar(window);

                Assert.True(campo.IsEffectivelyVisible);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Sem_projeto_a_tela_avisa_e_trava_so_o_que_depende_dele()
    {
        using var temp = new TempFolder();
        var vm = Criar(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var cards = window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("option")).ToList();

                Assert.All(cards, c => Assert.False(c.IsEffectivelyEnabled));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Abra um projeto no Início") == true);
                Assert.False(Botao(window, "Testar conexão").IsEffectivelyEnabled);
            },
            CancellationToken.None);
    }
}
