using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>Cada nome do jogo tem uma só tradução: o personagem e as unidades <c>name</c> andam juntos (#426).</summary>
public class CharacterNameSyncTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Nome(string script, int n, string original, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{script}:{n:D6}", TextUnitKind.Name, null, original, traducao, status);

    private static TextUnit Fala(string script, int n, string original, string? falante = null) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, falante, original, null, TranslationStatus.Untranslated);

    private static (ProjectFolder Projeto, LineEditor Editor) Preparar(string raiz)
    {
        var projeto = NovoProjeto(raiz);
        projeto.SaveText("yst00073", [Nome("yst00073", 0, "真弓"), Nome("yst00073", 1, "犬井"), Fala("yst00073", 2, "真弓", "真弓")]);
        projeto.SaveText("yst00074", [Nome("yst00074", 0, "真弓"), Fala("yst00074", 1, "真弓")]);
        return (projeto, new LineEditor(projeto, new FakeTimeProvider(Agora)));
    }

    private static TextUnit Gravada(ProjectFolder projeto, string script, int n) =>
        projeto.LoadText(script).Single(u => u.Id == $"{script}:{n:D6}");

    [Fact]
    public void ApplyToUnits_nao_toca_falas_nem_outros_nomes_e_nao_regrava_quando_nada_muda()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path);
        CharacterEntry[] personagens = [new("真弓", "Mayumi")];

        CharacterNameSync.ApplyToUnits(projeto, editor, personagens);

        Assert.Null(Gravada(projeto, "yst00073", 1).Translation);
        Assert.Null(Gravada(projeto, "yst00073", 2).Translation);
        Assert.Null(Gravada(projeto, "yst00074", 1).Translation);
        Assert.Equal(0, CharacterNameSync.ApplyToUnits(projeto, editor, personagens));
    }

    [Fact]
    public void ApplyToUnits_com_a_lista_de_scripts_so_le_esses_scripts()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path);

        var alteradas = CharacterNameSync.ApplyToUnits(projeto, editor, [new CharacterEntry("真弓", "Mayumi")], ["yst00073"]);

        Assert.Equal(1, alteradas);
        Assert.Null(Gravada(projeto, "yst00074", 0).Translation);
    }

    [Fact]
    public void Traduzir_uma_unidade_name_atualiza_o_personagem_e_as_outras_unidades_do_mesmo_original()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path);
        projeto.SaveCharacters([new CharacterEntry("真弓", "Antigo", CharacterGender.Female, "formal"), new CharacterEntry("犬井", "Inui")]);

        editor.SetTranslation("yst00073", "yst00073:000000", "Mayumi");

        var personagens = projeto.LoadCharacters();
        var mayumi = personagens.Single(c => c.Source == "真弓");
        Assert.Equal("Mayumi", mayumi.Target);
        Assert.Equal(CharacterGender.Female, mayumi.Gender);
        Assert.Equal("formal", mayumi.Tone);
        Assert.Equal("Inui", personagens.Single(c => c.Source == "犬井").Target);
        Assert.Equal("Mayumi", Gravada(projeto, "yst00074", 0).Translation);
        Assert.Null(Gravada(projeto, "yst00073", 1).Translation);
    }

    [Fact]
    public void Characters_json_invalido_nao_impede_gravar_a_linha()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path);
        File.WriteAllText(projeto.CharactersPath, "{ isto não é json");

        var gravada = editor.SetTranslation("yst00073", "yst00073:000000", "Mayumi");

        Assert.Equal("Mayumi", gravada.Translation);
        Assert.Equal("Mayumi", Gravada(projeto, "yst00073", 0).Translation);
        Assert.Equal("{ isto não é json", File.ReadAllText(projeto.CharactersPath));
    }
}
