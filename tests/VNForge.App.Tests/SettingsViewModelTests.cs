using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
public class SettingsViewModelTests
{
    private const string SecretKey = "sk-ant-segredo-do-teste-1234567890";

    private static SettingsViewModel Create(TempFolder temp, IApiKeyStore? vault = null, string? environmentKey = null, Func<string, IClaudeMessageClient>? client = null) =>
        new(vault ?? new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), name => name == ApiKeyResolver.EnvironmentVariable ? environmentKey : null, client);

    /// <summary>Abre o projeto e espera a estimativa, que lê <c>text/*.json</c> em segundo plano: sem isso a pasta temporária some com o arquivo ainda aberto.</summary>
    private static async Task LoadAsync(SettingsViewModel vm, ProjectSummary summary)
    {
        vm.Load(summary);
        await vm.EstimateLoading;
    }

    private static ProjectSummary OpenProject(TempFolder temp, string name = "Minha VN")
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), name, TranslationStatus.Approved);
        return ProjectSummaryReader.Read(root);
    }

    [Fact]
    public async Task Cada_mudanca_e_gravada_na_hora_so_no_projeto_e_preserva_o_resto_do_vnforge_json()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        await LoadAsync(vm, summary);

        vm.SelectedModel = vm.Models.Single(m => m.Id == ClaudeModels.Haiku);
        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "low");
        vm.BatchSize = 25;
        vm.EncodingStrategies.Options.Single(o => o.Id == "cp1252").IsSelected = true;
        vm.StyleGuide = "Trate por você.";

        var folder = ProjectFolder.Open(summary.Root);
        var config = folder.LoadConfig();
        Assert.Equal(new AiConfig("anthropic", ClaudeModels.Haiku, "low", 25), config.Ai);
        Assert.Equal("cp1252", config.Encoding.Strategy);
        Assert.Equal("Minha VN", config.Name);
        Assert.Equal("Trate por você.", File.ReadAllText(Path.Combine(summary.Root, "style.md")));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Falha_ao_gravar_vira_erro_em_portugues_e_nao_derruba_a_tela()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        await LoadAsync(vm, summary);
        var avisos = 0;
        vm.ProjectSaved += (_, _) => avisos++;
        var config = Path.Combine(summary.Root, ProjectFolder.ConfigFileName);
        File.Delete(config);
        Directory.CreateDirectory(config);

        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "high");

        Assert.StartsWith("Não foi possível salvar os ajustes do projeto", vm.ErrorMessage);
        Assert.Equal(0, avisos);
    }

    [Fact]
    public async Task Salvar_grava_o_formatting_json_e_mantem_as_regras_que_a_tela_nao_edita()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        folder.SaveFormatting(FormattingProfile.Default with { Kinds = [TextUnitKind.Dialogue], Typography = new TypographyRule(Dash: '―') });
        var vm = Create(temp);
        await LoadAsync(vm, summary);

        vm.Nfc = true;
        vm.SpaceAfterPunctuation = true;

        var saved = folder.LoadFormatting();
        Assert.True(saved.Normalization.Nfc);
        Assert.True(saved.Spacing.SpaceAfterPunctuation);
        Assert.Equal([TextUnitKind.Dialogue], saved.Kinds);
        Assert.Equal('―', saved.Typography.Dash);
    }

    [Fact]
    public async Task Salvar_chave_guarda_no_cofre_limpa_o_campo_e_nao_deixa_rastro_no_projeto()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vault = new MemoryApiKeyStore();
        var vm = Create(temp, vault);
        await LoadAsync(vm, summary);
        vm.SelectedModel = vm.Models[1];
        vm.StyleGuide = "Guia.";

        vm.NewApiKey = $"  {SecretKey}  ";
        vm.SaveKeyCommand.Execute(null);

        Assert.Equal(SecretKey, vault.Load());
        Assert.Equal(string.Empty, vm.NewApiKey);
        Assert.Equal("Chave do Claude no Cofre do Windows", vm.KeyStatus);
        Assert.True(vm.KeyConfigured);
        Assert.DoesNotContain(SecretKey, vm.KeyStatus + vm.KeyDetail + vm.ErrorMessage + vm.KeyMessage);
        AssertNoFileContains(temp.Path, SecretKey);
    }

    [Fact]
    public async Task Chave_pelo_cofre_dpapi_real_tambem_nao_aparece_no_projeto()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = new SettingsViewModel(new DpapiApiKeyStore(temp.SubFolder("config")), new AppSettingsFile(temp.SubFolder("config")), _ => null);
        await LoadAsync(vm, summary);

        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);
        vm.StyleGuide = "Guia.";

        Assert.Equal(SecretKey, new DpapiApiKeyStore(temp.SubFolder("config")).Load());
        AssertNoFileContains(summary.Root, SecretKey);
        AssertNoFileContains(temp.SubFolder("config"), SecretKey);
    }

    [Fact]
    public void Chave_da_variavel_de_ambiente_vence_o_cofre_e_o_status_diz_isso()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, environmentKey: SecretKey);

        Assert.Equal("Chave do Claude na variável ANTHROPIC_API_KEY", vm.KeyStatus);
        Assert.DoesNotContain(SecretKey, vm.KeyStatus);
    }

    private sealed class StubClient(Func<ClaudeRequest, Task<ClaudeReply>> reply) : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return reply(request);
        }
    }

    private sealed class HangingClient : IClaudeMessageClient
    {
        public async Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new ClaudeReply(null, string.Empty);
        }
    }

    private static void AssertNoFileContains(string root, string secret)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var content = File.ReadAllBytes(file);
            Assert.False(content.AsSpan().IndexOf(bytes) >= 0, $"A chave apareceu em {file}.");
        }
    }

    private sealed class ThrowingVault : IApiKeyStore
    {
        public string? Load() => null;

        public void Save(string apiKey) => throw new AiSettingsException("o cofre não está disponível.");

        public bool Delete() => false;
    }

}

