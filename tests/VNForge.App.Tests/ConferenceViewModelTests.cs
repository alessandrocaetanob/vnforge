using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
[Trait("Risco", "R4")]
public class ConferenceViewModelTests
{
    private static TextUnit Unit(string key, int n, string? translation, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", translation, status);

    /// <summary>Dois scripts: espaço duplo (aviso, corrigível) em duas linhas, uma linha aprovada sem texto (erro) e uma pendente (aviso).</summary>
    private static (ConferenceViewModel Vm, ProjectFolder Folder) Open(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Unit("yst00001", 0, "olá  mundo", TranslationStatus.Translated),
            Unit("yst00001", 1, "tudo certo", TranslationStatus.Translated),
            Unit("yst00001", 2, null, TranslationStatus.Approved),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
        ]);
        folder.SaveText("yst00002", [Unit("yst00002", 5, "a  b  c", TranslationStatus.Reviewed)]);
        return (new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) }, folder);
    }

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> Loaded(TempFolder temp)
    {
        var opened = Open(temp);
        await opened.Vm.RefreshAsync();
        return opened;
    }

    private static IEnumerable<ConferenceIssueRowViewModel> IssueRows(ConferenceViewModel vm) => vm.Rows.OfType<ConferenceIssueRowViewModel>();

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> LoadedCp1252(TempFolder temp)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("real"), new ProjectConfig(
            "Real",
            new GameConfig("D:/Jogos/Real", "Real.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveText("story04",
        [
            new TextUnit("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine),
            new TextUnit("story04:000065", TextUnitKind.Dialogue, null, "原文", "Sem † solução", TranslationStatus.Translated),
        ]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();
        return (vm, folder);
    }

    [Fact]
    public async Task Corrigir_tudo_troca_a_pontuacao_japonesa_e_deixa_o_que_nao_tem_substituto()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await LoadedCp1252(temp);

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal(["~Manhã~", "Sem † solução"], folder.LoadText("story04").Select(u => u.Translation));
        Assert.Equal(["story04:000065"], IssueRows(vm).Select(r => r.UnitId));
    }

    [Fact]
    public async Task Falha_no_segundo_script_mantem_o_primeiro_corrigido_avisa_e_reconfere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O bloqueio de arquivo por FileShare só vale no Windows.");
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;

        // Leitura compartilhada deixa o Core ler, mas impede o File.Move que regrava o segundo script.
        using (new FileStream(folder.TextPath("yst00002"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await vm.FixAllCommand.ExecuteAsync(null);
        }

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a  b  c", folder.LoadText("yst00002")[0].Translation);
        Assert.StartsWith("Não foi possível corrigir com um clique:", vm.ErrorMessage);
        Assert.EndsWith("continuam gravados.", vm.ErrorMessage);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.False(vm.IsBusy);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.Contains(IssueRows(vm), r => r.UnitId == "yst00002:000005" && r.RuleId == "espaco-duplo");
    }

    private sealed class ThrowingRule : IQaRule
    {
        public string Id => "regra-quebrada";

        public string Description => "Sempre lança.";

        public IEnumerable<QaIssue> Check(QaContext context) => throw new InvalidOperationException("regra quebrada.");
    }

}
