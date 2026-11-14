using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R4")]
public sealed class ConferenceFitTests : IDisposable
{

    private readonly TempFolder pasta = new();
    private readonly ProjectFolder folder;

    public ConferenceFitTests()
    {
        folder = ProjectFolder.Open(SampleProjects.Create(pasta.SubFolder("vn"), "Minha VN", TranslationStatus.Translated));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
    }

    public void Dispose() => pasta.Dispose();

    private static string Palavras(int quantas) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", quantas));

    private string Gravada(string id = "yst00001:000000") => folder.LoadText("yst00001").Single(u => u.Id == id).Translation!;

    [Fact]
    public async Task Corrigir_e_ir_para_o_proximo_divide_a_fala_e_mantem_os_marcadores_do_original_mais_a_virada_a_mais()
    {
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, @"原文\p", $@"{Palavras(16)}\p", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root), Shortener = null };
        await vm.RefreshAsync();
        vm.SelectedRow = vm.Rows.Single(r => r.RuleId == BoxLineLimitRule.RuleId);

        Assert.True(vm.FixAndNextCommand.CanExecute(null));
        await vm.FixAndNextCommand.ExecuteAsync(null);

        var gravada = Gravada();
        Assert.Equal($@"{Palavras(12)}\p{Palavras(4)}\p", gravada);
        Assert.Equal(1, ControlMarkers.Markers(@"原文\p").Count(m => m == ControlMarkers.PageBreak));
        Assert.Equal(2, ControlMarkers.Markers(gravada).Count(m => m == ControlMarkers.PageBreak));
        Assert.DoesNotContain(vm.Rows, r => r.RuleId == BoxLineLimitRule.RuleId);
        Assert.Contains(vm.Rows, r => r.RuleId == ExtraPageQaRule.RuleId);
        Assert.Equal("Fala dividida em páginas.", vm.NoticeMessage);
    }

    private sealed class EncurtadorParado(TaskCompletionSource<string> resposta) : ILineShortener
    {
        public Task<string> ShortenAsync(TextUnit unit, string current, WrapRule box, CancellationToken ct) => resposta.Task;
    }

}
