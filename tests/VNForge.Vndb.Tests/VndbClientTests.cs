using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Vndb;

namespace VNForge.Vndb.Tests;

public class VndbClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <paramref name="time"/> é o relógio da fila. O relógio do prazo do cliente (<paramref name="clientTime"/>) é o do sistema, de propósito: os testes que andam
    /// <paramref name="time"/> aos saltos não podem estourar o prazo de um pedido que esteja no ar.
    /// </summary>
    private static VndbClient Client(
        StubHandler handler,
        FakeTimeProvider? time = null,
        VndbCache? cache = null,
        int maxRequests = VndbRequestQueue.DefaultMaxRequests,
        VndbRequestQueue? queue = null,
        TimeProvider? clientTime = null,
        TimeSpan? timeout = null)
    {
        time ??= new FakeTimeProvider();
        return new VndbClient(new HttpClient(handler), queue ?? new VndbRequestQueue(time, maxRequests), cache, time: clientTime, timeout: timeout);
    }

    private static JsonNode Body(SentRequest request) => JsonNode.Parse(request.Body)!;

    /// <summary>
    /// Anda o relógio de teste até a tarefa terminar. Anda aos poucos porque o cliente só para a fila depois de ler o 429; adiantar o relógio antes disso
    /// não solta nada, e o próximo passo solta.
    /// </summary>
    private static async Task AdvanceUntilDone(Task task, FakeTimeProvider time, TimeSpan step)
    {
        for (var i = 0; i < 1000 && !task.IsCompleted; i++)
        {
            time.Advance(step);
            await Task.Delay(1);
        }

        Assert.True(task.IsCompleted, "A tarefa não terminou mesmo andando o relógio.");
    }

    [Fact]
    public async Task Busca_de_VN_por_titulo_manda_o_filtro_de_busca_e_converte_a_resposta()
    {
        var handler = new StubHandler(VndbSamples.VisualNovels);

        var found = await Client(handler).SearchVisualNovelsAsync("  Moon Flower ", 5, ct: Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.vndb.org/kana/vn", sent.Uri.ToString());
        Assert.Equal("""["search","=","Moon Flower"]""", Body(sent)["filters"]!.ToJsonString());
        Assert.Equal("searchrank", (string?)Body(sent)["sort"]);
        Assert.Equal(5, (int?)Body(sent)["results"]);
        Assert.Contains("tags.spoiler", (string?)Body(sent)["fields"]);
        Assert.Contains("image.url", (string?)Body(sent)["fields"]);
        Assert.Contains("length_minutes", (string?)Body(sent)["fields"]);
        Assert.Contains("description", (string?)Body(sent)["fields"]);
        Assert.Null(sent.Authorization);
        Assert.StartsWith("VNForge", sent.UserAgent);

        var vn = Assert.Single(found);
        Assert.Equal("v900001", vn.Id);
        Assert.Equal("Tsuki no Hana", vn.Title);
        Assert.Equal("月の花", vn.AltTitle);
        Assert.Equal("ja", vn.OriginalLanguage);
        Assert.Equal(0, vn.DevStatus);
        Assert.Equal("2019-05-31", vn.Released);
        Assert.Equal(["en", "ja"], vn.Languages);
        Assert.Equal(["Tsukihana"], vn.Aliases);
        Assert.Equal(3, vn.Length);
        Assert.Equal(1500, vn.LengthMinutes);
        Assert.Equal(42, vn.LengthVotes);
        Assert.Equal("Uma [b]história[/b] de exemplo.", vn.Description);
        Assert.Equal(new VndbTitle("ja", "月の花", "Tsuki no Hana", true, true), vn.Titles[0]);
        Assert.Equal(new VndbImage("https://t.vndb.org/cv/00/1.jpg", "https://t.vndb.org/cv.t/00/1.jpg", 800, 1200, 0.5, 0), vn.Image);
        Assert.Equal(new VndbTag("g2", "Twist", "cont", 1.2, 2, true), vn.Tags[1]);
    }

    [Fact]
    public async Task Busca_sem_resultado_devolve_lista_vazia()
    {
        var found = await Client(new StubHandler(VndbSamples.NoResults)).SearchVisualNovelsAsync("nada", ct: Ct);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Busca_de_VN_limita_o_pedido_a_100_resultados()
    {
        var handler = new StubHandler(VndbSamples.NoResults);

        await Client(handler).SearchVisualNovelsAsync("x", 5000, ct: Ct);

        Assert.Equal(100, (int?)Body(handler.Requests[0])["results"]);
    }

    [Fact]
    public async Task GetVisualNovel_pede_o_id_e_devolve_nulo_quando_nao_existe()
    {
        var handler = new StubHandler((_, n) => StubHandler.Json(n == 1 ? VndbSamples.VisualNovels : VndbSamples.NoResults));
        var client = Client(handler);

        var found = await client.GetVisualNovelAsync("v900001", ct: Ct);
        var missing = await client.GetVisualNovelAsync("v1", ct: Ct);

        Assert.Equal("v900001", found?.Id);
        Assert.Null(missing);
        Assert.Equal("""["id","=","v900001"]""", Body(handler.Requests[0])["filters"]!.ToJsonString());
    }

    [Fact]
    public async Task Personagens_por_titulo_usam_o_filtro_vn_com_busca_e_trazem_papel_spoiler_e_tracos()
    {
        var handler = new StubHandler(VndbSamples.Characters);

        var result = await Client(handler).SearchCharactersByTitleAsync("Moon Flower", ct: Ct);
        var found = result.Items;
        Assert.False(result.Truncated);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api.vndb.org/kana/character", sent.Uri.ToString());
        Assert.Equal("""["vn","=",["search","=","Moon Flower"]]""", Body(sent)["filters"]!.ToJsonString());
        var fields = (string?)Body(sent)["fields"];
        Assert.Contains("vns.role", fields);
        Assert.Contains("vns.spoiler", fields);
        Assert.Contains("vns.release.id", fields);
        Assert.Contains("traits.group_name", fields);
        Assert.Contains("image.url", fields);
        Assert.Contains("gender", fields);

        var character = Assert.Single(found);
        Assert.Equal("c900001", character.Id);
        Assert.Equal("Hoshino Ami", character.Name);
        Assert.Equal("星野 亜美", character.Original);
        Assert.Equal(new VndbGender("f", "m"), character.Gender);
        Assert.Equal(new VndbImage("https://t.vndb.org/ch/00/1.jpg", null, 256, 300, 0, 0), character.Image);
        Assert.Equal(
            [new VndbCharacterAppearance("v900001", "main", 0, null), new VndbCharacterAppearance("v900002", "side", 1, "r900010")],
            character.Appearances);
        Assert.Equal(new VndbTrait("i2164", "Priest", "i40", "Role", 2, false), character.Traits[1]);
    }

    [Fact]
    public async Task Personagens_de_um_jogo_seguem_as_paginas_ate_acabar()
    {
        var handler = new StubHandler((_, n) => StubHandler.Json(n == 1 ? VndbSamples.CharactersFirstPage : VndbSamples.CharactersSecondPage));

        var found = await Client(handler).GetCharactersAsync("v900001", ct: Ct);

        Assert.False(found.Truncated);
        Assert.Equal(["c900001", "c900002"], found.Items.Select(c => c.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("""["vn","=",["id","=","v900001"]]""", Body(handler.Requests[0])["filters"]!.ToJsonString());
        Assert.Equal([1, 2], handler.Requests.Select(r => (int)Body(r)["page"]!));
    }

    [Fact]
    public async Task Busca_de_personagens_que_passa_do_teto_de_paginas_avisa_que_cortou()
    {
        var handler = new StubHandler(VndbSamples.CharactersFirstPage);

        var result = await Client(handler).SearchCharactersByTitleAsync("Moon Flower", ct: Ct);

        Assert.True(result.Truncated);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task Edicoes_do_jogo_trazem_idiomas_engine_e_se_e_patch()
    {
        var handler = new StubHandler(VndbSamples.Releases);

        var found = (await Client(handler).GetReleasesAsync("v900001", ct: Ct)).Items;

        Assert.Equal("https://api.vndb.org/kana/release", handler.Requests[0].Uri.ToString());
        var release = Assert.Single(found);
        Assert.Equal("r900010", release.Id);
        Assert.Equal("YU-RIS", release.Engine);
        Assert.Equal(15, release.MinAge);
        Assert.Equal(4, release.Voiced);
        Assert.False(release.Patch);
        Assert.True(release.Official);
        Assert.Equal(new VndbReleaseLanguage("pt-br", null, null, true, false), release.Languages[1]);
        Assert.Equal(new VndbReleaseVisualNovel("v900001", "complete"), Assert.Single(release.VisualNovels));
    }

    [Theory]
    [InlineData("17")]
    [InlineData("c17")]
    [InlineData("v")]
    [InlineData("v1x")]
    [InlineData("")]
    public async Task Id_de_jogo_fora_do_formato_e_recusado_sem_pedido(string id)
    {
        var handler = new StubHandler(VndbSamples.NoResults);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).GetCharactersAsync(id, ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Titulo_vazio_e_recusado_sem_pedido()
    {
        var handler = new StubHandler(VndbSamples.NoResults);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Client(handler).SearchVisualNovelsAsync("  ", ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_mesma_busca_sai_do_cache_e_nao_gasta_pedido()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var handler = new StubHandler(VndbSamples.VisualNovels);
        var client = Client(handler, time, new VndbCache(dir.Path, time));

        var first = await client.SearchVisualNovelsAsync("Moon Flower", ct: Ct);
        var second = await client.SearchVisualNovelsAsync("Moon Flower", ct: Ct);

        Assert.Single(handler.Requests);
        Assert.Equal(first, second, new VnComparer());
    }

    [Fact]
    public async Task O_cache_sobrevive_a_outro_cliente_e_expira_depois_de_7_dias()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var handler = new StubHandler(VndbSamples.VisualNovels);
        await Client(handler, time, new VndbCache(dir.Path, time)).SearchVisualNovelsAsync("Moon Flower", ct: Ct);

        await Client(handler, time, new VndbCache(dir.Path, time)).SearchVisualNovelsAsync("Moon Flower", ct: Ct);
        Assert.Single(handler.Requests);

        time.Advance(TimeSpan.FromDays(7));
        await Client(handler, time, new VndbCache(dir.Path, time)).SearchVisualNovelsAsync("Moon Flower", ct: Ct);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Resposta_ilegivel_nao_vai_para_o_cache()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var handler = new StubHandler((_, n) => StubHandler.Json(n == 1 ? "isto nao e json" : VndbSamples.VisualNovels));
        var client = Client(handler, time, new VndbCache(dir.Path, time));

        await Assert.ThrowsAsync<VndbException>(() => client.SearchVisualNovelsAsync("Moon Flower", ct: Ct));
        var found = await client.SearchVisualNovelsAsync("Moon Flower", ct: Ct);

        Assert.Single(found);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Pedidos_alem_do_limite_esperam_a_janela_e_nunca_passam_de_N_por_janela()
    {
        var time = new FakeTimeProvider();
        var handler = new StubHandler(VndbSamples.NoResults);
        var client = Client(handler, time, maxRequests: 3);

        for (var i = 0; i < 3; i++)
        {
            await client.SearchVisualNovelsAsync("titulo " + i, ct: Ct);
        }

        var fourth = client.SearchVisualNovelsAsync("titulo 3", ct: Ct);
        Assert.False(fourth.IsCompleted);
        Assert.Equal(3, handler.Requests.Count);

        time.Advance(VndbRequestQueue.DefaultWindow);
        await fourth.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Resposta_429_para_a_fila_pelo_Retry_After_e_o_pedido_e_refeito()
    {
        var time = new FakeTimeProvider();
        var secondSentAt = DateTimeOffset.MinValue;
        var handler = new StubHandler((_, n) =>
        {
            if (n == 1)
            {
                var throttled = StubHandler.Text(HttpStatusCode.TooManyRequests, "Throttled");
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return throttled;
            }

            secondSentAt = time.GetUtcNow();
            return StubHandler.Json(VndbSamples.NoResults);
        });
        var started = time.GetUtcNow();
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, time, TimeSpan.FromSeconds(30));
        await pending;

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(secondSentAt - started >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Sem_Retry_After_a_espera_cresce_60_120_180_segundos_e_na_quarta_vez_o_pedido_passa()
    {
        var time = new FakeTimeProvider();
        var sentAt = new List<DateTimeOffset>();
        var handler = new StubHandler((_, n) =>
        {
            lock (sentAt)
            {
                sentAt.Add(time.GetUtcNow());
            }

            return n <= 3 ? StubHandler.Text(HttpStatusCode.TooManyRequests, "Throttled") : StubHandler.Json(VndbSamples.NoResults);
        });
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, time, TimeSpan.FromSeconds(10));
        await pending;

        Assert.Equal(4, handler.Requests.Count);
        Assert.True(sentAt[1] - sentAt[0] >= TimeSpan.FromSeconds(60));
        Assert.True(sentAt[2] - sentAt[1] >= TimeSpan.FromSeconds(120));
        Assert.True(sentAt[3] - sentAt[2] >= TimeSpan.FromSeconds(180));
    }

    [Fact]
    public async Task Quatro_respostas_429_seguidas_viram_erro_em_portugues()
    {
        var time = new FakeTimeProvider();
        var handler = new StubHandler((_, _) => StubHandler.Text(HttpStatusCode.TooManyRequests, "Throttled"));
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, time, TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<VndbException>(() => pending);

        Assert.Equal(429, error.StatusCode);
        Assert.Contains("limitou", error.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task O_Retry_After_vence_a_espera_padrao_e_nunca_passa_da_janela_da_fila()
    {
        var time = new FakeTimeProvider();
        var sentAt = new List<DateTimeOffset>();
        var handler = new StubHandler((_, n) =>
        {
            lock (sentAt)
            {
                sentAt.Add(time.GetUtcNow());
            }

            if (n > 1)
            {
                return StubHandler.Json(VndbSamples.NoResults);
            }

            var throttled = StubHandler.Text(HttpStatusCode.TooManyRequests, "Throttled");
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return throttled;
        });
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        // Uma hora pedida, mas a fila só segura até a janela de 5 minutos: passar disso só adiaria o erro.
        await AdvanceUntilDone(pending, time, VndbRequestQueue.DefaultWindow);
        await pending;

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(sentAt[1] - sentAt[0] >= VndbRequestQueue.DefaultWindow);
        Assert.True(sentAt[1] - sentAt[0] < TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Um_503_com_a_pagina_do_nginx_e_limite_de_uso_e_o_pedido_e_refeito()
    {
        var time = new FakeTimeProvider();
        var handler = new StubHandler((_, n) => n == 1
            ? StubHandler.Text(HttpStatusCode.ServiceUnavailable, "<html><head><title>503 Service Temporarily Unavailable</title></head></html>")
            : StubHandler.Json(VndbSamples.NoResults));
        var pending = Client(handler, time).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, time, TimeSpan.FromSeconds(10));
        await pending;

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Um_503_sem_sinal_de_limite_e_servidor_fora_do_ar_e_nao_e_refeito()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(HttpStatusCode.ServiceUnavailable, "down for maintenance"));

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).SearchVisualNovelsAsync("x", ct: Ct));

        Assert.Equal(503, error.StatusCode);
        Assert.Contains("fora do ar", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Queda_no_meio_do_corpo_vira_VndbException_em_portugues()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StubHandler.BrokenContent() });

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).SearchVisualNovelsAsync("x", ct: Ct));

        Assert.Contains("caiu no meio da resposta", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task Corpo_que_trava_para_pelo_prazo_do_cliente()
    {
        var clientTime = new FakeTimeProvider();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StubHandler.HangingContent() });
        var pending = Client(handler, clientTime: clientTime, timeout: TimeSpan.FromSeconds(30)).SearchVisualNovelsAsync("x", ct: Ct);

        await AdvanceUntilDone(pending, clientTime, TimeSpan.FromSeconds(30));
        var error = await Assert.ThrowsAsync<VndbException>(() => pending);

        Assert.Contains("demorou demais", error.Message);
    }

    [Fact]
    public async Task Dois_clientes_com_a_mesma_fila_juntos_nao_passam_do_limite()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time, maxRequests: 3);
        var handlerA = new StubHandler(VndbSamples.NoResults);
        var handlerB = new StubHandler(VndbSamples.NoResults);
        var a = Client(handlerA, queue: queue);
        var b = Client(handlerB, queue: queue);

        await a.SearchVisualNovelsAsync("a1", ct: Ct);
        await b.SearchVisualNovelsAsync("b1", ct: Ct);
        await a.SearchVisualNovelsAsync("a2", ct: Ct);
        var fourth = b.SearchVisualNovelsAsync("b2", ct: Ct);

        Assert.False(fourth.IsCompleted);
        Assert.Equal(3, handlerA.Requests.Count + handlerB.Requests.Count);

        time.Advance(VndbRequestQueue.DefaultWindow);
        await fourth.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(4, handlerA.Requests.Count + handlerB.Requests.Count);
    }

    [Fact]
    public void Create_divide_a_fila_do_processo_entre_os_clientes()
    {
        var first = VndbClient.Create();
        var second = VndbClient.Create();
        var own = new VndbRequestQueue(new FakeTimeProvider());

        Assert.Same(VndbClient.SharedRequestQueue, first.Queue);
        Assert.Same(first.Queue, second.Queue);
        Assert.Same(own, VndbClient.Create(queue: own).Queue);
        Assert.NotSame(first.Queue, VndbClient.Create(time: new FakeTimeProvider()).Queue);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Invalid filter", "recusou o pedido: Invalid filter")]
    [InlineData(HttpStatusCode.Unauthorized, "", "não aceitou o token")]
    [InlineData(HttpStatusCode.InternalServerError, "", "fora do ar")]
    [InlineData(HttpStatusCode.BadGateway, "", "fora do ar")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", "fora do ar")]
    [InlineData(HttpStatusCode.NotFound, "", "HTTP 404")]
    public async Task Erro_de_http_vira_VndbException_com_mensagem_em_portugues(HttpStatusCode status, string body, string expected)
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(status, body));

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).SearchVisualNovelsAsync("x", ct: Ct));

        Assert.Contains(expected, error.Message);
        Assert.Equal((int)status, error.StatusCode);
    }

    [Fact]
    public async Task Falha_de_rede_vira_VndbException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("sem rede"));

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).SearchVisualNovelsAsync("x", ct: Ct));

        Assert.Contains("Não foi possível falar com o VNDB", error.Message);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task Cancelar_nao_vira_VndbException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(new StubHandler(VndbSamples.NoResults)).SearchVisualNovelsAsync("x", ct: cts.Token));
    }

    [Fact]
    public async Task AuthInfo_manda_o_token_no_cabecalho_e_devolve_conta_e_permissoes()
    {
        var handler = new StubHandler(VndbSamples.AuthInfo);

        var info = await Client(handler).GetAuthInfoAsync(" abcd-efgh ", ct: Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://api.vndb.org/kana/authinfo", sent.Uri.ToString());
        Assert.Equal("Token abcd-efgh", sent.Authorization);
        Assert.Equal(new VndbAuthInfo("u900", "alessandro", ["listread", "listwrite"]), info, new AuthInfoComparer());
    }

    [Fact]
    public async Task Lista_do_usuario_segue_as_paginas_e_converte_datas_e_rotulos()
    {
        var handler = new StubHandler((_, n) => StubHandler.Json(n == 1 ? VndbSamples.ListFirstPage : VndbSamples.ListSecondPage));

        var list = (await Client(handler).GetListAsync("u900", "tok-1", ct: Ct)).Items;

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("https://api.vndb.org/kana/ulist", r.Uri.ToString());
            Assert.Equal("Token tok-1", r.Authorization);
            Assert.Equal("u900", (string?)Body(r)["user"]);
        });

        var first = list[0];
        Assert.Equal("v900001", first.VisualNovelId);
        Assert.Equal("Moon Flower", first.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), first.Added);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000200), first.Voted);
        Assert.Equal(80, first.Vote);
        Assert.Equal(new DateOnly(2024, 1, 2), first.Started);
        Assert.Null(first.Finished);
        Assert.Equal("bom", first.Notes);
        Assert.Equal(["Finished", "Voted"], first.Labels.Select(l => l.Label));
        Assert.Null(list[1].Vote);
        Assert.Null(list[1].Voted);
    }

    [Fact]
    public async Task Lista_sem_token_nao_manda_cabecalho_de_autorizacao()
    {
        var handler = new StubHandler(VndbSamples.ListSecondPage);

        await Client(handler).GetListAsync("u2", ct: Ct);

        Assert.Null(handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task Lista_e_token_nunca_passam_pelo_cache()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var handler = new StubHandler((req, _) => StubHandler.Json(req.Uri.AbsolutePath.EndsWith("authinfo", StringComparison.Ordinal) ? VndbSamples.AuthInfo : VndbSamples.ListSecondPage));
        var client = Client(handler, time, new VndbCache(dir.Path, time));

        await client.GetAuthInfoAsync("segredo-1", ct: Ct);
        await client.GetListAsync("u900", "segredo-1", ct: Ct);
        await client.GetListAsync("u900", "segredo-1", ct: Ct);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Empty(Directory.Exists(dir.Path) ? Directory.GetFiles(dir.Path) : []);
    }

    [Fact]
    public async Task Atualizar_entrada_manda_PATCH_so_com_o_que_mudou()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        var update = new VndbListUpdate
        {
            Vote = 60,
            Notes = VndbChange<string>.Clear,
            Finished = new DateOnly(2025, 3, 9),
            LabelsSet = [2],
            LabelsUnset = [1],
        };

        await Client(handler).UpdateEntryAsync("tok-1", "v17", update, ct: Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("PATCH", sent.Method.Method);
        Assert.Equal("https://api.vndb.org/kana/ulist/v17", sent.Uri.ToString());
        Assert.Equal("Token tok-1", sent.Authorization);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"vote":60,"notes":null,"finished":"2025-03-09","labels_set":[2],"labels_unset":[1]}"""),
            Body(sent)));
    }

    [Fact]
    public async Task Atualizar_so_a_nota_nao_manda_voto_nem_rotulos()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Client(handler).UpdateEntryAsync("tok-1", "v17", new VndbListUpdate { Notes = "ótimo" }, ct: Ct);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"notes":"ótimo"}"""), Body(handler.Requests[0])));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(101)]
    public async Task Voto_fora_de_10_a_100_e_recusado_sem_pedido(int vote)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(handler).UpdateEntryAsync("tok-1", "v17", new VndbListUpdate { Vote = vote }, ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(1601)]
    public async Task Rotulos_fora_de_1_a_1600_ou_o_7_nao_podem_ser_gravados_nem_tirados(int label)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateEntryAsync("tok-1", "v17", new VndbListUpdate { LabelsSet = [label] }, ct: Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateEntryAsync("tok-1", "v17", new VndbListUpdate { LabelsUnset = [label] }, ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Token_com_espaco_no_meio_e_recusado_sem_pedido()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Assert.ThrowsAsync<VndbException>(() => Client(handler).UpdateEntryAsync("abc def", "v17", new VndbListUpdate(), ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Token_invalido_na_atualizacao_vira_erro_401_em_portugues()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(HttpStatusCode.Unauthorized, "Invalid token"));

        var error = await Assert.ThrowsAsync<VndbException>(() => Client(handler).UpdateEntryAsync("tok-1", "v17", new VndbListUpdate { Vote = 70 }, ct: Ct));

        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("tok-1", error.Message);
    }

    private sealed class VnComparer : IEqualityComparer<IReadOnlyList<VndbVisualNovel>>
    {
        public bool Equals(IReadOnlyList<VndbVisualNovel>? x, IReadOnlyList<VndbVisualNovel>? y) =>
            x is not null && y is not null && x.Count == y.Count && x.Zip(y).All(p => p.First.Id == p.Second.Id && p.First.Tags.Count == p.Second.Tags.Count && p.First.Description == p.Second.Description);

        public int GetHashCode(IReadOnlyList<VndbVisualNovel> obj) => obj.Count;
    }

    private sealed class AuthInfoComparer : IEqualityComparer<VndbAuthInfo>
    {
        public bool Equals(VndbAuthInfo? x, VndbAuthInfo? y) =>
            x is not null && y is not null && x.UserId == y.UserId && x.Username == y.Username && x.Permissions.SequenceEqual(y.Permissions);

        public int GetHashCode(VndbAuthInfo obj) => obj.UserId.GetHashCode(StringComparison.Ordinal);
    }
}
