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
    public async Task O_diario_do_projeto_selecionado_chega_ao_Inicio_com_dias_seguidos_e_o_que_foi_aprovado_ontem()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        Seed(root, [.. Day(Now.AddHours(-1), 4), .. Day(Now.AddDays(-1), 120, 100), .. Day(Now.AddDays(-2), 10, 400)]);
        var vm = Create(temp, new FixedClock(Now));

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        Assert.Equal(3, vm.Diary.Source.Streak);
        Assert.Equal("3", vm.Diary.StreakValue);
        Assert.Equal("dias seguidos", vm.Diary.StreakCaption);
        Assert.Equal("Ontem você aprovou 120 linhas.", vm.Diary.YesterdayText);
        Assert.Equal("Hoje: 4 linhas.", vm.Diary.TodayText);
        Assert.Equal("134 linhas aprovadas nas últimas 12 semanas.", vm.Diary.TotalText);
        Assert.True(vm.Diary.HasActivity);
    }

    [Fact]
    public async Task Projeto_sem_aprovacoes_mostra_o_diario_vazio_com_o_convite()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var vm = Create(temp, new FixedClock(Now));

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        Assert.True(vm.Diary.IsEmpty);
        Assert.Equal("0", vm.Diary.StreakValue);
        Assert.Equal("Ontem você não aprovou linhas.", vm.Diary.YesterdayText);
        Assert.Equal("Hoje ainda não há linhas aprovadas.", vm.Diary.TodayText);
        Assert.Equal("Aprove algumas linhas e o diário começa a se preencher.", DiaryCard.EmptyText);
    }

    [Fact]
    public async Task Sem_projeto_selecionado_o_diario_volta_a_ficar_vazio()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        Seed(root, [.. Day(Now.AddHours(-1), 4)]);
        var vm = Create(temp, new FixedClock(Now));
        await vm.OpenAsync(root);
        await vm.DetailLoading;
        Assert.True(vm.Diary.HasActivity);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);
        await vm.DetailLoading;

        Assert.Null(vm.Selected);
        Assert.True(vm.Diary.IsEmpty);
    }

    [Fact]
    public void Um_dia_seguido_fica_no_singular()
    {
        var diary = new DiaryCard(new ReviewDiary([], 0, 0, 1, 1));

        Assert.Equal("dia seguido", diary.StreakCaption);
    }

    [Fact]
    public void O_resumo_ao_fechar_conta_o_que_foi_aprovado_desde_que_o_app_abriu_e_cita_os_dias_seguidos()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var clock = new FakeTimeProvider(Now);
        var vm = Create(temp, clock);
        Seed(root, [.. Day(Now.AddDays(-1), 50, 1_000), .. Day(Now.AddMinutes(5), 20)]);
        clock.Advance(TimeSpan.FromMinutes(40));

        var text = vm.CloseSummary(root);

        Assert.Equal("Nesta sessão: 20 linhas aprovadas em cerca de 10 min. Já são 2 dias seguidos.", text);
    }

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
    public void O_resumo_de_uma_linha_so_e_o_primeiro_dia_nao_fala_de_sequencia()
    {
        var text = DiaryCard.SessionText(new SessionSummary(1, TimeSpan.FromSeconds(20)), new ReviewDiary([], 1, 0, 1, 1));

        Assert.Equal("Nesta sessão: 1 linha aprovada em menos de 1 min.", text);
    }

    [Fact]
    public void Antes_da_primeira_leitura_o_diario_nao_mostra_o_convite_nem_numeros()
    {
        var pending = DiaryCard.Pending;

        Assert.True(pending.IsLoading);
        Assert.False(pending.IsEmpty);
        Assert.False(pending.HasActivity);
        Assert.Equal(string.Empty, pending.StreakValue);
        Assert.Equal(string.Empty, pending.StreakCaption);
        Assert.Equal(string.Empty, pending.YesterdayText);
        Assert.Equal(string.Empty, pending.TodayText);
        Assert.False(new DiaryCard(ReviewDiary.Empty).IsLoading);
        Assert.True(new StartViewModel(new RecentProjectsFile(Path.Combine(Path.GetTempPath(), "VNForge-sem-recentes")), new FakeFolderPicker(null), new FixedClock(Now)).Diary.IsLoading);
    }

    [Fact]
    public void O_titulo_do_painel_usa_o_numero_de_semanas_do_diario()
    {
        Assert.Equal($"Últimas {ReviewDiary.Weeks} semanas · um quadrado por dia", DiaryCard.RangeText);
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
