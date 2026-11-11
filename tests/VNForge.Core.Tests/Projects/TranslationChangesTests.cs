using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class TranslationChangesTests
{
    private static readonly DateTimeOffset TestAt = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Line(string id, string? translation, string? chosenSpeaker = null) =>
        new(id, TextUnitKind.Dialogue, "犬井", $"原文 {id}", translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated)
        {
            ChosenSpeaker = chosenSpeaker,
        };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);

    private static GameLastTest Tested(Dictionary<string, IReadOnlyList<TextUnit>> texts, string? onlyScript = null, GameTestKind kind = GameTestKind.Translation) =>
        new(TestAt, kind, [], TranslationChanges.Snapshot(texts, onlyScript));

    [Fact]
    public void Teste_sem_retrato_de_arquivo_antigo_deixa_a_lista_vazia_e_diz_o_motivo()
    {
        var old = new GameLastTest(TestAt, GameTestKind.Translation, []);

        var changes = TranslationChanges.Since(old, Texts(("a001", [Line("1", "Oi")])));

        Assert.Equal(ChangesSinceTestState.NoSnapshot, changes.State);
        Assert.False(changes.AccentOnly);
        Assert.Empty(changes.Scenes);
        Assert.Equal(TestAt, changes.TestAt);
    }

    [Fact]
    public void Falante_escolhido_depois_do_teste_conta_como_mudanca()
    {
        var test = Tested(Texts(("a001", [Line("1", "Oi")])));

        var changes = TranslationChanges.Since(test, Texts(("a001", [Line("1", "Oi", chosenSpeaker: "Ana")])));

        Assert.Equal(1, Assert.Single(changes.Scenes).Lines);
    }

    [Fact]
    public void Teste_que_nao_leu_os_textos_diz_isso_em_vez_de_versao_anterior()
    {
        var failed = new GameLastTest(TestAt, GameTestKind.Translation, [], null, SnapshotFailed: true);

        var changes = TranslationChanges.Since(failed, Texts(("a001", [Line("1", "Oi")])));

        Assert.Equal(ChangesSinceTestState.SnapshotFailed, changes.State);
        Assert.Empty(changes.Scenes);
    }
}
