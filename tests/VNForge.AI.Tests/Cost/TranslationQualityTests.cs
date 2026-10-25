using VNForge.AI.Claude;
using VNForge.AI.Cost;

namespace VNForge.AI.Tests.Cost;

public class TranslationQualityTests
{
    [Theory]
    [InlineData(TranslationQuality.Fast, ClaudeModels.Haiku)]
    [InlineData(TranslationQuality.Balanced, ClaudeModels.Sonnet)]
    [InlineData(TranslationQuality.Careful, ClaudeModels.Opus)]
    public void Cada_qualidade_aponta_para_um_modelo(TranslationQuality quality, string model)
    {
        Assert.Equal(model, QualityProfiles.For(quality).Model);
        Assert.Equal(quality, QualityProfiles.ForModel(model)?.Quality);
    }

    [Fact]
    public void Fable_nao_tem_qualidade_porque_so_aparece_nos_detalhes_tecnicos()
    {
        Assert.Null(QualityProfiles.ForModel(ClaudeModels.Fable));
    }

    [Fact]
    public void A_ordem_vai_da_mais_rapida_para_a_mais_cuidadosa()
    {
        Assert.Equal([TranslationQuality.Fast, TranslationQuality.Balanced, TranslationQuality.Careful], QualityProfiles.All.Select(p => p.Quality));
    }

    [Fact]
    public void Tempo_das_linhas_que_faltam_segue_o_ritmo_da_qualidade()
    {
        var careful = QualityProfiles.For(TranslationQuality.Careful);

        Assert.Equal(TimeSpan.Zero, careful.TimeFor(0));
        Assert.Equal(TimeSpan.FromMinutes(1), careful.TimeFor(careful.LinesPerMinute));
        Assert.True(QualityProfiles.For(TranslationQuality.Fast).TimeFor(1000) < careful.TimeFor(1000));
    }
}
