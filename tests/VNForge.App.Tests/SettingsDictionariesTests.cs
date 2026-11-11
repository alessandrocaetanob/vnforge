using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba Dicionários dos Ajustes: regras com condição, alcance de cada uma, prévia e o diálogo da regra (#539).</summary>
public sealed class SettingsDictionariesTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private sealed class NoopClient : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => Task.FromResult(new ClaudeReply("end_turn", string.Empty));
    }

    private static TextUnit Line(string id, string? speaker, string source, string? translation) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private async Task<(SettingsViewModel Vm, ProjectFolder Folder)> Opened(DictionaryRules rules)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("蓮", "Ren", CharacterGender.Male)]);
        folder.SaveText("dic001",
        [
            Line("dic001:000301", "蓮", "ぼ、ぼく、先生に会う", "você acha que ele vai dar conta?"),
            Line("dic001:000302", "蓮", "ぼ、ぼく", "você sabe."),
            Line("dic001:000214", "葵", "君は早いね", "você chegou cedo hoje."),
        ]);
        folder.SaveFormatting(FormattingProfile.Default with { Dictionary = rules });
        var vm = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, _ => new NoopClient());
        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        await vm.DictionaryCounting;
        return (vm, folder);
    }

    private static DictionaryRules Sample() => new(
        [new DictionaryRule("ぼ、ぼく", "ぼく"), new DictionaryRule("ありがとー", "ありがとう") { Enabled = false }],
        [new DictionaryRule("você", "tu") { Conditions = [new(DictionaryConditionKind.Speaker, "Ren")] }]);

    [Fact]
    public async Task Lista_as_regras_dos_dois_lados_com_os_selos_e_o_alcance()
    {
        var (vm, _) = await Opened(Sample());

        Assert.Equal(["ぼ、ぼく", "ありがとー"], vm.PreRules.Select(r => r.Pattern));
        Assert.Equal(["2 linhas", "0 linhas"], vm.PreRules.Select(r => r.LinesText));
        var post = Assert.Single(vm.PostRules);
        Assert.Equal(["Falante: Ren"], post.Chips);
        Assert.Equal("2 linhas", post.LinesText);
        Assert.Equal(["Sempre"], vm.PreRules[0].Chips);
        Assert.Equal(vm.PreRules[0].Rule.With, vm.PreRules[0].With);
        Assert.False(vm.PreRules[1].Enabled);
    }

    [Fact]
    public async Task A_previa_mostra_uma_linha_em_que_troca_e_uma_em_que_a_condicao_impede()
    {
        var (vm, _) = await Opened(Sample());

        Assert.Same(vm.PostRules[0], vm.SelectedRule);
        Assert.Equal("Linha 0301 · Ren", vm.AppliedLine);
        Assert.Equal("você acha que ele vai dar conta?", vm.AppliedBefore);
        Assert.Equal("tu acha que ele vai dar conta?", vm.AppliedAfter);
        Assert.StartsWith("Linha 0214", vm.BlockedLine, StringComparison.Ordinal);
        Assert.Equal("você chegou cedo hoje.", vm.BlockedText);
        Assert.Equal("Linha 0214 · 葵 (falante diferente: não troca)", vm.BlockedLine);
        Assert.Equal("você, Ren", vm.PreviewSubtitle);
    }

    [Fact]
    public async Task O_aviso_do_cache_conta_as_linhas_das_regras_ligadas_do_pre()
    {
        var (vm, _) = await Opened(Sample());

        Assert.Contains("2 linhas", vm.CacheNotice, StringComparison.Ordinal);
        Assert.Contains("chave do cache", vm.CacheNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ligar_e_desligar_grava_no_formatting_json_sem_apagar_a_regra()
    {
        var (vm, folder) = await Opened(Sample());

        vm.PreRules[0].Enabled = false;
        vm.PreRules[1].Enabled = true;

        var saved = folder.LoadFormatting().Dictionary;
        Assert.Equal([false, true], saved.Pre.Select(r => r.Enabled));
        Assert.Single(saved.Post);
    }

    [Fact]
    public async Task Nova_regra_do_pos_com_condicoes_grava_e_conta_o_alcance_no_dialogo()
    {
        var (vm, folder) = await Opened(new DictionaryRules([], []));

        vm.AddPostRuleCommand.Execute(null);
        var editor = vm.Editor!;
        Assert.True(vm.IsEditorOpen);
        Assert.Equal("Nova regra do pós-dicionário", editor.Title);
        Assert.NotNull(editor.Problem);

        editor.Pattern = "você";
        editor.With = "tu";
        editor.AddConditionCommand.Execute(null);
        editor.Conditions[0].Value = "Ren";
        await vm.EditorImpactLoading;

        Assert.Null(editor.Problem);
        Assert.Equal("Aplica em 2 linhas", editor.ImpactTitle);
        Assert.Equal("1 linha de outro falante não muda", editor.ImpactDetail);
        Assert.True(editor.HasImpactDetail);

        editor.SaveCommand.Execute(null);
        await vm.DictionaryCounting;

        Assert.False(vm.IsEditorOpen);
        var rule = Assert.Single(folder.LoadFormatting().Dictionary.Post);
        Assert.Equal(new DictionaryCondition(DictionaryConditionKind.Speaker, "Ren"), Assert.Single(rule.Conditions));
        Assert.Equal("2 linhas", Assert.Single(vm.PostRules).LinesText);
    }

    [Fact]
    public async Task Condicao_sem_valor_ou_regex_invalida_impede_salvar()
    {
        var (vm, folder) = await Opened(new DictionaryRules([], []));
        vm.AddPreRuleCommand.Execute(null);
        var editor = vm.Editor!;

        editor.Pattern = "(";
        editor.IsRegex = true;
        Assert.NotNull(editor.Problem);
        editor.SaveCommand.Execute(null);

        editor.IsRegex = false;
        editor.AddConditionCommand.Execute(null);
        Assert.NotNull(editor.Problem);
        editor.SaveCommand.Execute(null);

        Assert.True(vm.IsEditorOpen);
        Assert.True(folder.LoadFormatting().Dictionary.IsEmpty);
    }

    [Fact]
    public async Task Cancelar_fecha_sem_gravar_e_apagar_tira_a_regra_escolhida()
    {
        var (vm, folder) = await Opened(Sample());

        vm.AddPreRuleCommand.Execute(null);
        vm.Editor!.Pattern = "x";
        vm.CancelEditorCommand.Execute(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(2, folder.LoadFormatting().Dictionary.Pre.Count);

        vm.DeleteSelectedRuleCommand.Execute(null);

        Assert.Empty(folder.LoadFormatting().Dictionary.Post);
        Assert.Empty(vm.PostRules);
    }

    [Fact]
    public async Task Editar_troca_a_regra_no_mesmo_lugar()
    {
        var (vm, folder) = await Opened(Sample());

        vm.EditSelectedRuleCommand.Execute(null);
        vm.Editor!.With = "você mesmo";
        vm.Editor.SaveCommand.Execute(null);

        var rule = Assert.Single(folder.LoadFormatting().Dictionary.Post);
        Assert.Equal("você mesmo", rule.With);
        Assert.Single(rule.Conditions);
    }

    [Fact]
    public async Task Cada_selo_leva_o_icone_do_tipo_da_condicao_e_Sempre_leva_o_visto()
    {
        var rules = new DictionaryRules(
            [new DictionaryRule("a", "b")],
            [new DictionaryRule("você", "tu") { Conditions = [new(DictionaryConditionKind.Speaker, "Ren"), new(DictionaryConditionKind.Scene, "Capítulo 1"), new(DictionaryConditionKind.SourceContains, "先生")] }]);
        var (vm, _) = await Opened(rules);

        Assert.Equal([new RuleChip("Icon.Check", "Sempre")], vm.PreRules[0].ChipItems);
        Assert.Equal(
            [new RuleChip("Icon.Person", "Falante: Ren"), new RuleChip("Icon.Board", "Cena: Capítulo 1"), new RuleChip("Icon.Search", "Original contém 先生")],
            vm.PostRules[0].ChipItems);
    }

    [Fact]
    public async Task O_menu_da_linha_edita_e_apaga_a_regra_da_propria_linha_sem_botoes_na_previa()
    {
        var (vm, folder) = await Opened(Sample());

        vm.PreRules[1].EditCommand.Execute(null);
        Assert.Same(vm.PreRules[1], vm.SelectedRule);
        Assert.Equal("Editar regra do pré-dicionário", vm.Editor!.Title);
        vm.CancelEditorCommand.Execute(null);

        vm.PreRules[0].DeleteCommand.Execute(null);

        Assert.Equal(["ありがとー"], folder.LoadFormatting().Dictionary.Pre.Select(r => r.Pattern));
    }

    [Fact]
    public async Task O_interruptor_de_regex_so_aparece_ao_editar_uma_regra_que_ja_e_regex()
    {
        var rules = new DictionaryRules([new DictionaryRule("a+", "b", Regex: true), new DictionaryRule("c", "d")], []);
        var (vm, _) = await Opened(rules);

        vm.AddPreRuleCommand.Execute(null);
        Assert.False(vm.Editor!.ShowsRegexSwitch);
        vm.CancelEditorCommand.Execute(null);

        vm.PreRules[1].EditCommand.Execute(null);
        Assert.False(vm.Editor!.ShowsRegexSwitch);
        vm.CancelEditorCommand.Execute(null);

        vm.PreRules[0].EditCommand.Execute(null);
        Assert.True(vm.Editor!.ShowsRegexSwitch);
        Assert.True(vm.Editor.IsRegex);
    }

    [Fact]
    public void O_resumo_do_que_fica_de_fora_nomeia_a_condicao_que_barrou_e_o_selo_muda_com_o_lado()
    {
        var conditions = new List<DictionaryCondition>
        {
            new(DictionaryConditionKind.Speaker, "Ren"),
            new(DictionaryConditionKind.Scene, "Capítulo 1"),
            new(DictionaryConditionKind.SourceContains, "先生"),
        };
        var unit = Line("a:000001", "蓮", "先生", "você");
        var impact = new DictionaryImpact(1, 4, null, new DictionarySample(unit, "você", "tu", [1]), new Dictionary<int, int> { [1] = 2, [2] = 1 });

        Assert.Equal("2 linhas do Ren fora do Capítulo 1 não mudam", DictionaryReasons.LeftOut(conditions, impact));
        Assert.Equal("4 linhas com este texto não mudam por causa das condições", DictionaryReasons.LeftOut(conditions, impact with { BlockedByOne = null }));
        Assert.Equal("falante diferente, sem o trecho", DictionaryReasons.Why(conditions, [0, 2]));
        Assert.Equal("Trecho contém ー", DictionaryRuleRow.ChipText(new(DictionaryConditionKind.SourceContains, "ー"), DictionarySide.Pre));
        Assert.Equal("Original contém 先生", DictionaryRuleRow.ChipText(new(DictionaryConditionKind.SourceContains, "先生"), DictionarySide.Post));
    }

    [Fact]
    public void O_verbo_da_condicao_acompanha_o_tipo()
    {
        var condition = new DictionaryConditionEditor(new DictionaryCondition(DictionaryConditionKind.Speaker, "Ren"), _ => { }, () => { });

        Assert.Equal(["é"], condition.Operators);
        condition.KindIndex = 1;
        Assert.Equal(["está em"], condition.Operators);
        condition.KindIndex = 2;
        Assert.Equal(["contém"], condition.Operators);
    }

    [Fact]
    public void Sem_projeto_a_aba_fica_vazia()
    {
        var vm = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, _ => new NoopClient());
        vm.Load(null);

        Assert.Empty(vm.PreRules);
        Assert.Empty(vm.PostRules);
        Assert.Null(vm.SelectedRule);
    }

    [Fact]
    public void Sem_projeto_os_comandos_das_regras_nao_abrem_dialogo_nem_falham()
    {
        var vm = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, _ => new NoopClient());
        vm.Load(null);

        vm.AddPreRuleCommand.Execute(null);
        vm.AddPostRuleCommand.Execute(null);
        vm.EditSelectedRuleCommand.Execute(null);
        vm.DeleteSelectedRuleCommand.Execute(null);
        vm.IsEditorOpen = false;

        Assert.Null(vm.Editor);
        Assert.False(vm.IsEditorOpen);
        Assert.False(vm.HasSelectedRule);
        Assert.Equal("Escolha uma regra para ver onde ela troca.", vm.PreviewEmpty);
        vm.Dispose();
    }

    [Fact]
    public async Task Regra_cujo_texto_nao_aparece_em_linha_nenhuma_diz_isso_na_previa()
    {
        var (vm, _) = await Opened(new DictionaryRules([new DictionaryRule("ありがとー", "ありがとう")], []));

        Assert.True(vm.HasSelectedRule);
        Assert.False(vm.HasApplied);
        Assert.False(vm.HasBlocked);
        Assert.Equal("Nenhuma linha do projeto tem este texto.", vm.PreviewEmpty);
        Assert.True(vm.HasPreviewEmpty);
        Assert.Equal("0 linhas", Assert.Single(vm.PreRules).LinesText);
    }

    [Fact]
    public async Task Fechar_o_dialogo_pelo_interruptor_descarta_a_edicao_e_abrir_por_ele_nao_faz_nada()
    {
        var (vm, folder) = await Opened(Sample());
        vm.AddPreRuleCommand.Execute(null);
        vm.Editor!.Pattern = "x";

        vm.IsEditorOpen = true;
        Assert.True(vm.IsEditorOpen);
        vm.IsEditorOpen = false;

        Assert.Null(vm.Editor);
        Assert.Equal(2, folder.LoadFormatting().Dictionary.Pre.Count);
        vm.Dispose();
    }
}
