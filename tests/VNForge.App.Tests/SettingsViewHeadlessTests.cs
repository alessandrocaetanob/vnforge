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
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == texto || Avalonia.Automation.AutomationProperties.GetName(b) == texto);

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
                try
                {
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
                }
                finally
                {
                    window.Close();
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
}
