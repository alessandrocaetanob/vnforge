using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O registro de aprovações do app: na memória como fonte, gravação fora da thread da tela, e nunca por cima de um arquivo que não foi lido.</summary>
[Trait("Risco", "R2")]
public sealed class ApprovalLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly string raiz;

    public ApprovalLogTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Machine);
    }

    public void Dispose() => pasta.Dispose();

    private ProjectFolder Projeto => ProjectFolder.Open(raiz);

    [Fact]
    public async Task Arquivo_de_versao_mais_nova_nunca_e_sobrescrito()
    {
        Directory.CreateDirectory(Projeto.CacheDirectory);
        const string Futuro = "{\"version\": 2, \"approvals\": [], \"sessions\": [{\"id\": 1}]}";
        File.WriteAllText(SessionsFile.PathFor(Projeto), Futuro);
        var log = new ProjectApprovalLog(raiz);

        log.Record("yst00001", "yst00001:000001", Noon);
        log.Record("yst00001", "yst00001:000002", Noon);
        await log.Saving;

        Assert.Equal(Futuro, File.ReadAllText(SessionsFile.PathFor(Projeto)));
        Assert.Equal(2, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
    }
}
