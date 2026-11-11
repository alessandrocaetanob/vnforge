using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Vndb;

/// <summary>Romaji, apelidos e descrição do VNDB em <c>characters.json</c> (#536): opcionais, e o arquivo antigo continua valendo.</summary>
public sealed class CharacterVndbFieldsTests
{

    [Fact]
    public void Romaji_apelidos_e_descricao_fazem_a_ida_e_volta()
    {
        var entry = new CharacterEntry("相沢健太", "Kenta", CharacterGender.Male, Romaji: "Aizawa Kenta", Aliases: ["Ken", "Kenta-kun"], Description: "O protagonista.");

        var back = Assert.Single(CharacterSerializer.Parse(CharacterSerializer.Serialize([entry]), "characters.json"));

        Assert.Equal(("Aizawa Kenta", "O protagonista."), (back.Romaji, back.Description));
        Assert.Equal(["Ken", "Kenta-kun"], back.Aliases);
    }
}
