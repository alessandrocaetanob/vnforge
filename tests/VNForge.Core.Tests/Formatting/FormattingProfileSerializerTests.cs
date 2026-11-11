using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public class FormattingProfileSerializerTests
{

    [Fact]
    public void Serializa_e_le_de_volta_o_mesmo_perfil()
    {
        var profile = new FormattingProfile([TextUnitKind.Narration], new WrapRule(true, 30, 4, CharWidthMode.Uniform));

        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Kinds, back.Kinds);
        Assert.Equal(profile.Wrap, back.Wrap);
    }

    [Theory]
    [InlineData("""{ "wrap": { "maxWidth": "larga" } }""", "$.wrap.maxWidth")]
    [InlineData("""{ "wrap": { "maxLines": 0 } }""", "$.wrap.maxLines")]
    [InlineData("""{ "wrap": { "enabled": "sim" } }""", "$.wrap.enabled")]
    [InlineData("""{ "wrap": { "charWidth": "larga" } }""", "$.wrap.charWidth")]
    [InlineData("""{ "wrap": 3 }""", "$.wrap")]
    [InlineData("""{ "kinds": ["fala"] }""", "$.kinds[0]")]
    [InlineData("""{ "kinds": "dialogue" }""", "$.kinds")]
    [InlineData("""{ "kinds": [] }""", "$.kinds")]
    public void Valor_invalido_aponta_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
        Assert.Contains("formatting.json", ex.Message);
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void Json_malformado_diz_a_posicao()
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse("{ \"wrap\": "));

        Assert.Contains("malformado", ex.Message);
    }

    [Fact]
    public void Perfil_de_exemplo_em_samples_le_sem_erro()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "formatting.json");

        var profile = FormattingProfileSerializer.Parse(File.ReadAllText(path));

        Assert.True(profile.Wrap.Enabled);
        Assert.Equal(FormattingProfileSerializer.Serialize(profile), FormattingProfileSerializer.Serialize(FormattingProfileSerializer.Parse(File.ReadAllText(path))));
    }

    [Fact]
    public void Le_e_grava_normalizacao_e_espacos()
    {
        var profile = FormattingProfileSerializer.Parse("""
            { "normalization": { "nfc": true, "nbsp": true }, "spacing": { "spaceBeforePunctuation": true } }
            """);

        Assert.Equal(new NormalizationRule(Nfc: true, Nbsp: true), profile.Normalization);
        Assert.Equal(new SpacingRule(SpaceBeforePunctuation: true), profile.Spacing);

        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));
        Assert.Equal(profile.Normalization, back.Normalization);
        Assert.Equal(profile.Spacing, back.Spacing);
    }

    [Fact]
    public void Campo_desconhecido_em_spacing_cita_o_caminho()
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse("""{ "spacing": { "x": true } }"""));

        Assert.Equal("$.spacing.x", ex.Field);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VNForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
