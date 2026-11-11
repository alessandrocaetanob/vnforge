using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class StartDiaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    private static IEnumerable<ApprovalRecord> Day(DateTimeOffset at, int lines, int firstUnit = 0) =>
        Enumerable.Range(0, lines).Select(i => new ApprovalRecord("yst00001", $"yst00001:{firstUnit + i:D6}", at.AddSeconds(i * 30)));

    private static void Seed(string root, params ApprovalRecord[] approvals) =>
        SessionsFile.Save(ProjectFolder.Open(root), new SessionsFile(SessionsFile.CurrentVersion, approvals));

    private static StartViewModel Create(TempFolder temp, TimeProvider clock) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), clock);

    [Fact]
    public void Sem_aprovacao_na_sessao_nao_ha_resumo_ao_fechar()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var clock = new FakeTimeProvider(Now);
        var vm = Create(temp, clock);
        Seed(root, [.. Day(Now.AddDays(-1), 50)]);
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(vm.CloseSummary(root));
        Assert.Null(vm.CloseSummary(null));
        Assert.Null(vm.CloseSummary(Path.Combine(temp.SubFolder("config"), "nao-existe")));
    }

    [Fact]
    public async Task Trocar_de_projeto_zera_o_diario_na_hora_e_so_mostra_o_do_novo_depois_da_leitura()
    {
        using var temp = new TempFolder();
        var first = SampleProjects.Create(temp.SubFolder("a"), "Projeto A", TranslationStatus.Machine);
        var second = SampleProjects.Create(temp.SubFolder("b"), "Projeto B", TranslationStatus.Machine);
        Seed(first, [.. Day(Now.AddHours(-1), 7)]);
        Seed(second, [.. Day(Now.AddHours(-1), 3)]);
        var vm = Create(temp, new FixedClock(Now));
        await vm.OpenAsync(first);
        await vm.DetailLoading;
        await vm.OpenAsync(second);
        await vm.DetailLoading;
        Assert.Equal(3, vm.Diary.Source.Today);

        vm.Selected = vm.Recents.Single(r => r.Name == "Projeto A");

        Assert.True(vm.Diary.IsLoading, "o diário do projeto anterior ficou na tela");
        await vm.DetailLoading;
        Assert.Equal(7, vm.Diary.Source.Today);
        Assert.False(vm.Diary.IsLoading);
    }
}
