using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public class FormattingProfileSerializerTests
{
    [Fact]
    public void Objeto_vazio_usa_todos_os_valores_padrao()
    {
        var profile = FormattingProfileSerializer.Parse("{}");

        AssertDefault(profile);
    }

    [Fact]
    public void Wrap_sem_campos_usa_os_padroes_de_cada_um()
    {
        var profile = FormattingProfileSerializer.Parse("""{ "wrap": { "enabled": true } }""");

        Assert.True(profile.Wrap.Enabled);
        Assert.Equal(FormattingProfile.Default.Wrap.MaxWidth, profile.Wrap.MaxWidth);
        Assert.Equal(FormattingProfile.Default.Wrap.MaxLines, profile.Wrap.MaxLines);
        Assert.Equal(FormattingProfile.Default.Wrap.CharWidth, profile.Wrap.CharWidth);
        Assert.Equal(FormattingProfile.Default.Kinds, profile.Kinds);
    }

    [Fact]
    public void Le_todos_os_campos()
    {
        var profile = FormattingProfileSerializer.Parse("""
            {
              // comentário e vírgula final são aceitos
              "kinds": ["dialogue", "choice"],
              "wrap": { "enabled": true, "maxWidth": 40, "maxLines": null, "charWidth": "uniform", },
            }
            """);

        Assert.Equal([TextUnitKind.Dialogue, TextUnitKind.Choice], profile.Kinds);
        Assert.Equal(new WrapRule(true, 40, null, CharWidthMode.Uniform), profile.Wrap);
    }

    [Fact]
    public void Padrao_deixa_a_quebra_desligada()
    {
        Assert.False(FormattingProfile.Default.Wrap.Enabled);
        Assert.Equal([TextUnitKind.Dialogue, TextUnitKind.Narration], FormattingProfile.Default.Kinds);
    }

    [Fact]
    public void Serializa_e_le_de_volta_o_mesmo_perfil()
    {
        var profile = new FormattingProfile([TextUnitKind.Narration], new WrapRule(true, 30, 4, CharWidthMode.Uniform));

        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Kinds, back.Kinds);
        Assert.Equal(profile.Wrap, back.Wrap);
    }

    [Fact]
    public void Serializa_com_LF_e_termina_em_quebra_de_linha()
    {
        var json = FormattingProfileSerializer.Serialize(FormattingProfile.Default);

        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "wrap": { "maxWidth": 0 } }""", "$.wrap.maxWidth")]
    [InlineData("""{ "wrap": { "maxWidth": "larga" } }""", "$.wrap.maxWidth")]
    [InlineData("""{ "wrap": { "maxWidth": null } }""", "$.wrap.maxWidth")]
    [InlineData("""{ "wrap": { "maxLines": 0 } }""", "$.wrap.maxLines")]
    [InlineData("""{ "wrap": { "enabled": "sim" } }""", "$.wrap.enabled")]
    [InlineData("""{ "wrap": { "charWidth": "larga" } }""", "$.wrap.charWidth")]
    [InlineData("""{ "wrap": { "larguraMaxima": 5 } }""", "$.wrap.larguraMaxima")]
    [InlineData("""{ "wrap": 3 }""", "$.wrap")]
    [InlineData("""{ "kinds": ["fala"] }""", "$.kinds[0]")]
    [InlineData("""{ "kinds": "dialogue" }""", "$.kinds")]
    [InlineData("""{ "kinds": [] }""", "$.kinds")]
    [InlineData("""{ "outro": 1 }""", "$.outro")]
    [InlineData("[]", "$")]
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

    private static void AssertDefault(FormattingProfile profile)
    {
        Assert.Equal(FormattingProfile.Default.Kinds, profile.Kinds);
        Assert.Equal(FormattingProfile.Default.Wrap, profile.Wrap);
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
