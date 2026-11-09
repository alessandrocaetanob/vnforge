using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class PlayLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static PlaySession Session(int startHour, int minutes, bool interrupted = false) =>
        new(T0.AddHours(startHour), T0.AddHours(startHour).AddMinutes(minutes), TimeSpan.FromMinutes(minutes), interrupted);

    private static LibraryGame Game(string id, string title) =>
        new(id, Path.Combine(Path.GetTempPath(), id), "x.exe", LibraryEngine.Unknown, title, null, T0);

    [Fact]
    public void Tempo_total_e_ultimo_jogado_saem_das_sessoes()
    {
        var log = new PlayLog();
        log.Add("g1", Session(0, 30));
        log.Add("g1", Session(5, 45));
        log.Add("g1", Session(2, 10));

        var stats = log.StatsOf("g1");

        Assert.Equal(TimeSpan.FromMinutes(85), stats.Total);
        Assert.Equal(T0.AddHours(5).AddMinutes(45), stats.LastPlayedAt);
        Assert.Equal(3, stats.Sessions);
        Assert.Equal(PlayStats.None, log.StatsOf("nunca"));
    }

    [Fact]
    public void Ordem_da_biblioteca_poe_o_ultimo_jogado_no_topo_e_os_nunca_jogados_por_titulo()
    {
        var log = new PlayLog();
        log.Add("a", Session(0, 10));
        log.Add("c", Session(3, 10));
        var games = new[] { Game("z", "Zeta"), Game("a", "Alfa"), Game("c", "Charlie"), Game("b", "Bravo") };

        var ordered = log.ByLastPlayed(games);

        Assert.Equal(["c", "a", "b", "z"], ordered.Select(g => g.Id));
    }

    [Fact]
    public void Ida_e_volta_pelo_arquivo_guarda_as_sessoes_e_nao_deixa_temporario()
    {
        var file = new PlayLogFile(_dir.Path);
        var log = new PlayLog();
        log.Add("g1", Session(0, 30));
        log.Add("g2", Session(1, 5, interrupted: true));

        file.Save(log);
        var loaded = file.Load();

        Assert.Equal(PlayLogFile.Serialize(log), PlayLogFile.Serialize(loaded));
        Assert.Equal(Session(1, 5, interrupted: true), Assert.Single(loaded.SessionsOf("g2")));
        Assert.Equal(["playtime.json"], Directory.GetFiles(_dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Gravar_uma_sessao_nao_perde_a_que_outro_processo_gravou_no_meio()
    {
        var file = new PlayLogFile(_dir.Path);
        var stale = file.Record("g1", Session(0, 30));

        new PlayLogFile(_dir.Path).Record("g2", Session(1, 20));
        file.Record("g1", Session(2, 10));

        var loaded = file.Load();
        Assert.Equal(2, loaded.SessionsOf("g1").Count);
        Assert.Single(loaded.SessionsOf("g2"));
        Assert.Single(stale.SessionsOf("g1"));
    }

    [Fact]
    public void Sessao_sem_interrupcao_nao_escreve_o_campo()
    {
        var log = new PlayLog();
        log.Add("g1", Session(0, 30));

        var json = PlayLogFile.Serialize(log);

        Assert.DoesNotContain("interrupted", json);
        Assert.Contains("\"duration\": \"00:30:00\"", json);
    }

    [Fact]
    public void Arquivo_ilegivel_ou_de_outra_versao_e_recusado_e_continua_intacto()
    {
        var file = new PlayLogFile(_dir.Path);
        foreach (var text in new[] { "{ não é json", "null", """{"version":9,"games":{}}""", """{"version":1,"games":{"g1":[{"startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T19:00:00+00:00","duration":"00:30:00"}]}}""" })
        {
            File.WriteAllText(file.Path, text);

            var error = Assert.Throws<LibraryException>(() => file.Load());

            Assert.Contains(file.Path, error.Message);
            Assert.Equal(text, File.ReadAllText(file.Path));
        }
    }

    [Fact]
    public void Arquivo_que_nao_existe_e_um_historico_vazio()
    {
        Assert.Empty(new PlayLogFile(_dir.Path).Load().All);
    }

    [Theory]
    [InlineData(0, "menos de 1 min")]
    [InlineData(59, "menos de 1 min")]
    [InlineData(60, "1 min")]
    [InlineData(35 * 60, "35 min")]
    [InlineData(60 * 60, "1 h 00 min")]
    [InlineData((2 * 3600) + (5 * 60) + 40, "2 h 05 min")]
    [InlineData(130 * 3600, "130 h 00 min")]
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

    [Fact]
    public void Biblioteca_sem_o_caminho_nao_escreve_o_campo()
    {
        Assert.DoesNotContain("localeEmulatorPath", LibraryFile.Serialize(new GameLibrary()));
    }
}
