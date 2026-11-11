using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

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
    public void Lista_os_quatro_modelos_com_preco_e_marca_os_outros_provedores_como_em_breve()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Equal(ClaudeModels.All, vm.Models.Select(m => m.Id));
        Assert.Equal(["US$ 4 / 20 / M tokens", "US$ 2 / 10 / M tokens", "US$ 0,10 / 0,50 / M tokens", "US$ 10 / 50 / M tokens"], vm.Models.Select(m => m.Price));
        Assert.Equal(["Claude Opus 5.5", "Claude Sonnet 5.5", "Claude Haiku 5.5", "Claude Fable 5.1"], vm.Models.Select(m => m.Name));
        Assert.All(vm.Models, m => Assert.False(string.IsNullOrWhiteSpace(m.Use)));

        var claude = Assert.Single(vm.Providers, p => p.IsAvailable);
        Assert.Equal("Claude (Anthropic)", claude.Name);
        Assert.Equal(["OpenAI", "Gemini", "Ollama / LM Studio"], vm.Providers.Where(p => !p.IsAvailable).Select(p => p.Name));
        Assert.All(vm.Providers.Where(p => !p.IsAvailable), p => Assert.Equal($"{p.Name} · em breve", p.Label));
    }

    [Fact]
    public async Task Abrir_projeto_carrega_modelo_esforco_lote_acentuacao_formatacao_e_guia_de_estilo()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        folder.SaveConfig(folder.LoadConfig() with
        {
            Ai = new AiConfig("anthropic", ClaudeModels.Sonnet, "high", 40),
            Encoding = new EncodingConfig("remap"),
        });
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(true, 40, 2) });
        File.WriteAllText(Path.Combine(summary.Root, "style.md"), "Tom informal.");
        var vm = Create(temp);

        await LoadAsync(vm, summary);

        Assert.True(vm.HasProject);
        Assert.Equal(ClaudeModels.Sonnet, vm.SelectedModel?.Id);
        Assert.Equal("high", vm.SelectedEffort?.Id);
        Assert.Equal(40, vm.BatchSize);
        Assert.Equal("remap", vm.EncodingStrategy);
        Assert.True(vm.EncodingStrategies.Options.Single(o => o.Id == "remap").IsSelected);
        Assert.True(vm.WrapEnabled);
        Assert.Equal(40, vm.WrapMaxWidth);
        Assert.Equal(2, vm.WrapMaxLines);
        Assert.Equal("Tom informal.", vm.StyleGuide);
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
    public async Task Gravar_as_configuracoes_preserva_a_janela_de_contexto_do_vnforge_json()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        var original = folder.LoadConfig();
        folder.SaveConfig(original with { Ai = original.Ai with { ContextBefore = 15, ContextAfter = 5 } });
        var vm = Create(temp);
        await LoadAsync(vm, ProjectSummaryReader.Read(summary.Root));

        vm.BatchSize = 25;
        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "low");

        var saved = folder.LoadConfig().Ai;
        Assert.Equal(25, saved.BatchSize);
        Assert.Equal(15, saved.ContextBefore);
        Assert.Equal(5, saved.ContextAfter);
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
    public async Task Abrir_outro_projeto_antes_da_estimativa_acabar_nao_deixa_o_numero_do_anterior()
    {
        using var temp = new TempFolder();
        var big = SampleProjects.Create(temp.SubFolder("grande"), "Grande", Enumerable.Repeat(TranslationStatus.Untranslated, 500).ToArray());
        var small = SampleProjects.Create(temp.SubFolder("pequeno"), "Pequeno", TranslationStatus.Untranslated);
        var vm = Create(temp);

        vm.Load(ProjectSummaryReader.Read(big));
        vm.Load(ProjectSummaryReader.Read(small));
        await vm.EstimateLoading;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, vm.PendingLines);
    }

    [Fact]
    public async Task Guia_de_estilo_vazio_remove_o_style_md()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var style = Path.Combine(summary.Root, "style.md");
        File.WriteAllText(style, "Antigo.");
        var vm = Create(temp);
        await LoadAsync(vm, summary);

        vm.StyleGuide = "   ";

        Assert.False(File.Exists(style));
    }

    [Fact]
    public void Concorrencia_fica_no_perfil_do_usuario_e_volta_na_proxima_vez()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.Concurrency = 4;
        vm.Concurrency = 0;
        Assert.Equal(1, vm.Concurrency);
        vm.Concurrency = 4;

        Assert.Equal(4, Create(temp).Concurrency);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("duas palavras")]
    public void Chave_vazia_ou_com_espacos_e_recusada(string digitada)
    {
        using var temp = new TempFolder();
        var vault = new MemoryApiKeyStore();
        var vm = Create(temp, vault);

        vm.NewApiKey = digitada;

        Assert.False(vm.SaveKeyCommand.CanExecute(null));
        vm.SaveKeyCommand.Execute(null);
        Assert.Null(vault.Load());
    }

    [Fact]
    public void Apagar_chave_remove_do_cofre_e_avisa_o_que_sobrou()
    {
        using var temp = new TempFolder();
        var vault = new MemoryApiKeyStore();
        vault.Save(SecretKey);
        var vm = Create(temp, vault);
        Assert.True(vm.KeyConfigured);

        vm.ClearKeyCommand.Execute(null);

        Assert.Null(vault.Load());
        Assert.False(vm.KeyConfigured);
        Assert.Equal("Chave do Claude não configurada: a tradução com IA fica desligada", vm.KeyStatus);
    }

    [Fact]
    public void Chave_da_variavel_de_ambiente_vence_o_cofre_e_o_status_diz_isso()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, environmentKey: SecretKey);

        Assert.Equal("Chave do Claude na variável ANTHROPIC_API_KEY", vm.KeyStatus);
        Assert.DoesNotContain(SecretKey, vm.KeyStatus);
    }

    [Fact]
    public void Falha_do_cofre_vira_mensagem_em_portugues_sem_a_chave()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, new ThrowingVault());

        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);

        Assert.Equal("Não foi possível guardar a chave: o cofre não está disponível.", vm.KeyMessage);
        Assert.DoesNotContain(SecretKey, vm.KeyMessage);
    }

    [Fact]
    public void Trocar_chave_abre_o_campo_cancelar_fecha_e_guardar_fecha_e_limpa()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        Assert.False(vm.IsChangingKey);

        vm.ChangeKeyCommand.Execute(null);
        Assert.True(vm.IsChangingKey);
        vm.NewApiKey = SecretKey;
        vm.CancelChangeKeyCommand.Execute(null);
        Assert.False(vm.IsChangingKey);
        Assert.Equal(string.Empty, vm.NewApiKey);

        vm.ChangeKeyCommand.Execute(null);
        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);

        Assert.False(vm.IsChangingKey);
        Assert.Equal("Configurada · guardada no cofre do Windows, fora do projeto", vm.KeyDetail);
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

    [Fact]
    public async Task Testar_conexao_sem_chave_nao_chama_a_api_e_manda_configurar()
    {
        using var temp = new TempFolder();
        var chamadas = 0;
        var vm = Create(temp, client: _ =>
        {
            chamadas++;
            return new StubClient(_ => Task.FromResult(new ClaudeReply("end_turn", string.Empty)));
        });

        Assert.False(vm.TestConnectionCommand.CanExecute(null));
        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(0, chamadas);
        Assert.False(vm.ConnectionOk);
        Assert.Equal("Configure a chave da API antes de testar a conexão.", vm.ConnectionMessage);
    }

    [Fact]
    public async Task Testar_conexao_com_falha_mostra_a_frase_em_portugues_sem_a_chave()
    {
        using var temp = new TempFolder();
        var client = new StubClient(_ => throw new HttpRequestException($"falhou com {SecretKey}"));
        var vm = Create(temp, environmentKey: SecretKey, client: _ => client);

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.ConnectionOk);
        Assert.Contains("rede", vm.ConnectionMessage);
        Assert.DoesNotContain(SecretKey, vm.ConnectionMessage);
    }

    [Fact]
    public async Task Testar_conexao_que_nao_responde_desiste_no_prazo_e_solta_o_botao()
    {
        using var temp = new TempFolder();
        var client = new HangingClient();
        var vm = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => SecretKey, _ => client)
        {
            ConnectionTimeout = TimeSpan.FromMilliseconds(50),
        };

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.ConnectionOk);
        Assert.Contains("tempo esgotado", vm.ConnectionMessage);
        Assert.False(vm.IsTestingConnection);
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

    private static ProjectSummary OpenProjectWithTitle(TempFolder temp, string? translation = null, string strategy = "cp1252")
    {
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        var config = folder.LoadConfig();
        folder.SaveConfig(config with
        {
            Encoding = new EncodingConfig(strategy),
            Game = config.Game with { Title = new GameTitleConfig("原題", translation) },
        });
        return summary;
    }

    [Fact]
    public async Task Abrir_o_projeto_nao_regrava_o_titulo()
    {
        using var temp = new TempFolder();
        var summary = OpenProjectWithTitle(temp, "Meu Jogo");
        var vm = Create(temp);
        var avisos = 0;
        vm.ProjectSaved += (_, _) => avisos++;

        await LoadAsync(vm, summary);

        Assert.Equal(0, avisos);
    }
}

public class MemoryApiKeyStoreTests
{
    [Fact]
    public void Guarda_le_e_apaga()
    {
        var store = new MemoryApiKeyStore();
        Assert.Null(store.Load());
        Assert.False(store.Delete());

        store.Save("abc");

        Assert.Equal("abc", store.Load());
        Assert.True(store.Delete());
        Assert.Null(store.Load());
    }
}

public class AppSettingsFileTests
{
}
