using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class RestorePointStoreTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(Agora);
    private readonly ProjectFolder _project;
    private readonly RestorePointStore _store;

    public RestorePointStoreTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _store = new RestorePointStore(_project, _clock);
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Linha(string script, int n, string? traducao, TranslationStatus status = TranslationStatus.Translated, string original = "同じ") =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, null, original, traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
            Notes = $"nota {n}",
        };

    private static TextUnit Pendente(string script, int n) => Linha(script, n, null, TranslationStatus.Untranslated);

    private TextUnit Gravada(string script, int n) => _project.LoadText(script).Single(u => u.Id == $"{script}:{n:D6}");

    private LineEditor Editor() => new(_project, _clock);

    [Fact]
    public void Operacao_sem_mudanca_nao_deixa_ponto()
    {
        _project.SaveText("a", [Linha("a", 1, "Igual")]);

        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Igual"));

        Assert.Null(feito.Point);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Operacao_que_falha_no_meio_ainda_deixa_o_ponto_do_que_gravou()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);

        Assert.Throws<InvalidOperationException>(() => _store.Record<int>(
            "Lote que falha",
            () =>
            {
                Editor().SetTranslation("a", "a:000001", "Mudou");
                throw new InvalidOperationException("caiu");
            }));

        var ponto = Assert.Single(_store.List());
        Assert.Equal(1, ponto.LineCount);
        Assert.Equal(1, _store.Undo(ponto.Id).Restored);
        Assert.Equal("Oi", Gravada("a", 1).Translation);
    }

    [Fact]
    public void Pasta_sem_manifesto_nao_aparece_na_lista()
    {
        Directory.CreateDirectory(Path.Combine(_store.StoreDirectory, "20260101-000000-abcdef"));

        Assert.Empty(_store.List());
    }

    [Fact]
    public void Id_com_separador_de_pasta_e_recusado()
    {
        Assert.Throws<VNForgeException>(() => _store.Undo("..\\fora"));
    }

    [Fact]
    public void Ponto_gravado_antes_do_campo_doubt_existir_desfaz_sem_dar_duvida_a_linha()
    {
        var nova = new TranslationDoubt(DoubtKind.Pun, "Trocadilho no original.");
        _project.SaveText("a", [Pendente("a", 1)]);
        var feito = _store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var ia = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora);
                _project.SaveText("a", [.. _project.LoadText("a").Select(u => u with { Translation = "Um", Status = TranslationStatus.Machine, Origin = ia, Doubt = nova })]);
                return 0;
            },
            include: RestorePointStore.IsAiWrite);
        var antes = Path.Combine(_store.StoreDirectory, feito.Point!.Id, "a.antes.json");
        Assert.DoesNotContain("doubt", File.ReadAllText(antes), StringComparison.Ordinal);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(1, 0, 0), desfeito);
        Assert.Null(Gravada("a", 1).Doubt);
        Assert.Equal(TranslationStatus.Untranslated, Gravada("a", 1).Status);
    }
}
