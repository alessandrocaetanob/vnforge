using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class TranslationBatchBuilderTests
{
    private static TextUnit Unit(string id, string source, string? speaker = null, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation,
            translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static readonly GlossaryTerm[] Terms =
    [
        new("魔法学園", "Academia de Magia", "só o nome da escola"),
        new("先輩", "senpai"),
        new("勇者", "herói"),
    ];

    private static readonly CharacterBook Book = new(
    [
        new CharacterEntry("アリス", "Alice", CharacterGender.Female, "formal"),
        new CharacterEntry("ボブ", "Bob"),
    ]);

    [Fact]
    public void So_os_termos_presentes_no_lote_entram_no_prompt()
    {
        var builder = new TranslationBatchBuilder(Terms, Book);

        var batch = builder.Build([Unit("a", "魔法学園へようこそ"), Unit("b", "先輩、こんにちは")]);

        Assert.Equal(["魔法学園", "先輩"], batch.Glossary.Select(g => g.Source));
        Assert.DoesNotContain(batch.Glossary, g => g.Source == "勇者");
        Assert.Equal("só o nome da escola", batch.Glossary[0].Notes);
    }

    [Fact]
    public void Source_do_termo_com_espacos_nas_pontas_ainda_casa()
    {
        var builder = new TranslationBatchBuilder([new GlossaryTerm(" 先輩　", "senpai")], Book);

        Assert.Equal("先輩", Assert.Single(builder.Build([Unit("a", "先輩!")]).Glossary).Source);
    }

    [Fact]
    public void Style_md_existente_e_lido_e_aparado()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md");
        File.WriteAllText(path, "\n informal \n");
        try { Assert.Equal("informal", TranslationBatchBuilder.ReadStyleGuide(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Termo_so_nas_linhas_anteriores_nao_entra()
    {
        var builder = new TranslationBatchBuilder(Terms, Book);

        var batch = builder.Build([Unit("a", "こんにちは")], [Unit("p", "勇者だ", translation: "É um herói")]);

        Assert.Empty(batch.Glossary);
    }

    [Fact]
    public void Personagens_sao_os_falantes_do_lote_sem_repeticao()
    {
        var builder = new TranslationBatchBuilder(Terms, Book);

        var batch = builder.Build([Unit("a", "x", "アリス"), Unit("b", "y", "アリス"), Unit("c", "z", "desconhecido"), Unit("d", "w")]);

        var alice = Assert.Single(batch.Characters);
        Assert.Equal(new CharacterInfo("アリス", "Alice", "formal", "female"), alice);
    }

    [Theory]
    [InlineData("？？？")]
    [InlineData("???")]
    [InlineData("…")]
    public void Falante_so_de_sinais_nao_leva_entrada_de_personagem_mesmo_se_estiver_no_livro(string speaker)
    {
        var book = new CharacterBook([new CharacterEntry(speaker, "Desconhecido", CharacterGender.Male, "rude"), new CharacterEntry("アリス", "Alice")]);
        var builder = new TranslationBatchBuilder(Terms, book);

        var batch = builder.Build([Unit("a", "x", speaker), Unit("b", "y", "アリス")]);

        Assert.Equal(["アリス"], batch.Characters.Select(c => c.Source));
    }

    [Fact]
    public void Contexto_anterior_so_com_linhas_traduzidas_e_limitado_as_ultimas()
    {
        var builder = new TranslationBatchBuilder(Terms, Book, maxPreviousLines: 2);
        var previous = new[]
        {
            Unit("1", "a", translation: "A"),
            Unit("2", "b"),
            Unit("3", "c", translation: "C"),
            Unit("4", "d", translation: "D"),
        };

        var batch = builder.Build([Unit("x", "e")], previous);

        Assert.Equal(["3", "4"], batch.PreviousLines.Select(u => u.Id));
    }

    [Fact]
    public void Guia_de_estilo_entra_sem_espacos_nas_pontas()
    {
        var batch = new TranslationBatchBuilder(Terms, Book, "  informal\n").Build([Unit("a", "x")]);

        Assert.Equal("informal", batch.StyleGuide);
    }

    [Fact]
    public void Style_md_ausente_vira_guia_vazio()
    {
        Assert.Equal(string.Empty, TranslationBatchBuilder.ReadStyleGuide(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md")));
    }
}
