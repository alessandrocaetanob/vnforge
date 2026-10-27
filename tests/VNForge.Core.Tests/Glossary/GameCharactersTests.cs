using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

/// <summary>A lista de personagens do jogo vem das unidades <c>name</c> de <c>ES.CHAR.NAME</c> (#426).</summary>
public class GameCharactersTests
{
    private static TextUnit Unit(int n, TextUnitKind kind, string source, string? translation = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"yst00073:{n:D6}", kind, null, source, translation, status);

    [Fact]
    public void Names_lista_so_as_unidades_name_na_ordem_do_jogo_e_sem_repetir()
    {
        List<TextUnit> units =
        [
            Unit(0, TextUnitKind.Narration, "真弓"),
            Unit(1, TextUnitKind.Name, "真弓"),
            Unit(2, TextUnitKind.Name, "犬井"),
            Unit(3, TextUnitKind.Dialogue, "犬井"),
            Unit(4, TextUnitKind.Name, "真弓"),
            Unit(5, TextUnitKind.Name, "？？？"),
        ];

        var names = GameCharacters.Names(units);

        Assert.Equal(["真弓", "犬井", "？？？"], names.Select(n => n.Source));
        Assert.All(names, n => Assert.Equal(string.Empty, n.Target));
    }

    [Fact]
    public void Names_ignora_nome_em_branco_e_apara_os_espacos_do_original()
    {
        var names = GameCharacters.Names([Unit(0, TextUnitKind.Name, "  "), Unit(1, TextUnitKind.Name, "　学生Ａ ")]);

        Assert.Equal(["学生Ａ"], names.Select(n => n.Source));
    }

    [Fact]
    public void Names_traz_a_traducao_da_primeira_unidade_traduzida_do_mesmo_nome()
    {
        List<TextUnit> units =
        [
            Unit(0, TextUnitKind.Name, "真弓"),
            Unit(1, TextUnitKind.Name, "真弓", "Mayumi", TranslationStatus.Translated),
            Unit(2, TextUnitKind.Name, "真弓", "Outra", TranslationStatus.Machine),
            Unit(3, TextUnitKind.Name, "犬井", "Inui", TranslationStatus.Keep),
        ];

        var names = GameCharacters.Names(units);

        Assert.Equal("Mayumi", names.Single(n => n.Source == "真弓").Target);
        Assert.Equal(string.Empty, names.Single(n => n.Source == "犬井").Target);
    }

    [Fact]
    public void Missing_devolve_so_os_nomes_que_ainda_nao_sao_personagens()
    {
        var names = GameCharacters.Names([Unit(0, TextUnitKind.Name, "真弓"), Unit(1, TextUnitKind.Name, "犬井"), Unit(2, TextUnitKind.Name, "猿山")]);
        CharacterEntry[] existing = [new("犬井", "Inui", CharacterGender.Male)];

        var missing = GameCharacters.Missing(names, existing);

        Assert.Equal(["真弓", "猿山"], missing.Select(n => n.Source));
    }

    [Fact]
    public void Sem_unidades_name_a_lista_e_vazia()
    {
        Assert.Empty(GameCharacters.Names([Unit(0, TextUnitKind.Dialogue, "真弓")]));
        Assert.Empty(GameCharacters.Missing([], [new CharacterEntry("真弓", "Mayumi")]));
    }
}
