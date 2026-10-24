using VNForge.AI.Claude;
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

    private static SettingsViewModel Create(TempFolder temp, IApiKeyStore? vault = null, string? environmentKey = null) =>
        new(vault ?? new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), name => name == ApiKeyResolver.EnvironmentVariable ? environmentKey : null);

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
        Assert.All(vm.Models, m => Assert.Matches(@"^US\$ \d+,\d{2} / US\$ \d+,\d{2} por milhão de tokens$", m.Price));
        Assert.Equal("US$ 4,00 / US$ 20,00 por milhão de tokens", vm.Models[0].Price);

        var claude = Assert.Single(vm.Providers, p => p.IsAvailable);
        Assert.Equal("Claude (Anthropic)", claude.Name);
        Assert.Equal(["OpenAI", "Gemini", "Ollama / LM Studio"], vm.Providers.Where(p => !p.IsAvailable).Select(p => p.Name));
        Assert.All(vm.Providers.Where(p => !p.IsAvailable), p => Assert.Equal($"{p.Name} · em breve", p.Label));
    }

    [Fact]
    public void Sem_projeto_aberto_so_a_chave_e_a_concorrencia_ficam_ativas()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.False(vm.HasProject);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.Equal(2, vm.Concurrency);
    }

    [Fact]
    public void Abrir_projeto_carrega_modelo_esforco_lote_acentuacao_formatacao_e_guia_de_estilo()
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

        vm.Load(summary);

        Assert.True(vm.HasProject);
        Assert.Equal(ClaudeModels.Sonnet, vm.SelectedModel?.Id);
        Assert.Equal("high", vm.SelectedEffort?.Id);
        Assert.Equal(40, vm.BatchSize);
        Assert.Equal("remap", vm.SelectedEncoding?.Id);
        Assert.True(vm.WrapEnabled);
        Assert.Equal(40, vm.WrapMaxWidth);
        Assert.Equal(2, vm.WrapMaxLines);
        Assert.Equal("Tom informal.", vm.StyleGuide);
    }

    [Fact]
    public void Salvar_grava_so_no_projeto_e_preserva_o_resto_do_vnforge_json()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        vm.Load(summary);

        vm.SelectedModel = vm.Models.Single(m => m.Id == ClaudeModels.Haiku);
        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "low");
        vm.BatchSize = 25;
        vm.SelectedEncoding = vm.Encodings.Single(e => e.Id == "cp1252");
        vm.StyleGuide = "Trate por você.";
        vm.SaveProjectCommand.Execute(null);

        var folder = ProjectFolder.Open(summary.Root);
        var config = folder.LoadConfig();
        Assert.Equal(new AiConfig("anthropic", ClaudeModels.Haiku, "low", 25), config.Ai);
        Assert.Equal("cp1252", config.Encoding.Strategy);
        Assert.Equal("Minha VN", config.Name);
        Assert.Equal("Trate por você.", File.ReadAllText(Path.Combine(summary.Root, "style.md")));
        Assert.Equal("Ajustes do projeto salvos.", vm.StatusMessage);
    }

    [Fact]
    public void Guia_de_estilo_vazio_remove_o_style_md()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var style = Path.Combine(summary.Root, "style.md");
        File.WriteAllText(style, "Antigo.");
        var vm = Create(temp);
        vm.Load(summary);

        vm.StyleGuide = "   ";
        vm.SaveProjectCommand.Execute(null);

        Assert.False(File.Exists(style));
    }

    [Theory]
    [InlineData(5, 20)]
    [InlineData(200, 50)]
    public void Lote_fora_do_intervalo_volta_para_o_limite(int pedido, int esperado)
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.BatchSize = pedido;

        Assert.Equal(esperado, vm.BatchSize);
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
    public void Previa_aplica_as_regras_ligadas_na_hora_e_o_texto_do_projeto_nao_muda()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        vm.Load(summary);
        vm.PreviewBefore = "Espera...  “Sim” ;";
        Assert.Equal("Espera...  “Sim” ;", vm.PreviewAfter);

        vm.CollapseSpaces = true;
        Assert.Equal("Espera... “Sim” ;", vm.PreviewAfter);

        vm.SpaceBeforePunctuation = true;
        Assert.Equal("Espera... “Sim”;", vm.PreviewAfter);

        vm.SelectedEllipsis = vm.EllipsisStyles.Single(e => e.Value == EllipsisStyle.Character);
        vm.SelectedQuotes = vm.QuoteStyles.Single(q => q.Value == QuoteStyle.Ascii);
        Assert.Equal("Espera… \"Sim\";", vm.PreviewAfter);
        Assert.False(File.Exists(Path.Combine(summary.Root, ProjectFolder.FormattingFileName)));
    }

    [Fact]
    public void Previa_mostra_a_quebra_de_linha_e_avisa_quando_a_caixa_passa_do_limite()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        vm.PreviewBefore = "um dois tres quatro cinco seis";

        vm.WrapEnabled = true;
        vm.WrapMaxWidth = 10;
        vm.WrapMaxLines = 2;

        Assert.Contains('\n', vm.PreviewAfter);
        Assert.NotEmpty(vm.PreviewWarnings);
    }

    [Fact]
    public void Salvar_grava_o_formatting_json_e_mantem_as_regras_que_a_tela_nao_edita()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        folder.SaveFormatting(FormattingProfile.Default with { Kinds = [TextUnitKind.Dialogue], Typography = new TypographyRule(Dash: '―') });
        var vm = Create(temp);
        vm.Load(summary);

        vm.Nfc = true;
        vm.SpaceAfterPunctuation = true;
        vm.SaveProjectCommand.Execute(null);

        var saved = folder.LoadFormatting();
        Assert.True(saved.Normalization.Nfc);
        Assert.True(saved.Spacing.SpaceAfterPunctuation);
        Assert.Equal([TextUnitKind.Dialogue], saved.Kinds);
        Assert.Equal('―', saved.Typography.Dash);
    }

    [Fact]
    public void Formatting_json_invalido_aparece_como_erro_em_portugues_e_nao_derruba_a_tela()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        File.WriteAllText(Path.Combine(summary.Root, ProjectFolder.FormattingFileName), "{ não é json");
        var vm = Create(temp);

        vm.Load(summary);

        Assert.StartsWith("Não foi possível ler os ajustes do projeto", vm.ErrorMessage);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
    }

    [Fact]
    public void Salvar_chave_guarda_no_cofre_limpa_o_campo_e_nao_deixa_rastro_no_projeto()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vault = new MemoryApiKeyStore();
        var vm = Create(temp, vault);
        vm.Load(summary);
        vm.SelectedModel = vm.Models[1];
        vm.StyleGuide = "Guia.";
        vm.SaveProjectCommand.Execute(null);

        vm.NewApiKey = $"  {SecretKey}  ";
        vm.SaveKeyCommand.Execute(null);

        Assert.Equal(SecretKey, vault.Load());
        Assert.Equal(string.Empty, vm.NewApiKey);
        Assert.Equal("Chave do Claude no Cofre do Windows", vm.KeyStatus);
        Assert.True(vm.KeyConfigured);
        Assert.DoesNotContain(SecretKey, vm.KeyStatus + vm.StatusMessage + vm.ErrorMessage + vm.KeyMessage);
        AssertNoFileContains(temp.Path, SecretKey);
    }

    [Fact]
    public void Chave_pelo_cofre_dpapi_real_tambem_nao_aparece_no_projeto()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = new SettingsViewModel(new DpapiApiKeyStore(temp.SubFolder("config")), new AppSettingsFile(temp.SubFolder("config")), _ => null);
        vm.Load(summary);

        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);
        vm.SaveProjectCommand.Execute(null);

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
    public void Mudar_a_chave_avisa_a_janela_para_atualizar_a_barra_de_status()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var avisos = 0;
        vm.KeyChanged += (_, _) => avisos++;

        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);
        vm.ClearKeyCommand.Execute(null);

        Assert.Equal(2, avisos);
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
    [Fact]
    public void Sem_arquivo_devolve_o_padrao()
    {
        using var temp = new TempFolder();

        Assert.Equal(AppSettings.Default, new AppSettingsFile(temp.Path).Load());
    }

    [Fact]
    public void Arquivo_ilegivel_vale_como_padrao_e_o_proximo_Save_o_substitui()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.Path);
        File.WriteAllText(file.Path, "lixo");

        Assert.Equal(AppSettings.Default, file.Load());
        file.Save(new AppSettings(3));

        Assert.Equal(3, file.Load().Concurrency);
    }

    [Fact]
    public void Concorrencia_fora_do_intervalo_cai_no_padrao()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.Path);
        File.WriteAllText(file.Path, "{\"concurrency\": 99}");

        Assert.Equal(AppSettings.Default.Concurrency, file.Load().Concurrency);
    }
}
