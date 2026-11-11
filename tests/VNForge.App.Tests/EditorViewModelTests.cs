using System.Diagnostics;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

public class EditorViewModelTests
{
    private static ProjectLines DoisScripts() => Lines(
        new QaReport([new QaIssue("regra", QaSeverity.Warning, "yst00001:000002", "yst00001", "problema")], [], ["regra"]),
        ("yst00002", [Unit("yst00002", 1, "Ken", TranslationStatus.Approved)]),
        ("yst00001",
        [
            Unit("yst00001", 1, "Ana", TranslationStatus.Untranslated),
            Unit("yst00001", 2, "Ana", TranslationStatus.Machine),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
            Unit("yst00001", 4, "Ken", TranslationStatus.Approved),
            Unit("yst00001", 5, "Ana", TranslationStatus.Reviewed),
        ]));

    private static async Task<EditorViewModel> Carregado(ProjectLines? lines = null)
    {
        var vm = new EditorViewModel(_ => lines ?? DoisScripts());
        vm.Project = Summary();
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Carga_seleciona_o_primeiro_script_e_preenche_as_linhas()
    {
        var vm = await Carregado();

        Assert.Equal(["yst00001", "yst00002"], vm.Scripts.Select(s => s.Key));
        Assert.Equal("yst00001", vm.SelectedScript?.Key);
        Assert.Equal("Cena 1", vm.ScriptTitle);
        Assert.Equal(5, vm.Lines.Count);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.Equal("2", vm.ScriptsCount);
        Assert.Equal("5 linhas nesta cena", vm.LinesSummary);
        Assert.Equal("Minha VN", vm.ProjectName);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Linha_expoe_os_textos_da_grade()
    {
        var vm = await Carregado();

        var pendente = vm.Lines[0];
        Assert.Equal("Em japonês", pendente.StatusLabel);
        Assert.Equal("sem tradução", pendente.TranslationLabel);
        Assert.False(pendente.HasTranslation);
        Assert.Equal("原文1", pendente.Source);
        Assert.Equal("Ana", pendente.SpeakerLabel);
        Assert.False(pendente.IsNarration);
        Assert.Equal(string.Empty, pendente.IssueLabel);

        var ia = vm.Lines[1];
        Assert.Equal("tradução 2", ia.TranslationLabel);
        Assert.True(ia.HasWarning);
        Assert.False(ia.HasError);
        Assert.Equal("1 aviso do QA", ia.IssueLabel);
    }

    [Fact]
    public void Rotulo_do_QA_conta_erros_e_avisos()
    {
        var unit = Unit("a", 1, null, TranslationStatus.Approved);

        string Rotulo(params QaSeverity[] severities) =>
            new LineViewModel(new LineEntry("a", 1, unit, [.. severities.Select(s => new QaIssue("r", s, unit.Id, "a", "m"))])).IssueLabel;

        Assert.Equal("2 avisos do QA", Rotulo(QaSeverity.Warning, QaSeverity.Warning));
        Assert.Equal("1 erro do QA", Rotulo(QaSeverity.Error));
        Assert.Equal("2 erros e 1 aviso do QA", Rotulo(QaSeverity.Error, QaSeverity.Warning, QaSeverity.Error));
        Assert.True(new LineViewModel(new LineEntry("a", 1, unit, [new QaIssue("r", QaSeverity.Error, unit.Id, "a", "m")])).HasError);
    }

    [Fact]
    public async Task Carga_bem_sucedida_limpa_o_erro_anterior()
    {
        var falha = true;
        var vm = new EditorViewModel(_ => falha ? throw new VNForgeException("quebrou") : DoisScripts());
        vm.Project = Summary();
        await vm.Loading;
        Assert.NotNull(vm.ErrorMessage);

        falha = false;
        await vm.LoadAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.Scripts.Count);
    }

    [Fact]
    public async Task Carga_mais_antiga_que_termina_depois_e_descartada()
    {
        using var liberar = new ManualResetEventSlim();
        var vm = new EditorViewModel(root =>
        {
            if (root == "primeiro")
            {
                liberar.Wait();
                return Lines(("velho", [Unit("velho", 1, null, TranslationStatus.Approved)]));
            }

            return Lines(("novo", [Unit("novo", 1, null, TranslationStatus.Approved)]));
        });

        vm.Project = Summary(root: "primeiro");
        var primeira = vm.Loading;
        vm.Project = Summary(name: "Outra", root: "segundo");
        await vm.Loading;
        liberar.Set();
        await primeira;

        Assert.Equal(["novo"], vm.Scripts.Select(s => s.Key));
        Assert.Equal("Cena 1", vm.ScriptTitle);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Proxima_anda_so_entre_as_linhas_filtradas()
    {
        var vm = await Carregado();
        Filtrar(vm, LineStatusFilter.Pending);

        vm.SelectNextCommand.Execute(null);

        Assert.Equal(3, vm.SelectedLine?.Number);
    }

    private static void Filtrar(EditorViewModel vm, LineStatusFilter filter) => vm.SelectFilterCommand.Execute(Chip(vm, filter));

    private static FilterChipViewModel Chip(EditorViewModel vm, LineStatusFilter filter) => vm.Filters.Single(f => f.Filter == filter);
}
