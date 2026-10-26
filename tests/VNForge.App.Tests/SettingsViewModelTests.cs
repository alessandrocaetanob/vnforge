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
    public void Sem_projeto_aberto_so_a_chave_e_a_concorrencia_ficam_ativas()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.False(vm.HasProject);
        Assert.Equal(2, vm.Concurrency);
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
    public async Task Gravar_avisa_a_janela_a_cada_mudanca_e_nao_avisa_ao_abrir_o_projeto_nem_sem_projeto()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        var avisos = 0;
        vm.ProjectSaved += (_, _) => avisos++;

        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "low");
        await LoadAsync(vm, summary);
        Assert.Equal(0, avisos);

        vm.SelectedEffort = vm.Efforts.Single(e => e.Id == "high");
        vm.Nfc = true;
        vm.StyleGuide = "Tom informal.";

        Assert.Equal(3, avisos);
        Assert.Equal("Alterações salvas automaticamente em vnforge.json", SettingsViewModel.AutosaveNote);
    }

    [Fact]
    public async Task Mudar_o_modelo_nao_regrava_o_formatting_json_nem_o_guia_de_estilo()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        await LoadAsync(vm, summary);

        vm.SelectedModel = vm.Models.Single(m => m.Id == ClaudeModels.Sonnet);

        Assert.False(File.Exists(Path.Combine(summary.Root, ProjectFolder.FormattingFileName)));
        Assert.False(File.Exists(Path.Combine(summary.Root, "style.md")));
        Assert.Equal(ClaudeModels.Sonnet, ProjectFolder.Open(summary.Root).LoadConfig().Ai.Model);
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
    public void Escolher_o_cartao_do_modelo_ou_da_acentuacao_muda_a_selecao_e_o_contrario()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.Models[2].IsSelected = true;
        vm.EncodingStrategies.Options.Single(e => e.Id == "cp1252").IsSelected = true;

        Assert.Equal(ClaudeModels.Haiku, vm.SelectedModel?.Id);
        Assert.Equal("cp1252", vm.EncodingStrategy);
        Assert.Single(vm.Models, m => m.IsSelected);
        Assert.Single(vm.EncodingStrategies.Options, e => e.IsSelected);

        vm.SelectedModel = vm.Models[3];

        Assert.True(vm.Models[3].IsSelected);
        Assert.False(vm.Models[2].IsSelected);
    }

    [Fact]
    public void A_aba_IA_vem_aberta()
    {
        using var temp = new TempFolder();

        Assert.Equal(0, Create(temp).SelectedTab);
    }

    [Fact]
    public void O_cartao_do_cp1252_descreve_o_patch_da_fonte_e_diz_onde_aplicar()
    {
        using var temp = new TempFolder();
        var description = Create(temp).EncodingStrategies.Options.Single(e => e.Id == "cp1252").Description;

        Assert.Contains("MS Gothic", description);
        Assert.Contains("tela Testar", description);
        Assert.DoesNotContain("lead bytes", description);
        Assert.DoesNotContain("ANSI_CHARSET", description);
    }

    [Fact]
    public void Acentos_tem_os_tres_cartoes_do_Novo_projeto_com_a_frase_na_caixa_do_jogo()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);

        Assert.Equal(["fold", "remap", "cp1252"], vm.EncodingStrategies.Options.Select(o => o.Id));
        Assert.Equal(["Sem acentos", "Com acentos, fonte modificada", "Com acentos, ajuste no jogo"], vm.EncodingStrategies.Options.Select(o => o.Title));
        Assert.Equal(wizard.EncodingStrategies.Options.Select(o => (o.Id, o.Title, o.Description)), vm.EncodingStrategies.Options.Select(o => (o.Id, o.Title, o.Description)));
        Assert.All(vm.EncodingStrategies.Options, o => Assert.NotNull(o.Layout));
        var remap = vm.EncodingStrategies.Options[1];
        Assert.False(remap.IsAvailable);
        Assert.Equal("Em breve", remap.Badge);
        Assert.Equal("fold", vm.EncodingStrategy);
        Assert.True(vm.EncodingStrategies.Options[0].IsSelected);
    }

    [Fact]
    public async Task Os_nomes_tecnicos_e_o_sjis_ficam_so_em_detalhes_tecnicos_e_nenhum_cartao_os_usa()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Equal(["fold", "remap", "cp1252", "sjis"], vm.EncodingTechnicalRows.Select(r => r.Id));
        Assert.Equal("ação → acao", vm.EncodingTechnicalRows[0].Sample);
        Assert.All(vm.EncodingTechnicalRows, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
        Assert.All(vm.EncodingStrategies.Options, o =>
        {
            Assert.DoesNotContain("(fold)", o.Title);
            Assert.DoesNotContain("cp1252", o.Title + o.Description);
            Assert.DoesNotContain("sjis", o.Title + o.Description);
        });
        Assert.Equal("Gravada no projeto: fold", vm.EncodingCurrentNote);

        var summary = OpenProject(temp);
        await LoadAsync(vm, summary);
        vm.EncodingStrategy = "cp1252";

        Assert.Equal("Gravada no projeto: cp1252", vm.EncodingCurrentNote);
    }

    [Fact]
    public void O_aviso_do_cp1252_so_aparece_com_ele()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.False(vm.ShowCp1252Warning);

        vm.EncodingStrategies.Options.Single(e => e.Id == "cp1252").IsSelected = true;

        Assert.True(vm.ShowCp1252Warning);

        vm.EncodingStrategies.Options.Single(e => e.Id == "fold").IsSelected = true;

        Assert.False(vm.ShowCp1252Warning);
        Assert.Single(vm.EncodingStrategies.Options, o => o.IsSelected);
    }

    [Fact]
    public async Task Projeto_com_sjis_nao_marca_cartao_nenhum_mantem_o_valor_e_so_troca_quando_a_pessoa_escolhe()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var folder = ProjectFolder.Open(summary.Root);
        folder.SaveConfig(folder.LoadConfig() with { Encoding = new EncodingConfig("sjis") });
        var vm = Create(temp);

        await LoadAsync(vm, summary);

        Assert.Equal("sjis", vm.EncodingStrategy);
        Assert.True(vm.ShowLegacyEncodingNote);
        Assert.DoesNotContain(vm.EncodingStrategies.Options, o => o.IsSelected);

        vm.BatchSize = 25;

        Assert.Equal("sjis", ProjectFolder.Open(summary.Root).LoadConfig().Encoding.Strategy);

        vm.EncodingStrategies.Options.Single(o => o.Id == "fold").IsSelected = true;

        Assert.False(vm.ShowLegacyEncodingNote);
        Assert.Equal("fold", ProjectFolder.Open(summary.Root).LoadConfig().Encoding.Strategy);
    }

    [Fact]
    public async Task Trocar_o_cartao_num_projeto_com_traducao_grava_a_estrategia_e_avisa_do_i_com_trema_so_no_cp1252()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        await LoadAsync(vm, summary);
        var saved = 0;
        vm.ProjectSaved += (_, _) => saved++;

        vm.EncodingStrategies.Options.Single(o => o.Id == "cp1252").IsSelected = true;

        Assert.Equal("cp1252", ProjectFolder.Open(summary.Root).LoadConfig().Encoding.Strategy);
        Assert.True(vm.ShowCp1252Warning);
        Assert.Equal(1, saved);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task O_selo_Recomendada_para_este_jogo_aparece_com_uma_engine_que_tem_patch_de_executavel()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var withPatch = Create(temp);
        var withoutPatch = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("outra")), _ => null, engines: [new FakeEngine()]);

        await LoadAsync(withPatch, summary);
        await LoadAsync(withoutPatch, summary);

        Assert.Equal("Recomendada para este jogo", withPatch.EncodingStrategies.Options.Single(o => o.Id == "cp1252").Badge);
        Assert.Equal(string.Empty, withoutPatch.EncodingStrategies.Options.Single(o => o.Id == "cp1252").Badge);
    }

    [Fact]
    public void O_botao_de_teste_de_acentos_pede_a_tela_Testar_no_jogo()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var requested = 0;
        vm.TestAccentsRequested += (_, _) => requested++;

        vm.OpenAccentTestCommand.Execute(null);

        Assert.Equal(1, requested);
    }

    [Fact]
    public async Task Estimativa_por_modelo_usa_as_linhas_pendentes_do_projeto_e_o_preco_de_cada_modelo()
    {
        using var temp = new TempFolder();
        var statuses = Enumerable.Repeat(TranslationStatus.Untranslated, 2000).Concat([TranslationStatus.Approved, TranslationStatus.Machine]).ToArray();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        var pending = ProjectFolder.Open(root).LoadAllText().Values.SelectMany(u => u).Where(u => u.Status == TranslationStatus.Untranslated).ToList();
        var expected = CostEstimator.EstimateForModels(pending, ClaudeModels.All);
        var vm = Create(temp);

        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;

        Assert.Equal(2000, vm.PendingLines);
        foreach (var model in vm.Models)
        {
            var reais = CostEstimator.Brl(expected[model.Id], ExchangeRate.Placeholder);
            var text = $"R$ {Math.Round(reais, 2, MidpointRounding.AwayFromZero).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')}";
            Assert.Equal(text, model.Estimate);
            Assert.Equal($"{model.Use} · estimativa para as 2.000 pendentes: {text}", model.Detail);
        }
    }

    [Fact]
    public async Task Sem_linhas_pendentes_o_cartao_diz_isso_e_sem_projeto_mostra_so_o_uso()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        Assert.All(vm.Models, m => Assert.Equal(m.Use, m.Detail));

        vm.Load(OpenProject(temp));
        await vm.EstimateLoading;

        Assert.Equal(0, vm.PendingLines);
        Assert.All(vm.Models, m => Assert.Equal($"{m.Use} · nenhuma linha pendente", m.Detail));

        vm.Load(null);
        await vm.EstimateLoading;

        Assert.All(vm.Models, m => Assert.Equal(m.Use, m.Detail));
    }

    [Fact]
    public async Task Valor_abaixo_de_um_centavo_aparece_como_menos_de_um_centavo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Pequeno", TranslationStatus.Untranslated);
        var vm = Create(temp);

        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;

        Assert.Equal("menos de R$ 0,01", vm.Models.Single(m => m.Id == ClaudeModels.Haiku).Estimate);
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
    public async Task Previa_aplica_as_regras_ligadas_na_hora_e_o_texto_do_projeto_nao_muda()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        var vm = Create(temp);
        await LoadAsync(vm, summary);
        var textFile = Path.Combine(summary.Root, "text", "yst00001.json");
        var textBefore = File.ReadAllBytes(textFile);
        vm.PreviewBefore = "Espera...  “Sim” ;";
        Assert.Equal("Espera...  “Sim” ;", vm.PreviewAfter);

        vm.CollapseSpaces = true;
        Assert.Equal("Espera... “Sim” ;", vm.PreviewAfter);

        vm.SpaceBeforePunctuation = true;
        Assert.Equal("Espera... “Sim”;", vm.PreviewAfter);

        vm.SelectedEllipsis = vm.EllipsisStyles.Single(e => e.Value == EllipsisStyle.Character);
        vm.SelectedQuotes = vm.QuoteStyles.Single(q => q.Value == QuoteStyle.Ascii);
        Assert.Equal("Espera… \"Sim\";", vm.PreviewAfter);
        Assert.Equal(textBefore, File.ReadAllBytes(textFile));
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
    public async Task Formatting_json_invalido_aparece_como_erro_em_portugues_e_nao_derruba_a_tela()
    {
        using var temp = new TempFolder();
        var summary = OpenProject(temp);
        File.WriteAllText(Path.Combine(summary.Root, ProjectFolder.FormattingFileName), "{ não é json");
        var vm = Create(temp);

        await LoadAsync(vm, summary);

        Assert.StartsWith("Não foi possível ler os ajustes do projeto", vm.ErrorMessage);
        Assert.False(vm.HasProject);

        vm.Nfc = true;

        Assert.Equal("{ não é json", File.ReadAllText(Path.Combine(summary.Root, ProjectFolder.FormattingFileName)));
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

    [Fact]
    public void Cartao_da_chave_diz_de_onde_ela_vem_sem_mostrar_o_valor()
    {
        using var temp = new TempFolder();
        var vault = new MemoryApiKeyStore();
        var vm = Create(temp, vault);
        Assert.Equal("Não configurada · use Adicionar chave para guardá-la no cofre do Windows", vm.KeyDetail);
        Assert.Equal("Adicionar chave", vm.ChangeKeyLabel);

        vault.Save(SecretKey);
        vm = Create(temp, vault);
        Assert.Equal("Configurada · guardada no cofre do Windows, fora do projeto", vm.KeyDetail);
        Assert.Equal("Trocar chave", vm.ChangeKeyLabel);

        vm = Create(temp, vault, environmentKey: SecretKey);
        Assert.Equal("Configurada · lida de uma variável de ambiente, que vale mais que o cofre", vm.KeyDetail);
        Assert.DoesNotContain(SecretKey, vm.KeyDetail);
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

    [Fact]
    public void Apagar_a_chave_tambem_fecha_o_campo()
    {
        using var temp = new TempFolder();
        var vault = new MemoryApiKeyStore();
        vault.Save(SecretKey);
        var vm = Create(temp, vault);
        vm.ChangeKeyCommand.Execute(null);

        vm.ClearKeyCommand.Execute(null);

        Assert.False(vm.IsChangingKey);
        Assert.Equal("Adicionar chave", vm.ChangeKeyLabel);
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
    public async Task Testar_conexao_usa_a_chave_resolvida_o_provedor_falso_e_avisa_que_deu_certo()
    {
        using var temp = new TempFolder();
        var client = new StubClient(_ => Task.FromResult(new ClaudeReply("end_turn", "{\"ok\":true}")));
        string? keyUsed = null;
        var vm = Create(temp, environmentKey: SecretKey, client: key =>
        {
            keyUsed = key;
            return client;
        });

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(SecretKey, keyUsed);
        Assert.True(vm.ConnectionOk);
        Assert.StartsWith("Conexão funcionando", vm.ConnectionMessage);
        Assert.Single(client.Requests);
        Assert.False(vm.IsTestingConnection);
        Assert.DoesNotContain(SecretKey, vm.ConnectionMessage);
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
    public async Task Testar_conexao_trava_o_botao_enquanto_espera_e_nao_dispara_duas_vezes()
    {
        using var temp = new TempFolder();
        var gate = new TaskCompletionSource<ClaudeReply>();
        var client = new StubClient(_ => gate.Task);
        var vm = Create(temp, environmentKey: SecretKey, client: _ => client);

        var running = vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestingConnection);
        Assert.False(vm.TestConnectionCommand.CanExecute(null));
        Assert.Null(vm.ConnectionMessage);

        gate.SetResult(new ClaudeReply("end_turn", string.Empty));
        await running;

        Assert.False(vm.IsTestingConnection);
        Assert.True(vm.TestConnectionCommand.CanExecute(null));
        Assert.Single(client.Requests);
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

    [Fact]
    public async Task Trocar_ou_apagar_a_chave_limpa_o_resultado_do_teste_anterior()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, client: _ => new StubClient(_ => Task.FromResult(new ClaudeReply("end_turn", string.Empty))));
        vm.NewApiKey = SecretKey;
        vm.SaveKeyCommand.Execute(null);
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ConnectionMessage);

        vm.ClearKeyCommand.Execute(null);

        Assert.Null(vm.ConnectionMessage);
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
