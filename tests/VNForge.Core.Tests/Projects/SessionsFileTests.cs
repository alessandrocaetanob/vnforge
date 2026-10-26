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
    public void Sem_arquivo_o_registro_e_vazio()
    {
        Assert.Equal(SessionsFile.Empty, SessionsFile.Load(_project));
        Assert.Empty(SessionsFile.Load(_project).Approvals);
    }

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
    public void O_json_guarda_a_hora_de_cada_aprovacao()
    {
        Gravar(Approval("yst00013:000001", Noon));

        var text = File.ReadAllText(SessionsFile.PathFor(_project));

        Assert.Contains("\"scriptKey\": \"yst00013\"", text);
        Assert.Contains("\"unitId\": \"yst00013:000001\"", text);
        Assert.Contains("\"at\": \"2026-10-25T15:00:00+00:00\"", text);
    }

    [Fact]
    public void Gravar_nao_deixa_arquivo_temporario_para_tras()
    {
        Gravar(Approval("yst00013:000001", Noon));
        Gravar(Approval("yst00013:000002", Noon));

        Assert.Equal([SessionsFile.PathFor(_project)], Directory.GetFiles(_project.CacheDirectory, "sessions*"));
    }

    [Fact]
    public void Gravar_de_novo_acrescenta_ao_que_ja_estava_gravado()
    {
        Gravar(Approval("yst00013:000001", Noon));
        Gravar(Approval("yst00013:000002", Noon.AddMinutes(1)));

        Assert.Equal(["yst00013:000001", "yst00013:000002"], SessionsFile.Load(_project).Approvals.Select(a => a.UnitId));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{\"version\": 99, \"approvals\": []}")]
    [InlineData("{\"version\": 1}")]
    [InlineData("{\"version\": 1, \"approvals\": [{\"at\": \"ontem\"}]}")]
    public void Arquivo_ilegivel_ou_de_outra_versao_vira_registro_vazio(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), content);

        Assert.Empty(SessionsFile.Load(_project).Approvals);
    }

    [Fact]
    public void Itens_nulos_da_lista_sao_ignorados_na_leitura()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), "{\"version\": 1, \"approvals\": [null, {\"scriptKey\": \"yst00013\", \"unitId\": \"yst00013:000001\", \"at\": \"2026-10-25T15:00:00+00:00\"}]}");

        var loaded = SessionsFile.Load(_project);

        Assert.Equal([Approval("yst00013:000001", Noon)], loaded.Approvals);
        Assert.Equal(1, loaded.ReviewedOn(Noon, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Registro_ilegivel_nao_impede_de_gravar_a_proxima_aprovacao()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), "não é json");

        Gravar(Approval("yst00013:000001", Noon));

        Assert.Single(SessionsFile.Load(_project).Approvals);
    }

    [Theory]
    [InlineData("não é json", SessionsLoadStatus.Corrupt, true)]
    [InlineData("{\"version\": 1}", SessionsLoadStatus.Corrupt, true)]
    [InlineData("{\"version\": 2, \"approvals\": [], \"extra\": 1}", SessionsLoadStatus.NewerVersion, false)]
    [InlineData("{\"version\": 1, \"approvals\": []}", SessionsLoadStatus.Ok, true)]
    public void Read_diz_se_da_para_gravar_por_cima_do_arquivo(string content, SessionsLoadStatus status, bool canOverwrite)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(SessionsFile.PathFor(_project), content);

        var read = SessionsFile.Read(_project);

        Assert.Equal(status, read.Status);
        Assert.Equal(canOverwrite, read.CanOverwrite);
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

    [Fact]
    public void Aprovacoes_com_mais_de_noventa_dias_saem_na_gravacao()
    {
        var sessions = SessionsFile.Empty
            .With(Approval("antiga", Noon.AddDays(-91)))
            .With(Approval("recente", Noon.AddDays(-89)))
            .With(Approval("agora", Noon));

        Assert.Equal(["recente", "agora"], sessions.Approvals.Select(a => a.UnitId));
    }

    [Fact]
    public void ReviewedOn_conta_as_linhas_do_dia_no_fuso_da_pessoa()
    {
        var saoPaulo = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
        var sessions = SessionsFile.Empty
            .With(Approval("ontem-a-noite", new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.Zero)))
            .With(Approval("a", new DateTimeOffset(2026, 10, 25, 3, 30, 0, TimeSpan.Zero)))
            .With(Approval("b", new DateTimeOffset(2026, 10, 26, 2, 59, 0, TimeSpan.Zero)))
            .With(Approval("amanha", new DateTimeOffset(2026, 10, 26, 3, 1, 0, TimeSpan.Zero)));

        Assert.Equal(2, sessions.ReviewedOn(new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero), saoPaulo));
        Assert.Equal(1, sessions.ReviewedOn(new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.Zero), saoPaulo));
    }

    [Fact]
    public void ReviewedOn_conta_uma_vez_a_linha_aprovada_duas_vezes_no_dia()
    {
        var sessions = SessionsFile.Empty
            .With(Approval("yst00013:000001", Noon))
            .With(Approval("yst00013:000001", Noon.AddMinutes(10)))
            .With(Approval("yst00013:000002", Noon.AddMinutes(11)));

        Assert.Equal(2, sessions.ReviewedOn(Noon.AddHours(1), TimeZoneInfo.Utc));
    }
}
