using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Renomear um capítulo na linha do Painel: o nome vira o <c>group</c> dos scripts dele em <c>scripts.json</c> e o sumário se refaz.</summary>
[Trait("Risco", "R2")]
public class DashboardChapterRenameTests
{
    private const string Root = @"D:\Traducoes\minha-vn-ptbr";

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static readonly ProjectSummary Summary = new("Minha VN PT-BR", Root, "yuris", 500, "cp1252", "claude-opus-5-5", 3, new StatusCounts(0, 0, 0, 0, 0));

    private static readonly string[] ScriptKeys = ["yst00001", "yst00002", "yst00003", "yst00004"];

    private static ProjectStatus Status() => ProjectStatusReporter.Create(ScriptKeys.ToDictionary(
        k => k,
        k => (IReadOnlyList<TextUnit>)[new TextUnit($"{k}:000001", TextUnitKind.Dialogue, null, "原文", "tradução", TranslationStatus.Machine)],
        StringComparer.Ordinal));

    private static ScriptNames InitialNames() => ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
    {
        ["yst00001"] = new("manha", "userscript/01_cap1"),
        ["yst00002"] = new("tarde", "userscript/01_cap1"),
        ["yst00003"] = new("noite", "userscript/02_cap2"),
        ["yst00004"] = new("menu", "userscript/system"),
    });

    private static IReadOnlyList<PipelineStage> Stages(StatusCounts total) =>
    [
        new(PipelineStageKey.Extract, true, 0),
        new(PipelineStageKey.Translate, true, 0),
        new(PipelineStageKey.Review, false, total.Total),
        new(PipelineStageKey.Build, false, 1),
        new(PipelineStageKey.Inject, false, 1),
    ];

    private sealed class Fixture
    {
        public ScriptNames Names { get; set; } = InitialNames();

        public List<(string Root, string Group, string NewName)> Renames { get; } = [];

        public int Loads { get; private set; }

        public Exception? Failure { get; set; }

        public DashboardViewModel Create()
        {
            var status = Status();
            return new DashboardViewModel(
                _ =>
                {
                    Loads++;
                    return new ProjectDashboard(status, Stages(status.Total), AiUsageTotals.Empty) { ScriptNames = Names, Qa = new QaTotals(0, 0) };
                },
                new FixedClock(Now),
                (root, group, newName) =>
                {
                    if (Failure is not null)
                    {
                        throw Failure;
                    }

                    Renames.Add((root, group, newName));
                    Names = ScriptGrouping.RenameGroup(Names, status.Files, group, newName);
                })
            { Project = Summary };
        }
    }

    [Fact]
    public async Task Renomear_de_verdade_grava_o_scripts_json_do_projeto()
    {
        using var temp = new TempFolder();
        var project = ProjectFolder.Create(temp.Path, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        foreach (var key in new[] { "yst00001", "yst00002", "yst00003" })
        {
            project.SaveText(key, [new TextUnit($"{key}:000001", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        project.SaveScriptNames(InitialNames());
        var vm = new DashboardViewModel(clock: new FixedClock(Now)) { Project = Summary with { Root = temp.Path } };
        await vm.ReloadAsync();
        var row = vm.Chapters[0];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = "Prólogo";

        await vm.CommitRenameChapterCommand.ExecuteAsync(row);

        Assert.Equal("Prólogo", ProjectFolder.Open(temp.Path).LoadScriptNames().Group("yst00001"));
        Assert.Equal("Prólogo", vm.Chapters[0].Name);
    }
}
