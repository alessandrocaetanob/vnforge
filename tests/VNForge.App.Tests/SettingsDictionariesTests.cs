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
        Assert.Contains("1 linha", editor.ImpactDetail, StringComparison.Ordinal);

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
    public void Sem_projeto_a_aba_fica_vazia()
    {
        var vm = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, _ => new NoopClient());
        vm.Load(null);

        Assert.Empty(vm.PreRules);
        Assert.Empty(vm.PostRules);
        Assert.Null(vm.SelectedRule);
    }
}
