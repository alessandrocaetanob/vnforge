using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class TranslateDoubtTests : IDisposable
{
    private const string UnitId = "yst00001:000001";

    private static readonly TranslationDoubt Duvida = new(DoubtKind.Pun, "Trocadilho com 'hashi'.");

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;
    private readonly ScriptedProvider _provider = new();

    public TranslateDoubtTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _cli.Cli.TranslationProviderFactory = (_, _) => _provider;
    }

    public void Dispose() => _cli.Dispose();

    private void Seed(TranslationDoubt? doubt) =>
        _project.SaveText("yst00001", [new TextUnit(UnitId, TextUnitKind.Dialogue, null, "原文", "Texto", TranslationStatus.Untranslated) { Doubt = doubt }]);

    private async Task TranslateAsync() =>
        Assert.Equal(0, (await _cli.RunAsync(["translate", "--project", _projectPath], TestContext.Current.CancellationToken)).ExitCode);

    [Fact]
    public async Task Resposta_nova_sem_duvida_encerra_a_duvida_antiga_mesmo_com_o_mesmo_texto()
    {
        Seed(Duvida);

        await TranslateAsync();

        Assert.Equal(1, _provider.Calls);
        Assert.Null(_project.LoadText("yst00001").Single().Doubt);
    }

    [Fact]
    public async Task Resposta_com_duvida_grava_a_duvida_e_a_linha_servida_do_cache_mantem_a_que_tinha()
    {
        Seed(null);
        _provider.Doubt = Duvida;
        await TranslateAsync();
        Assert.Equal(Duvida, _project.LoadText("yst00001").Single().Doubt);

        Seed(Duvida);
        await TranslateAsync();

        Assert.Equal(1, _provider.Calls);
        Assert.Equal(Duvida, _project.LoadText("yst00001").Single().Doubt);
    }

    private sealed class ScriptedProvider : ITranslationProvider
    {
        public int Calls { get; private set; }

        public TranslationDoubt? Doubt { get; set; }

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TranslationResult>>([.. batch.Lines.Select(l => new TranslationResult(l.Id, "Texto", Doubt: Doubt))]);
        }
    }
}
