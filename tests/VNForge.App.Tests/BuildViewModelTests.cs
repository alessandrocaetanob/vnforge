using VNForge.App.Services;
using VNForge.App.ViewModels;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class BuildViewModelTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";
    private const string Original = "original";
    private const string Traduzido = "traduzido";

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly ProjectFolder folder;
    private readonly RecordingStarter starter = new();

    public BuildViewModelTests()
    {
        game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, Ysbin), Original);
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        folder.SaveState(new PipelineState([], new Dictionary<string, StageState>(), game));
    }

    public void Dispose() => temp.Dispose();

    private string GameFile => Path.Combine(game, Ysbin);

    private void PutBuild(string name = Ysbin, string content = Traduzido)
    {
        var path = Path.Combine(folder.BuildDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<BuildViewModel> OpenAsync()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], starter);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task Sem_build_nada_pode_ser_injetado_nem_restaurado()
    {
        var vm = await OpenAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.False(vm.RestoreCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.Contains("Ainda não há build", vm.PlanMessage);
        Assert.Equal(game, vm.GameFolder);
        Assert.Contains("Nenhum backup", vm.BackupMessage);
        Assert.Equal(folder.BackupDirectory, vm.BackupLocation);
    }

    [Fact]
    public async Task Sem_projeto_nenhum_comando_roda()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [], starter);

        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.False(vm.RestoreCommand.CanExecute(null));
        Assert.False(vm.LaunchCommand.CanExecute(null));
        await vm.ReloadAsync();
    }

    [Fact]
    public async Task Com_build_lista_o_que_muda_no_jogo_sem_gravar_nada()
    {
        PutBuild();
        PutBuild("pac/extra.ypf", "novo");

        var vm = await OpenAsync();

        Assert.True(vm.InjectCommand.CanExecute(null));
        Assert.Equal(2, vm.Changes.Count);
        Assert.Contains(vm.Changes, c => c.Path == Ysbin && c.Kind == "Substitui o original");
        Assert.Contains(vm.Changes, c => c.Path == "pac/extra.ypf" && c.Kind == "Arquivo novo");
        Assert.Contains("2 arquivo(s)", vm.PlanMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Build_igual_ao_jogo_nao_tem_o_que_injetar()
    {
        PutBuild(content: Original);

        var vm = await OpenAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.Empty(vm.Changes);
        Assert.Contains("já estão iguais", vm.PlanMessage);
    }

    [Fact]
    public async Task Injetar_pede_confirmacao_e_so_grava_depois_de_confirmar()
    {
        PutBuild();
        var vm = await OpenAsync();

        vm.InjectCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("1 arquivo(s)", vm.ConfirmationText);
        Assert.Equal(new PlannedChange(Ysbin, "Substitui o original"), Assert.Single(vm.ConfirmationFiles));
        Assert.Equal(Original, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirming);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
        var backup = Assert.Single(Directory.GetDirectories(folder.BackupDirectory));
        Assert.Equal(Original, File.ReadAllText(Path.Combine(backup, Ysbin)));
        Assert.Equal(backup, vm.BackupLocation);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Backup: ", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Message.Contains("Injetado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.RestoreCommand.CanExecute(null));
        Assert.False(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Injecao_de_um_projeto_que_ja_foi_trocado_nao_avisa_no_projeto_novo()
    {
        PutBuild();
        var vm = await OpenAsync();
        var avisos = new List<BuildNotice>();
        vm.NoticeRaised += (_, notice) => avisos.Add(notice);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildViewModel.IsBusy) && !vm.IsBusy)
            {
                vm.Project = null;
            }
        };
        vm.InjectCommand.Execute(null);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Empty(avisos);
    }

    [Fact]
    public async Task Cancelar_a_confirmacao_nao_grava_nada()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);

        vm.CancelConfirmationCommand.Execute(null);

        Assert.False(vm.IsConfirming);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.False(Directory.Exists(folder.BackupDirectory) && Directory.GetDirectories(folder.BackupDirectory).Length > 0);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_pede_confirmacao_e_devolve_o_original()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        vm.RestoreCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("Restaurar", vm.ConfirmationText);
        Assert.Equal(new PlannedChange(Ysbin, "Devolve o original"), Assert.Single(vm.ConfirmationFiles));
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.Contains(vm.Log, l => l.Message.Contains("Restaurado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_remove_o_arquivo_que_a_injecao_criou()
    {
        PutBuild("pac/extra.ypf", "novo");
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var extra = Path.Combine(game, "pac", "extra.ypf");
        Assert.True(File.Exists(extra));

        vm.RestoreCommand.Execute(null);
        Assert.Equal(new PlannedChange("pac/extra.ypf", "Remove o arquivo criado pela injeção"), Assert.Single(vm.ConfirmationFiles));
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(File.Exists(extra));
        Assert.Contains(vm.Log, l => l.Message.Contains("removido: 1 arquivo(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_da_injecao_vira_mensagem_e_o_jogo_fica_como_estava()
    {
        PutBuild();
        var vm = await OpenAsync();
        File.WriteAllText(GameFile, "mudou depois do plano");

        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal("mudou depois do plano", File.ReadAllText(GameFile));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Falha_ao_copiar_o_log_vira_mensagem_na_tela()
    {
        var vm = await OpenAsync();

        vm.ReportCopyFailure("clipboard travado");

        Assert.Contains("clipboard travado", vm.ErrorMessage);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Não foi possível copiar o log: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_que_mudou_depois_do_plano_confirmado_nao_e_gravado_no_jogo()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        PutBuild(content: "outro conteúdo, que a tela não mostrou");

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("mudou depois da conferência", vm.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.False(Directory.Exists(folder.BackupDirectory) && Directory.GetDirectories(folder.BackupDirectory).Length > 0);
    }

    [Fact]
    public async Task Backup_mais_novo_que_o_confirmado_nao_e_ignorado_na_restauracao()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        vm.RestoreCommand.Execute(null);
        var newer = Path.Combine(folder.BackupDirectory, "9999-12-31_2359");
        Directory.CreateDirectory(newer);
        var manifest = Directory.GetDirectories(folder.BackupDirectory).Where(d => d != newer).Single();
        File.Copy(Path.Combine(manifest, "vnforge-backup.json"), Path.Combine(newer, "vnforge-backup.json"));
        File.Copy(Path.Combine(manifest, Ysbin), Path.Combine(newer, Ysbin));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("Os backups mudaram depois da conferência", vm.ErrorMessage);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Recarregar_com_o_build_alterado_derruba_a_confirmacao_e_a_nova_injecao_usa_o_plano_novo()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        Assert.True(vm.IsConfirming);
        PutBuild(content: "traduzido-v2");

        await vm.ReloadAsync();

        Assert.False(vm.IsConfirming);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Contains(vm.Log, l => l.Message.Contains("confirmação foi cancelada", StringComparison.Ordinal));
        Assert.Equal(Original, File.ReadAllText(GameFile));

        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("traduzido-v2", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Recarregar_com_o_mesmo_plano_mantem_a_confirmacao_e_injeta_o_que_foi_mostrado()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);

        await vm.ReloadAsync();

        Assert.True(vm.IsConfirming);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Recarregar_com_backup_novo_derruba_a_confirmacao_da_restauracao()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        vm.RestoreCommand.Execute(null);
        Assert.True(vm.IsConfirming);
        var older = Directory.GetDirectories(folder.BackupDirectory).Single();
        var newer = Path.Combine(folder.BackupDirectory, "9999-12-31_2359");
        Directory.CreateDirectory(newer);
        File.Copy(Path.Combine(older, "vnforge-backup.json"), Path.Combine(newer, "vnforge-backup.json"));
        File.Copy(Path.Combine(older, Ysbin), Path.Combine(newer, Ysbin));

        await vm.ReloadAsync();

        Assert.False(vm.IsConfirming);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
        Assert.Contains(vm.Log, l => l.Message.Contains("confirmação foi cancelada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Projeto_ilegivel_na_recarga_vira_mensagem_e_nao_lanca()
    {
        var vm = await OpenAsync();
        Directory.Delete(folder.Root, recursive: true);

        await vm.ReloadAsync();

        Assert.False(vm.InjectCommand.CanExecute(null));
        Assert.NotEmpty(vm.PlanMessage);
    }

    [Fact]
    public async Task Trocar_de_projeto_durante_uma_operacao_descarta_a_saida_dela()
    {
        var blocking = new BlockingStarter();
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], blocking);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        var running = vm.LaunchCommand.ExecuteAsync(null);
        Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(vm.IsBusy);

        var other = SampleProjects.Create(temp.SubFolder("outro"), "Outro", TranslationStatus.Approved);
        vm.Project = ProjectSummaryReader.Read(other);
        blocking.Release.Set();
        await running;

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Log);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Fechar_a_janela_no_meio_de_uma_operacao_e_recusado_com_aviso()
    {
        var blocking = new BlockingStarter();
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], blocking);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        Assert.True(vm.TryCloseSafely());

        var running = vm.LaunchCommand.ExecuteAsync(null);
        Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.False(vm.TryCloseSafely());
        Assert.Contains("Aguarde", vm.Log[^1].Message);

        blocking.Release.Set();
        await running;
        Assert.True(vm.TryCloseSafely());
    }

    [Fact]
    public async Task Abrir_jogo_dispara_o_executavel_do_projeto()
    {
        var vm = await OpenAsync();

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(game, "jogo.exe"), starter.Executable);
        Assert.Equal(game, starter.WorkingDirectory);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Jogo aberto: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Abrir_jogo_sem_o_executavel_mostra_o_caminho_procurado()
    {
        File.Delete(Path.Combine(game, "jogo.exe"));
        var vm = await OpenAsync();

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Null(starter.Executable);
        Assert.Contains("jogo.exe", vm.ErrorMessage);
    }

    [Fact]
    public async Task Build_com_engine_sem_suporte_mostra_o_erro_sem_derrubar_o_app()
    {
        var vm = await OpenAsync();

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.Contains(vm.Log, l => l.Message.Contains("Build falhou", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_o_log_e_o_estado()
    {
        PutBuild();
        var vm = await OpenAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Log);

        var other = SampleProjects.Create(temp.SubFolder("outro"), "Outro", TranslationStatus.Approved);
        vm.Project = ProjectSummaryReader.Read(other);
        await vm.ReloadAsync();

        Assert.Empty(vm.Log);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsConfirming);
    }

    private BuildViewModel BuildableVm(QaEngine qa, TimeProvider? clock = null, BackgroundTaskService? tasks = null)
    {
        var scripts = Path.Combine(folder.GameCacheDirectory, "pkg");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "a001.fake"), "x");
        folder.SaveText("a001",
        [
            new TextUnit("a001:000000", TextUnitKind.Dialogue, null, "こんにちは", "Olá", TranslationStatus.Translated),
            new TextUnit("a001:000001", TextUnitKind.Dialogue, null, "さようなら", null, TranslationStatus.Untranslated),
            new TextUnit("a001:000002", TextUnitKind.Dialogue, null, "また", null, TranslationStatus.Untranslated),
        ]);
        return new BuildViewModel(tasks ?? new BackgroundTaskService(), [new BuildableEngine()], starter, qa, clock)
        {
            Project = ProjectSummaryReader.Read(folder.Root),
        };
    }

    [Fact]
    public async Task Build_de_projeto_parcial_grava_sem_permitir_avisos_e_mostra_a_contagem_no_resumo()
    {
        var vm = BuildableVm(new QaEngine([new UntranslatedQaRule()]));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.False(vm.AllowWarnings);
        Assert.Null(vm.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(folder.BuildDirectory, Ysbin)));
        Assert.Contains(vm.Facts, f => f.Label == "QA" && f.Value == "2 linhas sem tradução; o build copiou o original nelas.");
        Assert.DoesNotContain(vm.Log, l => l.Message.Contains("[nao-traduzida]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outro_aviso_do_qa_continua_bloqueando_e_a_mensagem_nao_conta_as_linhas_sem_traducao()
    {
        var vm = BuildableVm(new QaEngine([new UntranslatedQaRule(), new BadTranslationRule()]));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(folder.BuildDirectory, Ysbin)));
        Assert.Contains("1 aviso", vm.ErrorMessage);
        Assert.Contains(vm.Log, l => l.Message.Contains("[traducao-ruim]", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Message.Contains("2 linhas sem tradução", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outro_aviso_do_qa_liberado_aparece_no_resumo_junto_da_contagem()
    {
        var vm = BuildableVm(new QaEngine([new UntranslatedQaRule(), new BadTranslationRule()]));
        vm.AllowWarnings = true;

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Contains(vm.Facts, f => f.Value.Contains("liberados por 'Permitir avisos do QA'", StringComparison.Ordinal));
        Assert.Contains(vm.Facts, f => f.Value.Contains("2 linhas sem tradução", StringComparison.Ordinal));
    }

    // ---- Tela no nível do design v2 (#246) ----

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 7, TimeSpan.Zero);

    private BuildViewModel Open(TimeProvider? clock = null, QaEngine? qa = null) =>
        new(new BackgroundTaskService(), [new FakeEngine()], starter, qa, clock) { Project = ProjectSummaryReader.Read(folder.Root) };

    [Fact]
    public async Task O_cabecalho_mostra_o_executavel_do_jogo()
    {
        var vm = Open();
        await vm.ReloadAsync();

        Assert.Equal(Path.Combine(game, "jogo.exe"), vm.GameExecutable);
    }

    [Fact]
    public async Task Sem_erros_de_qa_nao_ha_faixa_de_aviso_e_o_build_esta_liberado()
    {
        var vm = Open();
        await vm.ReloadAsync();

        Assert.False(vm.HasQaErrors);
        Assert.Equal(0, vm.QaErrorCount);
        Assert.True(vm.BuildCommand.CanExecute(null));
    }

    private BuildViewModel OpenWithQaRules(params IQaRule[] rules)
    {
        folder.SaveText("a001",
        [
            new TextUnit("a001:000000", TextUnitKind.Dialogue, null, "こんにちは", "Olá", TranslationStatus.Translated),
            new TextUnit("a001:000001", TextUnitKind.Dialogue, null, "さようなら", "Tchau", TranslationStatus.Translated),
        ]);
        return Open(qa: new QaEngine(rules));
    }

    [Fact]
    public async Task Erros_de_qa_acendem_a_faixa_com_a_contagem_e_travam_o_novo_build()
    {
        PutBuild();
        var vm = OpenWithQaRules(new ErrorOnOlaRule());

        await vm.ReloadAsync();

        Assert.True(vm.HasQaErrors);
        Assert.Equal(1, vm.QaErrorCount);
        Assert.Equal("1 erro novo impede um build novo.", vm.QaBannerTitle);
        Assert.Contains("O build abaixo é de ", vm.QaBannerDetail);
        Assert.Contains("continua válido para injetar", vm.QaBannerDetail);
        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Varios_erros_vao_para_o_plural_e_sem_build_a_faixa_manda_corrigir()
    {
        var vm = OpenWithQaRules(new ErrorOnEveryLineRule());

        await vm.ReloadAsync();

        Assert.Equal("2 erros novos impedem um build novo.", vm.QaBannerTitle);
        Assert.DoesNotContain("O build abaixo", vm.QaBannerDetail);
    }

    [Fact]
    public async Task Ver_erros_pede_a_conferencia()
    {
        var vm = OpenWithQaRules(new ErrorOnOlaRule());
        await vm.ReloadAsync();
        var requests = 0;
        vm.ErrorsRequested += (_, _) => requests++;

        vm.ShowErrorsCommand.Execute(null);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task A_descricao_de_permitir_avisos_diz_quantos_avisos_ha_abertos()
    {
        var vm = OpenWithQaRules(new BadTranslationRule());

        await vm.ReloadAsync();

        Assert.Equal(1, vm.QaWarningCount);
        Assert.Equal("Erros sempre bloqueiam. Hoje há 1 aviso aberto.", vm.AllowWarningsDescription);
    }

    [Fact]
    public async Task Sem_avisos_a_descricao_nao_inventa_numero()
    {
        var vm = Open();
        await vm.ReloadAsync();

        Assert.Equal("Erros sempre bloqueiam. Não há avisos abertos.", vm.AllowWarningsDescription);
    }

    [Fact]
    public async Task O_resultado_acompanha_o_estado_do_build_da_injecao_e_da_restauracao()
    {
        var vm = Open();
        await vm.ReloadAsync();
        Assert.Equal(BuildResultTone.Info, vm.ResultTone);
        Assert.Equal("Ainda não há build.", vm.ResultTitle);

        PutBuild();
        await vm.ReloadAsync();
        Assert.Equal(BuildResultTone.Info, vm.ResultTone);
        Assert.Equal("Build pronto, ainda não injetado.", vm.ResultTitle);
        Assert.Equal("O jogo continua com os arquivos originais.", vm.ResultText);
        Assert.Equal("Icon.Info", vm.ResultIconKey);

        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var name = Path.GetFileName(Directory.GetDirectories(folder.BackupDirectory).Single());
        Assert.Equal(BuildResultTone.Success, vm.ResultTone);
        Assert.Equal("Injetado no jogo.", vm.ResultTitle);
        Assert.Equal($"Originais copiados para backup{Path.DirectorySeparatorChar}{name} antes da cópia.", vm.ResultText);
        Assert.Equal("Icon.Check", vm.ResultIconKey);

        vm.RestoreCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(BuildResultTone.Warning, vm.ResultTone);
        Assert.Equal("Originais restaurados.", vm.ResultTitle);
        Assert.Equal(
            $"O jogo voltou aos arquivos de backup{Path.DirectorySeparatorChar}{name}. Para testar de novo, use Testar no jogo (Ctrl+B): ele refaz o build e copia por cima, sem restaurar antes.",
            vm.ResultText);
        Assert.Contains("Testar no jogo", vm.ResultText, StringComparison.Ordinal);
        Assert.Equal("Icon.Warning", vm.ResultIconKey);
    }

    [Fact]
    public async Task Depois_do_build_o_resultado_traz_scripts_pacote_e_acentuacao()
    {
        var vm = BuildableVm(new QaEngine([]));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal("Scripts", vm.Facts[0].Label);
        Assert.Equal("1 reconstruído de 1", vm.Facts[0].Value);
        Assert.Equal("12 B (original 8 B)", vm.Facts.Single(f => f.Label == Ysbin).Value);
        Assert.Equal("FOLD · 1 caractere substituído", vm.Facts.Single(f => f.Label == "Acentuação").Value);
        Assert.True(vm.HasFacts);
    }

    [Fact]
    public async Task Sem_caractere_trocado_a_acentuacao_diz_que_nenhum_foi_substituido()
    {
        var vm = BuildableVm(new QaEngine([]));
        folder.SaveText("a001", [new TextUnit("a001:000000", TextUnitKind.Dialogue, null, "こんにちは", "Ola", TranslationStatus.Translated)]);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal("FOLD · nenhum caractere substituído", vm.Facts.Single(f => f.Label == "Acentuação").Value);
    }

    [Fact]
    public async Task O_tamanho_do_pacote_mostra_o_original_quando_o_jogo_o_tem()
    {
        File.WriteAllText(GameFile, new string('o', 2048));
        var vm = BuildableVm(new QaEngine([]));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal("12 B (original 2,0 KB)", vm.Facts.Single(f => f.Label == Ysbin).Value);
    }

    [Fact]
    public async Task O_log_leva_o_horario_e_o_cabecalho_a_duracao_total()
    {
        var clock = new SteppingClock(Now, TimeSpan.FromMilliseconds(6800));
        var vm = BuildableVm(new QaEngine([]), clock);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.All(vm.Log, l => Assert.Equal("00:41:07", l.Time));
        Assert.Equal("24/10/2026 00:41 · 6,8 s", vm.LogSummary);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Build concluído em 6,8 s", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "0,0 s")]
    [InlineData(6800, "6,8 s")]
    [InlineData(59_940, "59,9 s")]
    [InlineData(72_000, "1 min 12 s")]
    [InlineData(3_725_000, "62 min 5 s")]
    public void A_duracao_vira_texto_em_portugues(int milliseconds, string expected)
    {
        Assert.Equal(expected, BuildViewModel.FormatDuration(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task O_log_lista_os_scripts_reconstruidos_com_a_contagem_de_textos()
    {
        var vm = BuildableVm(new QaEngine([]));

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Contains(vm.Log, l => l.Message == "a001.fake reconstruído · 1 texto");
    }

    [Fact]
    public async Task O_build_entra_no_grupo_Teste_no_jogo_e_nao_usa_a_IA()
    {
        var tasks = new BackgroundTaskService();
        var vm = BuildableVm(new QaEngine([]), tasks: tasks);

        await vm.BuildCommand.ExecuteAsync(null);

        var build = Assert.Single(tasks.Tasks);
        Assert.Equal("Teste no jogo", build.Group?.Title);
        Assert.False(build.UsesAi);
    }

    [Fact]
    public async Task O_progresso_do_core_alimenta_a_tarefa_e_nao_enche_o_log()
    {
        var tasks = new BackgroundTaskService();
        var seen = new System.Collections.Concurrent.ConcurrentQueue<(double? Progress, string Message)>();
        tasks.Tasks.CollectionChanged += (_, e) =>
        {
            var item = (BackgroundTaskItem)e.NewItems![0]!;
            item.PropertyChanged += (_, _) => seen.Enqueue((item.Progress, item.Message));
        };
        var vm = BuildableVm(new QaEngine([]), tasks: tasks);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Contains(seen, s => s.Progress == 0 && s.Message == "Reconstruindo a001.fake (1 de 1)");
        Assert.Contains(seen, s => s.Progress == 1 && s.Message == "Reconstruindo os scripts (1 de 1)");
        Assert.DoesNotContain(vm.Log, l => l.Message.Contains("(1 de 1)", StringComparison.Ordinal));
        Assert.Single(vm.Log, l => l.Message == "Reconstruindo os scripts com as traduções");
    }

    [Fact]
    public async Task Sem_backup_a_lista_fica_vazia()
    {
        var vm = Open();
        await vm.ReloadAsync();

        Assert.Empty(vm.Backups);
        Assert.False(vm.HasBackups);
        Assert.Contains("Nenhum backup", vm.BackupMessage);
    }

    [Fact]
    public async Task A_lista_de_backups_mostra_pasta_tamanho_e_descricao_do_mais_novo_ao_mais_antigo()
    {
        PutBuild();
        PutBuild("pac/extra.ypf", "novo");
        var vm = Open(new FixedClock(Now));
        await vm.ReloadAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var older = Path.Combine(folder.BackupDirectory, "2020-01-02_0304");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, "vnforge-backup.json"), "{ ilegível");
        await vm.ReloadAsync();

        Assert.True(vm.HasBackups);
        Assert.Equal(2, vm.Backups.Count);
        var separator = Path.DirectorySeparatorChar;
        var latest = vm.Backups[0];
        Assert.StartsWith($"backup{separator}", latest.Name, StringComparison.Ordinal);
        Assert.Equal("8 B", latest.SizeText);
        Assert.Equal($"{Ysbin} original · 1 arquivo criado · injeção de {Now.ToLocalTime():dd/MM}", latest.Note);
        Assert.Equal($"backup{separator}2020-01-02_0304", vm.Backups[1].Name);
        Assert.Contains("manifesto", vm.Backups[1].Note);
        Assert.Equal($"Pasta backup{separator} do projeto · fora do git · 2 backups", vm.BackupFooter);
    }

    [Fact]
    public async Task Backup_ilegivel_sem_outra_fonte_explica_por_que_nao_da_para_restaurar()
    {
        var broken = Path.Combine(folder.BackupDirectory, "2020-01-02_0304");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "vnforge-backup.json"), "{ ilegível");
        var vm = Open();
        await vm.ReloadAsync();

        Assert.True(vm.HasRestoreProblem);
        Assert.NotEmpty(vm.BackupMessage);
        Assert.False(vm.RestoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task O_rodape_so_diz_fora_do_git_quando_o_gitignore_lista_o_backup()
    {
        var vm = Open();
        await vm.ReloadAsync();
        Assert.Contains("fora do git", vm.BackupFooter);

        File.WriteAllText(Path.Combine(folder.Root, ".gitignore"), "build/\n");
        await vm.ReloadAsync();

        Assert.DoesNotContain("fora do git", vm.BackupFooter);
    }

    [Fact]
    public async Task Backup_restaurado_diz_que_foi_restaurado()
    {
        PutBuild();
        var vm = Open();
        await vm.ReloadAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        vm.RestoreCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Backups);
        Assert.Contains("restaurado", row.Note);
        Assert.Equal($"Pasta backup{Path.DirectorySeparatorChar} do projeto · fora do git · 1 backup", vm.BackupFooter);
    }

    private sealed class ErrorOnOlaRule : UnitQaRule
    {
        public override string Id => "erro-ola";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Error;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == "Olá")
            {
                yield return "recusada";
            }
        }
    }

    private sealed class ErrorOnEveryLineRule : UnitQaRule
    {
        public override string Id => "erro-todas";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Error;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            yield return "recusada";
        }
    }

    /// <summary>Hora fixa; cada leitura do cronômetro avança o passo, então a duração de uma operação é conhecida.</summary>
    private sealed class SteppingClock(DateTimeOffset now, TimeSpan step) : TimeProvider
    {
        private long ticks;

        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp()
        {
            var current = ticks;
            ticks += (long)step.TotalMilliseconds;
            return current;
        }
    }

    private sealed class BadTranslationRule : UnitQaRule
    {
        public override string Id => "traducao-ruim";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Warning;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == "Olá")
            {
                yield return "recusada";
            }
        }
    }

    private sealed class BuildableEngine : IEngine, IGameUnpacker, IGameRepacker
    {
        private readonly BuildableFormat format = new();

        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [format];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default) => new([]);

        public UnpackedGame Open(string unpackedDirectory) =>
            new(new BuildableContext(), [.. Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories).Select(f => new UnpackedScript(Path.GetFileName(f), f, format, null))]);

        public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, Func<string, string>? originalOf = null, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(Path.Combine(outputDirectory, Ysbin), "reconstruido");
            return new GameRepackResult([new RepackedArchive(Ysbin, rebuilt.Count)]);
        }
    }

    private sealed class BuildableContext() : EngineContext("yuris");

    private sealed class BuildableFormat : IScriptFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".fake"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx) => new([]);

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx) => Encoding.UTF8.GetBytes("y");
    }

    private sealed class BlockingStarter : IGameProcessStarter
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public void Start(string executablePath, string workingDirectory)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class RecordingStarter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public void Start(string executablePath, string workingDirectory)
        {
            Executable = executablePath;
            WorkingDirectory = workingDirectory;
        }
    }
}
