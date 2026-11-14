using VNForge.AI.Claude;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba Dicionários dos Ajustes: regras com condição, alcance de cada uma, prévia e o diálogo da regra (#539).</summary>
[Trait("Risco", "R2")]
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
    public async Task Ligar_e_desligar_grava_no_formatting_json_sem_apagar_a_regra()
    {
        var (vm, folder) = await Opened(Sample());

        vm.PreRules[0].Enabled = false;
        vm.PreRules[1].Enabled = true;

        var saved = folder.LoadFormatting().Dictionary;
        Assert.Equal([false, true], saved.Pre.Select(r => r.Enabled));
        Assert.Single(saved.Post);
    }
}
