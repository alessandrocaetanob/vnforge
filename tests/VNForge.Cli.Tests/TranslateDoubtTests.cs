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
