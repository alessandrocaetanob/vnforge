using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Renomear um capítulo na linha do Painel: o nome vira o <c>group</c> dos scripts dele em <c>scripts.json</c> e o sumário se refaz.</summary>
public class DashboardChapterRenameTests
{
    private const string Root = @"D:\Traducoes\minha-vn-ptbr";

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static readonly ProjectSummary Summary = new("Minha VN PT-BR", Root, "yuris", 500, "cp1252", "claude-opus-5-5", 3, new StatusCounts(0, 0, 0, 0, 0));

    private static ProjectStatus Status() => ProjectStatusReporter.Create(new[] { "yst00001", "yst00002", "yst00003", "yst00004" }.ToDictionary(
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

    private static async Task<(Fixture Fixture, DashboardViewModel Vm)> Open()
    {
        var fixture = new Fixture();
        var vm = fixture.Create();
        await vm.ReloadAsync();
        return (fixture, vm);
    }

    [Fact]
    public async Task Renomear_abre_a_edicao_na_linha_com_o_nome_atual()
    {
        var (_, vm) = await Open();
        var row = vm.Chapters[0];

        vm.BeginRenameChapterCommand.Execute(row);

        Assert.True(row.IsEditing);
        Assert.Equal("Capítulo 1", row.EditText);
        Assert.False(vm.Chapters[1].IsEditing);
    }

    [Fact]
    public async Task Abrir_a_edicao_de_outra_linha_fecha_a_primeira()
    {
        var (_, vm) = await Open();

        vm.BeginRenameChapterCommand.Execute(vm.Chapters[0]);
        vm.BeginRenameChapterCommand.Execute(vm.Chapters[1]);

        Assert.False(vm.Chapters[0].IsEditing);
        Assert.True(vm.Chapters[1].IsEditing);
    }

    [Fact]
    public async Task Menus_e_nomes_nao_se_renomeia()
    {
        var (_, vm) = await Open();
        var extra = vm.Chapters.Single(c => c.IsExtra);

        vm.BeginRenameChapterCommand.Execute(extra);

        Assert.False(extra.IsEditing);
    }

    [Fact]
    public async Task Confirmar_grava_pelo_nome_antigo_recarrega_e_mostra_o_nome_novo_na_mesma_posicao()
    {
        var (fixture, vm) = await Open();
        var row = vm.Chapters[0];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = "  Manhã de chuva ";

        await vm.CommitRenameChapterCommand.ExecuteAsync(row);

        Assert.Equal([(Root, "Capítulo 1", "Manhã de chuva")], fixture.Renames);
        Assert.Equal(2, fixture.Loads);
        Assert.Equal(["Manhã de chuva", "Capítulo 2", ScriptGrouping.ExtraName], vm.Chapters.Select(c => c.Name));
        Assert.All(vm.Chapters, c => Assert.False(c.IsEditing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nome_vazio_nao_grava_e_pede_um_nome_sem_fechar_a_edicao(string text)
    {
        var (fixture, vm) = await Open();
        var row = vm.Chapters[0];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = text;

        await vm.CommitRenameChapterCommand.ExecuteAsync(row);

        Assert.Empty(fixture.Renames);
        Assert.True(row.IsEditing);
        Assert.Equal("Escreva um nome para o capítulo.", row.EditError);
    }

    [Fact]
    public async Task Confirmar_o_mesmo_nome_so_fecha_a_edicao()
    {
        var (fixture, vm) = await Open();
        var row = vm.Chapters[1];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = " Capítulo 2 ";

        await vm.CommitRenameChapterCommand.ExecuteAsync(row);

        Assert.Empty(fixture.Renames);
        Assert.False(row.IsEditing);
        Assert.Equal(1, fixture.Loads);
    }

    [Fact]
    public async Task Cancelar_fecha_a_edicao_sem_gravar()
    {
        var (fixture, vm) = await Open();
        var row = vm.Chapters[0];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = "Outro";

        vm.CancelRenameChapterCommand.Execute(row);

        Assert.False(row.IsEditing);
        Assert.Empty(fixture.Renames);
        Assert.Equal("Capítulo 1", row.Name);
    }

    [Fact]
    public async Task Falha_ao_gravar_mostra_a_mensagem_na_linha_e_mantem_a_edicao()
    {
        var (fixture, vm) = await Open();
        fixture.Failure = new ScriptGroupingException("Não há capítulo chamado 'Capítulo 1'.");
        var row = vm.Chapters[0];
        vm.BeginRenameChapterCommand.Execute(row);
        row.EditText = "Novo";

        await vm.CommitRenameChapterCommand.ExecuteAsync(row);

        Assert.True(row.IsEditing);
        Assert.Equal("Não há capítulo chamado 'Capítulo 1'.", row.EditError);
        Assert.Equal(1, fixture.Loads);
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
