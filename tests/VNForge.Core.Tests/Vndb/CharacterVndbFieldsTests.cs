using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Vndb;

/// <summary>Romaji, apelidos e descrição do VNDB em <c>characters.json</c> (#536): opcionais, e o arquivo antigo continua valendo.</summary>
public sealed class CharacterVndbFieldsTests
{
    [Fact]
    public void Os_campos_do_vndb_ficam_fora_do_json_quando_nulos()
    {
        var json = CharacterSerializer.Serialize([new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male)]);

        Assert.DoesNotContain("romaji", json, StringComparison.Ordinal);
        Assert.DoesNotContain("aliases", json, StringComparison.Ordinal);
        Assert.DoesNotContain("description", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Romaji_apelidos_e_descricao_fazem_a_ida_e_volta()
    {
        var entry = new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male, Romaji: "Aizawa Kenta", Aliases: ["Ken", "Kenta-kun"], Description: "O protagonista.");

        var back = Assert.Single(CharacterSerializer.Parse(CharacterSerializer.Serialize([entry]), "characters.json"));

        Assert.Equal(("Aizawa Kenta", "O protagonista."), (back.Romaji, back.Description));
        Assert.Equal(["Ken", "Kenta-kun"], back.Aliases);
    }

    [Fact]
    public void Arquivo_sem_os_campos_novos_continua_valendo()
    {
        const string old = "{\n  \"version\": 1,\n  \"entries\": [\n    {\"source\":\"凛\",\"target\":\"Rin\",\"gender\":\"female\"}\n  ]\n}\n";

        var entry = Assert.Single(CharacterSerializer.Parse(old, "characters.json"));

        Assert.Null(entry.Romaji);
        Assert.Null(entry.Aliases);
        Assert.Null(entry.Description);
    }
}
