using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O índice por original e o "Usar em todos": a mesma fala em outras linhas, e só ela.</summary>
public class SameSourceTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(string script, int n, string original, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos() => new Dictionary<string, IReadOnlyList<TextUnit>>
    {
        ["yst00001"] =
        [
            Linha("yst00001", 0, "「うん」\\p"),
            Linha("yst00001", 1, "「ええ」\\p", "Sim.\\p", TranslationStatus.Translated),
            Linha("yst00001", 2, "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
        ],
        ["yst00002"] =
        [
            Linha("yst00002", 0, "「うん」\\p"),
            Linha("yst00002", 1, "「うん」", "Sem pausa."),
            Linha("yst00002", 2, "「うん」\\p", "Hm.\\p", TranslationStatus.Machine),
        ],
    };

    [Fact]
    public void A_propria_linha_e_texto_diferente_nao_entram()
    {
        var linhas = ProjectLines.Create(Textos());

        Assert.DoesNotContain(linhas.LinesOf("yst00001")[0].Unit.Id, linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]).Select(l => l.Unit.Id));
        Assert.Empty(linhas.SameSourceAs(linhas.LinesOf("yst00001")[1]));
        Assert.DoesNotContain(linhas.LinesOf("yst00002")[1].Unit.Id, linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]).Select(l => l.Unit.Id));
    }

    [Fact]
    public void Original_vazio_nao_agrupa()
    {
        var textos = new Dictionary<string, IReadOnlyList<TextUnit>> { ["yst00001"] = [Linha("yst00001", 0, ""), Linha("yst00001", 1, "")] };
        var linhas = ProjectLines.Create(textos);

        Assert.Empty(linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]));
    }

    [Fact]
    public void Usar_em_todos_com_linha_que_nao_existe_avisa_sem_gravar_o_script()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("yst00001", Textos()["yst00001"]);
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));

        var resultado = editor.SetTranslationFor([("yst00001", "yst00001:000000"), ("yst00001", "yst00001:000099")], "Tá.\\p");

        Assert.IsType<KeyNotFoundException>(resultado.Failure);
        Assert.Empty(resultado.Changes);
        Assert.Null(projeto.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public void Na_nova_tentativa_as_linhas_que_ja_foram_gravadas_voltam_como_inalteradas()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        foreach (var (chave, unidades) in Textos())
        {
            projeto.SaveText(chave, unidades);
        }

        var alvos = new[] { ("yst00001", "yst00001:000002"), ("yst00002", "yst00002:000000"), ("yst00002", "yst00002:000002") };
        var falha = true;
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora), (chave, unidades) =>
        {
            if (falha && chave == "yst00002")
            {
                throw new UnauthorizedAccessException("sem permissão");
            }

            projeto.SaveText(chave, unidades);
        });
        editor.SetTranslationFor(alvos, "Tá.\\p");

        falha = false;
        var segunda = editor.SetTranslationFor(alvos, "Tá.\\p");

        Assert.Null(segunda.Failure);
        Assert.Equal(["yst00002:000000", "yst00002:000002"], segunda.Changes.Select(c => c.Saved.Id));
        Assert.Equal("yst00001:000002", Assert.Single(segunda.Unchanged).Unit.Id);
        Assert.Equal(3, segunda.Applied);
    }

    [Fact]
    public void Cancelar_no_meio_mantem_os_scripts_ja_gravados_e_deixa_os_outros_como_estavam()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        foreach (var (chave, unidades) in Textos())
        {
            projeto.SaveText(chave, unidades);
        }

        using var cancelamento = new CancellationTokenSource();
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora), (chave, unidades) =>
        {
            projeto.SaveText(chave, unidades);
            cancelamento.Cancel();
        });
        var alvos = new[] { ("yst00001", "yst00001:000002"), ("yst00002", "yst00002:000000"), ("yst00002", "yst00002:000002") };

        var resultado = editor.SetTranslationFor(alvos, "Tá.\\p", null, cancelamento.Token);

        Assert.True(resultado.Canceled);
        Assert.Null(resultado.Failure);
        Assert.Equal("yst00001:000002", Assert.Single(resultado.Changes).Saved.Id);
        Assert.Equal("Tá.\\p", projeto.LoadText("yst00001")[2].Translation);
        Assert.Null(projeto.LoadText("yst00002")[0].Translation);
    }

    [Fact]
    public async Task Cancelar_enquanto_espera_a_trava_nao_grava_o_script()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        foreach (var (chave, unidades) in Textos())
        {
            projeto.SaveText(chave, unidades);
        }

        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));
        using var cancelamento = new CancellationTokenSource();
        var alvos = new[] { ("yst00001", "yst00001:000002"), ("yst00002", "yst00002:000000") };
        Thread? threadDoLote = null;
        using var noMeio = new ManualResetEventSlim();
        var seguindo = false;
        var andamento = new SincronoProgress(p =>
        {
            if (p.Done != 1)
            {
                return;
            }

            threadDoLote = Thread.CurrentThread;
            noMeio.Set();
            while (!Volatile.Read(ref seguindo))
            {
                Thread.SpinWait(100);
            }
        });
        var lote = Task.Run(() => editor.SetTranslationFor(alvos, "Tá.\\p", andamento, cancelamento.Token), TestContext.Current.CancellationToken);
        Assert.True(noMeio.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var segurando = Task.Run(() =>
        {
            lock (ProjectTextLock.For(projeto))
            {
                Volatile.Write(ref seguindo, true);
                Assert.True(SpinWait.SpinUntil(() => threadDoLote!.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TimeSpan.FromSeconds(30)));
                cancelamento.Cancel();
            }
        }, TestContext.Current.CancellationToken);
        await segurando;
        var resultado = await lote;

        Assert.True(resultado.Canceled);
        Assert.Equal("yst00001:000002", Assert.Single(resultado.Changes).Saved.Id);
        Assert.Null(projeto.LoadText("yst00002")[0].Translation);
    }

    private sealed class SincronoProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => report(value);
    }

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));
}
