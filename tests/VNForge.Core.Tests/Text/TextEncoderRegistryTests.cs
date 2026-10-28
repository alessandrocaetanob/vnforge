using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class TextEncoderRegistryTests
{
    [Fact]
    public void Default_cria_cada_encoder_pelo_id()
    {
        Assert.IsType<ShiftJisTextEncoder>(TextEncoderRegistry.Default.Create("sjis"));
        Assert.IsType<FoldTextEncoder>(TextEncoderRegistry.Default.Create("fold"));
        Assert.IsType<Cp1252TextEncoder>(TextEncoderRegistry.Default.Create("cp1252"));
    }

    [Fact]
    public void O_delimitador_da_fala_chega_so_ao_encoder_que_o_usa()
    {
        var padrao = Assert.IsType<Cp1252TextEncoder>(TextEncoderRegistry.Default.Create("cp1252"));
        var guillemets = Assert.IsType<Cp1252TextEncoder>(TextEncoderRegistry.Default.Create("cp1252", SpeakerDelimiterStyle.Guillemets));

        Assert.Equal(SpeakerDelimiterStyle.None, padrao.SpeakerDelimiter);
        Assert.Equal(SpeakerDelimiterStyle.Guillemets, guillemets.SpeakerDelimiter);
        Assert.IsType<ShiftJisTextEncoder>(TextEncoderRegistry.Default.Create("sjis", SpeakerDelimiterStyle.Guillemets));
        Assert.IsType<FoldTextEncoder>(TextEncoderRegistry.Default.Create("fold", SpeakerDelimiterStyle.Guillemets));
    }

    [Fact]
    public void Fabrica_que_recebe_o_estilo_entra_com_uma_linha()
    {
        var registry = new TextEncoderRegistry().Register("cp1252", style => new Cp1252TextEncoder(style));

        Assert.Equal(SpeakerDelimiterStyle.Guillemets, Assert.IsType<Cp1252TextEncoder>(registry.Create("cp1252", SpeakerDelimiterStyle.Guillemets)).SpeakerDelimiter);
    }

    [Fact]
    public void Estrategia_desconhecida_e_erro_em_portugues_com_os_ids_aceitos()
    {
        var ex = Assert.Throws<VNForgeException>(() => TextEncoderRegistry.Default.Create("latin"));

        Assert.Contains("'latin' não é conhecida", ex.Message);
        Assert.Contains("sjis, fold, remap, cp1252", ex.Message);
    }

    [Fact]
    public void Estrategia_conhecida_sem_encoder_nao_cai_em_shift_jis()
    {
        const string id = "remap";

        var ex = Assert.Throws<VNForgeException>(() => TextEncoderRegistry.Default.Create(id));

        Assert.Contains($"'{id}' ainda não está disponível", ex.Message);
        Assert.Contains("sjis, fold, cp1252", ex.Message);
    }

    [Fact]
    public void Cada_encoder_entra_com_uma_linha()
    {
        var registry = new TextEncoderRegistry().Register("fold", () => new FoldTextEncoder());

        Assert.IsType<FoldTextEncoder>(registry.Create("fold"));
        Assert.Equal(["fold"], registry.AvailableIds);
    }

    [Fact]
    public void Id_vazio_ou_fabrica_nula_sao_recusados()
    {
        var registry = new TextEncoderRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(" ", () => new FoldTextEncoder()));
        Assert.Throws<ArgumentNullException>(() => registry.Register("fold", (Func<ITextEncoder>)null!));
        Assert.Throws<ArgumentNullException>(() => registry.Create(null!));
    }
}
