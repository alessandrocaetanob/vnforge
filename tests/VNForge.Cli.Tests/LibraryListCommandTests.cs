using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

public sealed class LibraryListCommandTests : IDisposable
{
    private const string Secret = FakeVndbCatalog.ValidToken;

    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_cli.UserConfigDirectory);

    private static VndbListEntry Entry(string id, int? vote, DateTimeOffset lastModified, params int[] labels) =>
        new(id, "Jogo", T0, lastModified, null, vote, null, null, null, [.. labels.Select(LibraryListLabels.Named)]);

    private FakeVndbCatalog Prepare(IEnumerable<VndbListEntry>? remote = null, bool withToken = true)
    {
        var games = new[]
        {
            new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0) { VndbId = "v4", Confirmation = LibraryConfirmation.Manual },
            new LibraryGame("g2", "C:\\Jogos\\Outro", null, LibraryEngine.Unknown, "Outro", null, T0),
        };
        File.Save(new GameLibrary(games));
        var fake = new FakeVndbCatalog(list: remote ?? []);
        _cli.Cli.VndbUserListFactory = () => fake;
        if (withToken)
        {
            _cli.Environment["VNDB_TOKEN"] = Secret;
        }

        return fake;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private static bool Updated(FakeVndbCatalog fake) => fake.Calls.Any(c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));

    private LibraryGame Saved() => File.Load().Find("g1");

    [Fact]
    public async Task Status_sem_opcoes_mostra_a_lista_do_jogo()
    {
        Prepare();

        var result = await Run("library", "status", "CLANNAD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CLANNAD (v4)", result.Output, StringComparison.Ordinal);
        Assert.Contains("nada ainda", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_grava_rotulo_e_voto_so_aqui_e_nao_chama_o_VNDB()
    {
        var fake = Prepare();

        var result = await Run("library", "status", "CLANNAD", "--label", "terminado", "--vote", "8,5", "--finished", "2026-03-04", "--notes", "Chorei");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Rótulos: Terminado", result.Output, StringComparison.Ordinal);
        Assert.Contains("Voto: 8,5", result.Output, StringComparison.Ordinal);
        Assert.Contains("Fim: 2026-03-04", result.Output, StringComparison.Ordinal);
        Assert.Contains("Gravado só aqui", result.Output, StringComparison.Ordinal);
        var state = Saved().CurrentList!;
        Assert.Equal(85, state.Fields.Vote);
        Assert.Equal([2], state.Fields.Labels.Select(l => l.Id));
        Assert.Equal("Chorei", state.Fields.Notes);
        Assert.True(state.Pending);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Status_tira_rotulo_e_apaga_o_voto_com_valor_vazio()
    {
        Prepare();
        await Run("library", "status", "CLANNAD", "--label", "jogando", "--label", "terminado", "--vote", "7");

        var result = await Run("library", "status", "CLANNAD", "--unlabel", "jogando", "--vote", string.Empty);

        Assert.Equal(0, result.ExitCode);
        var state = Saved().CurrentList!;
        Assert.Equal([2], state.Fields.Labels.Select(l => l.Id));
        Assert.Null(state.Fields.Vote);
    }

    [Theory]
    [InlineData("--label", "inventado")]
    [InlineData("--label", "voted")]
    [InlineData("--vote", "11")]
    [InlineData("--vote", "8.55")]
    [InlineData("--started", "04/03/2026")]
    public async Task Status_recusa_valor_invalido_sem_gravar(string option, string value)
    {
        Prepare();

        var result = await Run("library", "status", "CLANNAD", option, value);

        Assert.Equal(1, result.ExitCode);
        Assert.Null(Saved().List);
    }

    [Fact]
    public async Task Status_recusa_o_mesmo_rotulo_em_label_e_unlabel_e_jogo_sem_confirmacao()
    {
        Prepare();

        var both = await Run("library", "status", "CLANNAD", "--label", "terminado", "--unlabel", "terminado");
        var unconfirmed = await Run("library", "status", "Outro", "--label", "terminado");

        Assert.Equal(1, both.ExitCode);
        Assert.Equal(1, unconfirmed.ExitCode);
        Assert.Contains("confirm", unconfirmed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_com_push_envia_so_este_jogo_e_deixa_a_lista_em_dia()
    {
        var fake = Prepare();

        var result = await Run("library", "status", "CLANNAD", "--label", "terminado", "--vote", "8.5", "--push", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Enviado ao VNDB: rótulos, voto", result.Output, StringComparison.Ordinal);
        var remote = (await fake.GetListAsync("u1", Secret, Ct)).Items.Single();
        Assert.Equal(85, remote.Vote);
        Assert.Contains(remote.Labels, l => l.Id == 2);
        Assert.False(Saved().CurrentList!.Pending);
    }

    [Fact]
    public async Task Status_com_push_sem_token_diz_como_configurar_e_deixa_a_edicao_gravada()
    {
        Prepare(withToken: false);

        var result = await Run("library", "status", "CLANNAD", "--label", "terminado", "--push", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnf vndb token set", result.Error, StringComparison.Ordinal);
        Assert.NotNull(Saved().CurrentList);
    }

    [Fact]
    public async Task Status_com_push_pergunta_antes_e_recusar_deixa_a_edicao_gravada_sem_enviar()
    {
        var fake = Prepare();
        _cli.SetInput("n\n");

        var result = await Run("library", "status", "CLANNAD", "--vote", "9", "--push");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Nada foi enviado", result.Output, StringComparison.Ordinal);
        Assert.False(Updated(fake));
        Assert.Equal(90, Saved().CurrentList!.Fields.Vote);
    }

    [Fact]
    public async Task Status_com_push_e_json_exige_yes_e_nao_grava_nada_sem_ele()
    {
        var fake = Prepare();

        var result = await Run("library", "status", "CLANNAD", "--vote", "9", "--push", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--yes", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Null(Saved().List);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Status_avisa_quando_o_jogo_fica_Jogando_e_Terminado_ao_mesmo_tempo()
    {
        Prepare();

        var result = await Run("library", "status", "CLANNAD", "--label", "jogando", "--label", "terminado");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso: o jogo está como Jogando e também como Terminado", result.Output, StringComparison.Ordinal);
        Assert.Contains("--unlabel jogando", result.Output, StringComparison.Ordinal);
        Assert.Equal([1, 2], Saved().CurrentList!.Fields.Labels.Select(l => l.Id));
    }

    private void SeedDeletedOnSite(bool edited = true)
    {
        var fields = new LibraryListFields { Vote = 70, Labels = [LibraryListLabels.Named(2)] };
        var game = new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0)
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            List = new LibraryListState("v4", edited ? fields with { Vote = 95 } : fields, T0.AddDays(3)) { Base = fields, SyncedAt = T0, OnVndb = true },
        };
        File.Save(new GameLibrary([game]));
    }

    [Fact]
    public async Task Entrada_apagada_no_site_aparece_no_plano_e_o_status_push_recusa_sem_recriar()
    {
        var fake = Prepare();
        SeedDeletedOnSite();

        var plan = await Run("library", "sync", "--dry-run");
        var push = await Run("library", "status", "CLANNAD", "--notes", "Ainda aqui", "--push", "--yes");

        Assert.Equal(0, plan.ExitCode);
        Assert.Contains("foi apagado da sua lista do VNDB", plan.Output, StringComparison.Ordinal);
        Assert.Contains("--recreate", plan.Output, StringComparison.Ordinal);
        Assert.Equal(1, push.ExitCode);
        Assert.Contains("apagada na sua lista do VNDB", push.Error, StringComparison.Ordinal);
        Assert.False(Updated(fake));
        Assert.Equal("Ainda aqui", Saved().CurrentList!.Fields.Notes);
    }

    [Fact]
    public async Task Status_push_sem_edicao_e_com_a_entrada_apagada_diz_a_verdade_e_manda_trazer_a_exclusao()
    {
        var fake = Prepare();
        SeedDeletedOnSite(edited: false);

        var push = await Run("library", "status", "CLANNAD", "--push", "--yes");
        var syncPush = await Run("library", "sync", "--push", "--yes");

        Assert.Equal(1, push.ExitCode);
        Assert.Contains("foi apagada na sua lista do VNDB", push.Error, StringComparison.Ordinal);
        Assert.Contains("não há mudança aqui", push.Error, StringComparison.Ordinal);
        Assert.Contains("vnf library sync", push.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("--recreate", push.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("edição ficou gravada", push.Error, StringComparison.Ordinal);
        Assert.Equal(0, syncPush.ExitCode);
        Assert.Contains("--push não traz a exclusão", syncPush.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--recreate", syncPush.Output, StringComparison.Ordinal);
        Assert.False(Updated(fake));
        Assert.True(Saved().CurrentList!.OnVndb);
    }

    [Fact]
    public async Task Sync_com_recreate_recria_a_entrada_apagada_e_sem_ele_nao()
    {
        var fake = Prepare();
        SeedDeletedOnSite();

        var without = await Run("library", "sync", "--yes");
        var withIt = await Run("library", "sync", "--recreate", "--yes");
        var bad = await Run("library", "sync", "--recreate", "--pull");

        Assert.Equal(0, without.ExitCode);
        Assert.Contains("não foi recriado", without.Output, StringComparison.Ordinal);
        Assert.Equal(0, withIt.ExitCode);
        var remote = (await fake.GetListAsync("u1", Secret, Ct)).Items.Single();
        Assert.Equal(95, remote.Vote);
        Assert.Contains(remote.Labels, l => l.Id == 2);
        Assert.Equal(1, bad.ExitCode);
    }

    [Fact]
    public async Task Sync_dry_run_mostra_o_que_faria_sem_gravar_nada()
    {
        var fake = Prepare([Entry("v4", 90, T0.AddDays(2), 2)]);
        await Run("library", "status", "Outro", "--label", "terminado");

        var result = await Run("library", "sync", "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("simulação", result.Output, StringComparison.Ordinal);
        Assert.Contains("CLANNAD: traria rótulos, voto", result.Output, StringComparison.Ordinal);
        Assert.Contains("sem --dry-run", result.Output, StringComparison.Ordinal);
        Assert.Null(Saved().List);
        Assert.False(Updated(fake));
    }

    [Fact]
    public async Task Sync_traz_a_lista_do_VNDB_para_os_jogos_confirmados()
    {
        Prepare([Entry("v4", 90, T0.AddDays(2), 2), Entry("v99", 50, T0, 1)]);

        var result = await Run("library", "sync", "--json");

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(1, data.GetProperty("pulled").GetInt32());
        Assert.Equal(0, data.GetProperty("pushed").GetInt32());
        Assert.Equal(1, data.GetProperty("remoteOnly").GetInt32());
        Assert.False(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(90, Saved().CurrentList!.Fields.Vote);
        Assert.DoesNotContain(Secret, result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_pergunta_antes_de_enviar_e_recusar_nao_manda_nada()
    {
        var fake = Prepare();
        await Run("library", "status", "CLANNAD", "--vote", "9");
        _cli.SetInput("n\n");

        var result = await Run("library", "sync");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Nada foi enviado", result.Output, StringComparison.Ordinal);
        Assert.False(Updated(fake));
        Assert.True(Saved().CurrentList!.Pending);
    }

    [Fact]
    public async Task Sync_com_yes_envia_e_a_segunda_rodada_nao_tem_o_que_fazer()
    {
        var fake = Prepare();
        await Run("library", "status", "CLANNAD", "--vote", "9", "--label", "terminado");

        var first = await Run("library", "sync", "--yes");
        var second = await Run("library", "sync", "--yes");

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("1 enviados", first.Output, StringComparison.Ordinal);
        Assert.Equal(90, (await fake.GetListAsync("u1", Secret, Ct)).Items.Single().Vote);
        Assert.Contains("0 enviados", second.Output, StringComparison.Ordinal);
        Assert.Single(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_com_json_sem_yes_nao_envia_porque_nao_ha_como_perguntar()
    {
        var fake = Prepare();
        await Run("library", "status", "CLANNAD", "--vote", "9");

        var result = await Run("library", "sync", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.False(Updated(fake));
    }

    [Fact]
    public async Task Sync_pull_nao_pergunta_nem_envia_e_pull_com_push_e_erro_de_uso()
    {
        var fake = Prepare([Entry("v4", 70, T0, 1)]);
        await Run("library", "status", "CLANNAD", "--vote", "9");

        var both = await Run("library", "sync", "--pull", "--push");
        var pull = await Run("library", "sync", "--pull");

        Assert.Equal(1, both.ExitCode);
        Assert.Equal(0, pull.ExitCode);
        Assert.False(Updated(fake));
    }

    [Fact]
    public async Task Conflito_sai_como_aviso_com_quem_venceu()
    {
        Prepare([Entry("v4", 60, T0.AddDays(-5), 1)]);
        await Run("library", "status", "CLANNAD", "--vote", "9.5");

        var result = await Run("library", "sync", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso: CLANNAD: o campo voto mudou dos dois lados (aqui: 9,5; VNDB: 6). Venceu o que está aqui.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_sem_token_diz_como_configurar_e_nao_toca_na_biblioteca()
    {
        Prepare(withToken: false);

        var result = await Run("library", "sync");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("VNDB_TOKEN", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_com_token_recusado_falha_sem_mostrar_o_token()
    {
        Prepare();
        _cli.Environment["VNDB_TOKEN"] = "token-vencido";

        var result = await Run("library", "sync");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("token-vencido", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ficha_e_json_do_jogo_trazem_a_lista_e_o_token_nao_vai_para_o_arquivo_da_biblioteca()
    {
        Prepare();
        await Run("library", "status", "CLANNAD", "--label", "terminado", "--vote", "8");

        var sheet = await Run("library", "list", "CLANNAD");
        var json = await Run("library", "list", "CLANNAD", "--json");

        Assert.Contains("Rótulos: Terminado", sheet.Output, StringComparison.Ordinal);
        var list = Data(json).GetProperty("game").GetProperty("list");
        Assert.Equal(80, list.GetProperty("vote").GetInt32());
        Assert.True(list.GetProperty("pending").GetBoolean());
        Assert.Equal("Terminado", list.GetProperty("labels")[0].GetProperty("name").GetString());
        var raw = System.IO.File.ReadAllText(File.Path);
        Assert.DoesNotContain(Secret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
    }
}
