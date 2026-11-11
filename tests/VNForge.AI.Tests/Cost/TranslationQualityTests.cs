using VNForge.AI.Claude;
using VNForge.AI.Cost;

namespace VNForge.AI.Tests.Cost;

public class TranslationQualityTests
{

    [Fact]
    public void Tempo_das_linhas_que_faltam_segue_o_ritmo_da_qualidade()
    {
        var careful = QualityProfiles.For(TranslationQuality.Careful);

        Assert.Equal(TimeSpan.Zero, careful.TimeFor(0));
        Assert.Equal(TimeSpan.FromMinutes(1), careful.TimeFor(careful.LinesPerMinute));
        Assert.True(QualityProfiles.For(TranslationQuality.Fast).TimeFor(1000) < careful.TimeFor(1000));
    }
}
