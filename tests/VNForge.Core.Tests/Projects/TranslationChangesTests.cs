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
    public void Sem_teste_nao_ha_lista()
    {
        var changes = TranslationChanges.Since(null, Texts(("a001", [Line("1", "Oi")])));

        Assert.Equal(ChangesSinceTestState.NoTest, changes.State);
        Assert.Empty(changes.Scenes);
    }

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
    public void Teste_de_acentos_nao_tem_retrato_mesmo_que_o_arquivo_traga_um()
    {
        var texts = Texts(("a001", [Line("1", "Oi")]));

        var changes = TranslationChanges.Since(Tested(texts, kind: GameTestKind.AccentTest), texts);

        Assert.Equal(ChangesSinceTestState.NoSnapshot, changes.State);
        Assert.True(changes.AccentOnly);
    }

    [Fact]
    public void Nada_mudou_desde_o_teste()
    {
        var texts = Texts(("a001", [Line("1", "Oi")]), ("a002", [Line("1", "Tchau")]));

        var changes = TranslationChanges.Since(Tested(texts), texts);

        Assert.Equal(ChangesSinceTestState.Unchanged, changes.State);
        Assert.Empty(changes.Scenes);
    }

    [Fact]
    public void Lista_as_cenas_mudadas_na_ordem_da_chave_com_a_posicao_e_a_contagem_de_falas()
    {
        var before = Texts(
            ("a003", [Line("1", "Um"), Line("2", "Dois")]),
            ("a001", [Line("1", "Oi"), Line("2", "Olá")]),
            ("a002", [Line("1", "Tchau")]));
        var test = Tested(before);

        var now = Texts(
            ("a003", [Line("1", "Um"), Line("2", "Dois!")]),
            ("a001", [Line("1", "Oi!"), Line("2", "Olá!")]),
            ("a002", [Line("1", "Tchau")]));
        var changes = TranslationChanges.Since(test, now);

        Assert.Equal(ChangesSinceTestState.Changed, changes.State);
        Assert.Equal(["a001", "a003"], changes.Scenes.Select(s => s.Key));
        Assert.Equal([1, 3], changes.Scenes.Select(s => s.Number));
        Assert.Equal([2, 1], changes.Scenes.Select(s => s.Lines));
        Assert.Equal(["1", "2"], changes.Scenes[0].UnitIds);
        Assert.Equal(["2"], changes.Scenes[1].UnitIds);
        Assert.Equal(3, changes.TotalLines);
        Assert.Equal(TestAt, changes.TestAt);
    }

    [Fact]
    public void Linha_traduzida_ou_apagada_depois_do_teste_conta_como_mudada()
    {
        var test = Tested(Texts(("a001", [Line("1", "Oi"), Line("2", null)])));

        var changes = TranslationChanges.Since(test, Texts(("a001", [Line("1", null), Line("2", "Novo")])));

        var scene = Assert.Single(changes.Scenes);
        Assert.Equal(2, scene.Lines);
        Assert.Equal(["1", "2"], scene.UnitIds);
    }

    [Fact]
    public void Falante_escolhido_depois_do_teste_conta_como_mudanca()
    {
        var test = Tested(Texts(("a001", [Line("1", "Oi")])));

        var changes = TranslationChanges.Since(test, Texts(("a001", [Line("1", "Oi", chosenSpeaker: "Ana")])));

        Assert.Equal(1, Assert.Single(changes.Scenes).Lines);
    }

    [Fact]
    public void Linha_que_saiu_do_projeto_conta_mas_nao_vira_id_navegavel()
    {
        var test = Tested(Texts(("a001", [Line("1", "Oi"), Line("2", "Olá")])));

        var changes = TranslationChanges.Since(test, Texts(("a001", [Line("1", "Oi")])));

        var scene = Assert.Single(changes.Scenes);
        Assert.Equal(1, scene.Lines);
        Assert.Empty(scene.UnitIds);
    }

    [Fact]
    public void Mudar_so_notas_ou_status_nao_conta()
    {
        var texts = Texts(("a001", [Line("1", "Oi")]));
        var test = Tested(texts);

        var edited = Texts(("a001", [Line("1", "Oi") with { Status = TranslationStatus.Approved, Notes = "ok" }]));

        Assert.Equal(ChangesSinceTestState.Unchanged, TranslationChanges.Since(test, edited).State);
    }

    [Fact]
    public void Teste_de_uma_cena_so_lista_as_outras_traduzidas_como_mudadas()
    {
        var texts = Texts(("a001", [Line("1", "Oi")]), ("a002", [Line("1", "Tchau")]));
        var test = Tested(texts, onlyScript: "a002");

        var changes = TranslationChanges.Since(test, texts);

        Assert.Equal(["a001"], changes.Scenes.Select(s => s.Key));
    }

    [Fact]
    public void O_retrato_guarda_so_hashes_e_nunca_o_texto()
    {
        var snapshot = TranslationChanges.Snapshot(Texts(("a001", [Line("1", "Frase secreta da tradução")])));

        var hash = snapshot.Scripts["a001"]["1"];
        Assert.Equal(12, hash.Length);
        Assert.DoesNotContain("secreta", hash);
    }

    [Fact]
    public void Linha_sem_traducao_ou_igual_ao_original_fica_fora_do_retrato()
    {
        var same = new TextUnit("2", TextUnitKind.Dialogue, null, "abc", "abc", TranslationStatus.Keep);

        var snapshot = TranslationChanges.Snapshot(Texts(("a001", [Line("1", null), same])));

        Assert.Empty(snapshot.Scripts);
    }
}
