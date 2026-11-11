using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

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
    public void Grava_em_cache_sessions_json_e_le_de_volta_igual()
    {
        var sessions = SessionsFile.Empty.With(Approval("yst00013:000001", Noon)).With(Approval("yst00013:000002", Noon.AddMinutes(3)));

        SessionsFile.Save(_project, sessions);

        Assert.Equal(Path.Combine(_project.Root, ".cache", "sessions.json"), SessionsFile.PathFor(_project));
        var loaded = SessionsFile.Load(_project);
        Assert.Equal(SessionsFile.CurrentVersion, loaded.Version);
        Assert.Equal(sessions.Approvals, loaded.Approvals);
    }

    [Fact]
    public void Gravar_com_a_pasta_do_projeto_movida_falha_como_IOException_e_nao_recria_a_pasta()
    {
        Directory.Delete(_project.Root, recursive: true);

        Assert.ThrowsAny<IOException>(() => SessionsFile.Save(_project, SessionsFile.Empty.With(Approval("yst00013:000001", Noon))));

        Assert.False(Directory.Exists(_project.Root));
    }

    [Theory]
    [InlineData("{\"version\": 99, \"approvals\": []}")]
    [InlineData("{\"version\": 1}")]
    public void Arquivo_ilegivel_ou_de_outra_versao_vira_registro_vazio(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), content);

        Assert.Empty(SessionsFile.Load(_project).Approvals);
    }

    [Fact]
    public void Registro_ilegivel_nao_impede_de_gravar_a_proxima_aprovacao()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), "não é json");

        Gravar(Approval("yst00013:000001", Noon));

        Assert.Single(SessionsFile.Load(_project).Approvals);
    }

    [Fact]
    public void Read_sem_arquivo_pode_gravar()
    {
        var read = SessionsFile.Read(_project);

        Assert.Equal(SessionsLoadStatus.Missing, read.Status);
        Assert.True(read.CanOverwrite);
    }

    [Fact]
    public void Read_de_arquivo_que_nao_abre_nao_deixa_gravar_por_cima()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), "{\"version\": 1, \"approvals\": []}");
        using var _ = new FileStream(SessionsFile.PathFor(_project), FileMode.Open, FileAccess.Read, FileShare.None);

        var read = SessionsFile.Read(_project);

        Assert.Equal(SessionsLoadStatus.Unreadable, read.Status);
        Assert.False(read.CanOverwrite);
    }

    [Fact]
    public void Without_tira_a_ultima_aprovacao_da_linha()
    {
        var sessions = SessionsFile.Empty
            .With(Approval("a", Noon))
            .With(Approval("b", Noon.AddMinutes(1)))
            .With(Approval("a", Noon.AddMinutes(2)));

        var without = sessions.Without("yst00013", "a");

        Assert.Equal([Noon, Noon.AddMinutes(1)], without.Approvals.Select(x => x.At));
        Assert.Equal(2, without.ReviewedOn(Noon, TimeZoneInfo.Utc));
        Assert.Same(sessions, sessions.Without("yst00013", "nao-existe"));
    }
}
