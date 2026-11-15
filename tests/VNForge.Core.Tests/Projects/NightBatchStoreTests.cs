using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class NightBatchStoreTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 8, 23, 41, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;
    private readonly NightBatchStore _store;

    public NightBatchStoreTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _store = new NightBatchStore(_project);
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Linha(int n, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", traducao, status);

    private static NightBatchState Lote(string id, DateTimeOffset at, params TextUnit[] lines) =>
        new(id, "O capítulo", "claude-opus-5-5", "Careful", "Medium", at, 13.46m, ["yst00001"], [new NightBatchRequest("r00001", [.. lines.Select(NightBatchLine.Of)])]);

    [Fact]
    public void Arquivo_ilegivel_fica_de_fora_sem_derrubar_a_lista()
    {
        _store.Save(Lote("msgbatch_a", Agora, Linha(0)));
        File.WriteAllText(Path.Combine(_store.StoreDirectory, "quebrado.json"), "{ isto não é json");

        Assert.Equal(["msgbatch_a"], _store.List().Select(s => s.Id));
        Assert.Null(_store.Find("quebrado"));
    }
}
