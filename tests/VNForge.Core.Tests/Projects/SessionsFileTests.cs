using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class SessionsFileTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public SessionsFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static ApprovalRecord Approval(string unit, DateTimeOffset at) => new("yst00013", unit, at);

    private void Gravar(params ApprovalRecord[] approvals) =>
        SessionsFile.Save(_project, approvals.Aggregate(SessionsFile.Load(_project), (file, approval) => file.With(approval)));

    [Fact]
    public void Registro_ilegivel_nao_impede_de_gravar_a_proxima_aprovacao()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), "não é json");

        Gravar(Approval("yst00013:000001", Noon));

        Assert.Single(SessionsFile.Load(_project).Approvals);
    }
}
