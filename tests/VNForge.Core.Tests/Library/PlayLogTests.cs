using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class PlayLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlaySession Session(int startHour, int minutes, bool interrupted = false) =>
        new($"s{startHour}-{minutes}", T0.AddHours(startHour), T0.AddHours(startHour).AddMinutes(minutes), TimeSpan.FromMinutes(minutes), interrupted);

    private static LibraryGame Game(string id, string title) =>
        new(id, Path.Combine(Path.GetTempPath(), id), "x.exe", LibraryEngine.Unknown, title, null, T0);

    [Fact]
    public void Ordem_da_biblioteca_poe_o_ultimo_jogado_no_topo_e_os_nunca_jogados_por_titulo()
    {
        var log = new PlayLog();
        log.Upsert("a", Session(0, 10));
        log.Upsert("c", Session(3, 10));
        var games = new[] { Game("z", "Zeta"), Game("a", "Alfa"), Game("c", "Charlie"), Game("b", "Bravo") };

        var ordered = log.ByLastPlayed(games);

        Assert.Equal(["c", "a", "b", "z"], ordered.Select(g => g.Id));
    }

    [Fact]
    public void Ida_e_volta_pelo_arquivo_guarda_as_sessoes_e_nao_deixa_temporario()
    {
        var file = new PlayLogFile(_dir.Path);
        var log = new PlayLog();
        log.Upsert("g1", Session(0, 30));
        log.Upsert("g2", Session(1, 5, interrupted: true));

        file.Save(log);
        var loaded = file.Load();

        Assert.Equal(PlayLogFile.Serialize(log), PlayLogFile.Serialize(loaded));
        Assert.Equal(Session(1, 5, interrupted: true), Assert.Single(loaded.SessionsOf("g2")));
        Assert.Equal(["playtime.json"], Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Arquivo_ilegivel_ou_de_outra_versao_e_recusado_e_continua_intacto()
    {
        var file = new PlayLogFile(_dir.Path);
        foreach (var text in new[] { "{ não é json", "null", """{"version":9,"games":{}}""" })
        {
            File.WriteAllText(file.Path, text);

            var error = Assert.Throws<LibraryException>(() => file.Load());

            Assert.Contains(file.Path, error.Message);
            Assert.Equal(text, File.ReadAllText(file.Path));
        }
    }

    [Fact]
    public void Sessao_invalida_e_descartada_com_aviso_e_as_outras_ficam()
    {
        var file = new PlayLogFile(_dir.Path);
        File.WriteAllText(file.Path, """
            {"version":1,"games":{
              "g1":[
                {"id":"a","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T19:00:00+00:00","duration":"00:30:00"},
                {"id":"b","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T21:00:00+00:00","duration":"-00:10:00"},
                {"id":"c","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T20:30:00+00:00","duration":"00:30:00"}
              ],
              "g2":[{"startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T20:10:00+00:00","duration":"00:10:00"}]
            }}
            """);

        var log = file.Load();

        Assert.Equal(["c"], log.SessionsOf("g1").Select(s => s.Id));
        Assert.Equal(2, log.Warnings.Count);
        Assert.All(log.Warnings, w => Assert.Contains("g1", w));
        Assert.False(string.IsNullOrEmpty(Assert.Single(log.SessionsOf("g2")).Id));
    }

    [Fact]
    public void Antes_de_regravar_sem_a_sessao_invalida_guarda_uma_copia_do_original_uma_vez()
    {
        var clock = new TestClock(T0);
        var file = new PlayLogFile(_dir.Path, clock);
        const string Original = """{"version":1,"games":{"g1":[{"id":"a","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T19:00:00+00:00","duration":"00:30:00"},{"id":"c","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T20:30:00+00:00","duration":"00:30:00"}]}}""";
        File.WriteAllText(file.Path, Original);

        file.Record("g2", Session(1, 5));
        clock.Advance(TimeSpan.FromMinutes(5));
        file.Record("g2", Session(2, 5));

        var copy = Assert.Single(Directory.GetFiles(_dir.Path, "playtime.json.invalido-*"));
        Assert.Equal("playtime.json.invalido-20261109-200000", Path.GetFileName(copy));
        Assert.Equal(Original, File.ReadAllText(copy));
        Assert.Equal(["c"], file.Load().SessionsOf("g1").Select(s => s.Id));
        Assert.Empty(file.Load().Warnings);
    }

    [Fact]
    public void BackUpDiscarded_repetido_devolve_a_mesma_copia_e_nada_descartado_nao_copia()
    {
        var file = new PlayLogFile(_dir.Path, new TestClock(T0));
        File.WriteAllText(file.Path, """{"version":1,"games":{"g1":[{"id":"a","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T19:00:00+00:00","duration":"00:30:00"}]}}""");

        var first = file.BackUpDiscarded(file.Load());
        var second = file.BackUpDiscarded(file.Load());

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(_dir.Path, "playtime.json.invalido-*"));
        Assert.Null(new PlayLogFile(_dir.Path).BackUpDiscarded(new PlayLog()));
    }

    [Fact]
    public void Sessao_com_o_mesmo_id_troca_a_anterior_e_sessao_invalida_nao_entra()
    {
        var log = new PlayLog();
        log.Upsert("g1", Session(0, 10) with { Open = true });
        log.Upsert("g1", Session(0, 10) with { Duration = TimeSpan.FromMinutes(25), EndedAt = T0.AddMinutes(25) });

        var only = Assert.Single(log.SessionsOf("g1"));
        Assert.Equal(TimeSpan.FromMinutes(25), only.Duration);
        Assert.False(only.Open);
        Assert.Throws<ArgumentException>(() => log.Upsert("g1", Session(1, 10) with { Duration = TimeSpan.FromMinutes(-1) }));
        Assert.Throws<ArgumentException>(() => log.Upsert("g1", Session(1, 10) with { EndedAt = T0 }));
    }

    [Fact]
    public void Sessao_aberta_sem_atualizacao_vira_interrompida_e_a_recente_fica()
    {
        var file = new PlayLogFile(_dir.Path);
        file.Record("g1", Session(0, 10) with { Open = true });
        file.Record("g2", Session(5, 10) with { Open = true });

        var closed = file.RecoverStale(T0.AddHours(5).AddMinutes(11), TimeSpan.FromMinutes(15));

        Assert.Equal(1, closed);
        var log = file.Load();
        var old = Assert.Single(log.SessionsOf("g1"));
        Assert.True(old.Interrupted);
        Assert.False(old.Open);
        Assert.Equal(TimeSpan.FromMinutes(10), old.Duration);
        Assert.True(Assert.Single(log.SessionsOf("g2")).Open);
    }

    [Theory]
    [InlineData(0, "menos de 1 min")]
    public void Tempo_aparece_em_horas_e_minutos(int seconds, string expected)
    {
        Assert.Equal(expected, PlayTime.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Opcao_de_localidade_e_caminho_do_Locale_Emulator_vao_e_voltam_pela_biblioteca()
    {
        var file = new LibraryFile(_dir.Path);
        var game = Game("g1", "Jogo") with { RequiresJapaneseLocale = true };
        var other = Game("g2", "Outro");

        file.Save(new GameLibrary([game, other], localeEmulatorPath: @"C:\LE"));
        var loaded = file.Load();

        Assert.Equal(@"C:\LE", loaded.LocaleEmulatorPath);
        Assert.True(loaded.Games[0].RequiresJapaneseLocale);
        Assert.False(loaded.Games[1].RequiresJapaneseLocale);
        var json = JsonDocument.Parse(File.ReadAllText(file.Path)).RootElement;
        Assert.Equal(1, json.GetProperty("games").EnumerateArray().Count(g => g.TryGetProperty("requiresJapaneseLocale", out _)));
    }
}
