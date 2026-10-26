using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class StyleConfigTests
{
    private const string Base = """
        {
          "name": "Minha VN",
          "game": { "path": "D:/Jogos/MinhaVN", "exe": "MinhaVN.exe" },
          "engine": { "id": "yuris" },
          "languages": { "source": "ja", "target": "pt-BR" },
          "encoding": { "strategy": "cp1252" }
          STYLE
        }
        """;

    private static ProjectConfig Parse(string style = "") => ProjectConfigSerializer.Parse(Base.Replace("STYLE", style));

    [Fact]
    public void Sem_bloco_style_vale_o_padrao_e_ele_nao_e_gravado()
    {
        var config = Parse();

        Assert.Equal(StyleConfig.Default, config.Style);
        Assert.Equal(HonorificsPolicy.Adapt, config.Style.Honorifics);
        Assert.Equal(AddressForm.Voce, config.Style.Address);
        Assert.True(config.Style.TranslateOnomatopoeia);
        Assert.True(config.Style.WesternNameOrder);
        Assert.True(config.Style.KeyboardPunctuation);
        Assert.DoesNotContain("\"style\"", ProjectConfigSerializer.Serialize(config));
    }

    [Fact]
    public void Parse_le_todas_as_escolhas()
    {
        var config = Parse("""
            , "style": {
              "honorifics": "keep", "address": "tu",
              "narrator": { "who": "Kenta (protagonista)", "person": "third", "gender": "female" },
              "onomatopoeia": false, "westernNameOrder": false
            }
            """);

        Assert.Equal(HonorificsPolicy.Keep, config.Style.Honorifics);
        Assert.Equal(AddressForm.Tu, config.Style.Address);
        Assert.Equal("Kenta (protagonista)", config.Style.Narrator.Who);
        Assert.Equal(NarratorPerson.Third, config.Style.Narrator.Person);
        Assert.Equal(NarratorGender.Female, config.Style.Narrator.Gender);
        Assert.False(config.Style.TranslateOnomatopoeia);
        Assert.False(config.Style.WesternNameOrder);
    }

    [Fact]
    public void A_troca_da_pontuacao_japonesa_vem_ligada_e_faz_ida_e_volta_desligada()
    {
        Assert.True(StyleConfig.Default.KeyboardPunctuation);
        var desligada = Parse() with { Style = StyleConfig.Default with { KeyboardPunctuation = false } };

        var json = ProjectConfigSerializer.Serialize(desligada);

        Assert.Contains("\"keyboardPunctuation\": false", json);
        Assert.False(ProjectConfigSerializer.Parse(json).Style.KeyboardPunctuation);
        Assert.False(Parse(", \"style\": { \"keyboardPunctuation\": false }").Style.KeyboardPunctuation);
    }

    [Fact]
    public void Bloco_style_parcial_completa_o_resto_com_o_padrao()
    {
        var config = Parse("""
            , "style": { "honorifics": "remove" }
            """);

        Assert.Equal(StyleConfig.Default with { Honorifics = HonorificsPolicy.Remove }, config.Style);
    }

    [Fact]
    public void Serialize_e_Parse_fazem_ida_e_volta_do_estilo()
    {
        var style = new StyleConfig
        {
            Honorifics = HonorificsPolicy.Remove,
            Address = AddressForm.Tu,
            Narrator = new NarratorStyle { Who = "  Kenta  ", Person = NarratorPerson.Third, Gender = NarratorGender.Male },
            TranslateOnomatopoeia = false,
            WesternNameOrder = false,
        };
        var original = Parse() with { Style = style };

        var json = ProjectConfigSerializer.Serialize(original);
        var back = ProjectConfigSerializer.Parse(json);

        Assert.Equal(style with { Narrator = style.Narrator with { Who = "Kenta" } }, back.Style);
        Assert.Contains("\"honorifics\": \"remove\"", json);
        Assert.Contains("\"who\": \"Kenta\"", json);
    }

    [Theory]
    [InlineData("\"honorifics\": \"tanto-faz\"", "$.style.honorifics")]
    [InlineData("\"address\": 3", "$.style.address")]
    [InlineData("\"narrator\": { \"person\": \"segunda\" }", "$.style.narrator.person")]
    [InlineData("\"narrator\": { \"gender\": \"x\" }", "$.style.narrator.gender")]
    [InlineData("\"onomatopoeia\": \"sim\"", "$.style.onomatopoeia")]
    [InlineData("\"cor\": \"azul\"", "$.style.cor")]
    public void Estilo_invalido_aponta_o_campo(string style, string field)
    {
        var ex = Assert.Throws<ProjectConfigException>(() => Parse($", \"style\": {{ {style} }}"));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Serialize_recusa_valor_de_enum_fora_da_lista()
    {
        var config = Parse() with { Style = new StyleConfig { Honorifics = (HonorificsPolicy)9 } };

        var ex = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Serialize(config));

        Assert.Equal("$.style.honorifics", ex.Field);
    }

    [Fact]
    public void CacheKey_muda_com_cada_escolha_e_ignora_espacos_do_narrador()
    {
        var baseKey = StyleConfig.Default.CacheKey();

        StyleConfig[] changed =
        [
            StyleConfig.Default with { Honorifics = HonorificsPolicy.Keep },
            StyleConfig.Default with { Address = AddressForm.Tu },
            StyleConfig.Default with { Narrator = new NarratorStyle { Who = "Kenta" } },
            StyleConfig.Default with { Narrator = new NarratorStyle { Person = NarratorPerson.Third } },
            StyleConfig.Default with { Narrator = new NarratorStyle { Gender = NarratorGender.Male } },
            StyleConfig.Default with { TranslateOnomatopoeia = false },
            StyleConfig.Default with { WesternNameOrder = false },
        ];

        Assert.All(changed, c => Assert.NotEqual(baseKey, c.CacheKey()));

        // A troca da pontuação vem depois do cache: ligar ou desligar não invalida o que já foi pago.
        Assert.Equal(baseKey, (StyleConfig.Default with { KeyboardPunctuation = false }).CacheKey());
        Assert.Equal(
            (StyleConfig.Default with { Narrator = new NarratorStyle { Who = "Kenta" } }).CacheKey(),
            (StyleConfig.Default with { Narrator = new NarratorStyle { Who = " Kenta " } }).CacheKey());
        Assert.Equal(baseKey, (StyleConfig.Default with { Narrator = new NarratorStyle { Who = "  " } }).CacheKey());
    }
}
