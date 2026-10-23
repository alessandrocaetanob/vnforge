using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class TextEncoderRegistryTests
{
    [Fact]
    public void Default_cria_o_shift_jis_pelo_id()
    {
        var encoder = TextEncoderRegistry.Default.Create("sjis");

        Assert.IsType<ShiftJisTextEncoder>(encoder);
    }

    [Fact]
    public void Estrategia_desconhecida_e_erro_em_portugues_com_os_ids_aceitos()
    {
        var ex = Assert.Throws<VNForgeException>(() => TextEncoderRegistry.Default.Create("latin"));

        Assert.Contains("'latin' não é conhecida", ex.Message);
        Assert.Contains("sjis, fold, remap, cp1252", ex.Message);
    }

    [Theory]
    [InlineData("fold")]
    [InlineData("remap")]
    [InlineData("cp1252")]
    public void Estrategia_conhecida_sem_encoder_nao_cai_em_shift_jis(string id)
    {
        var ex = Assert.Throws<VNForgeException>(() => TextEncoderRegistry.Default.Create(id));

        Assert.Contains($"'{id}' ainda não está disponível", ex.Message);
        Assert.Contains("sjis", ex.Message);
    }

    [Fact]
    public void Cada_encoder_entra_com_uma_linha()
    {
        var registry = new TextEncoderRegistry().Register("fold", () => new FakeFold());

        Assert.IsType<FakeFold>(registry.Create("fold"));
        Assert.Equal(["fold"], registry.AvailableIds);
    }

    [Fact]
    public void Id_vazio_ou_fabrica_nula_sao_recusados()
    {
        var registry = new TextEncoderRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(" ", () => new FakeFold()));
        Assert.Throws<ArgumentNullException>(() => registry.Register("fold", null!));
        Assert.Throws<ArgumentNullException>(() => registry.Create(null!));
    }
}
