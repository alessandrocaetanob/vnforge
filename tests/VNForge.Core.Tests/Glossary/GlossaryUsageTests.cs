using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public class GlossaryUsageTests
{
    private static TextUnit Line(string source, string? speaker = null) =>
        new($"s:{source}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static readonly GlossaryUsage Usage = GlossaryUsage.From(
    [
        Line("「先輩、遅刻です」", "美咲"),
        Line("先輩と先輩の傘", "健太"),
        Line("傘を忘れた", "美咲"),
        Line("narração sem falante"),
    ]);

    [Fact]
    public void Termo_ausente_ou_vazio_conta_zero()
    {
        Assert.Equal(0, Usage.LinesContaining("文化祭"));
        Assert.Equal(0, Usage.LinesContaining(""));
        Assert.Equal(0, Usage.LinesContaining("   "));
    }

    [Fact]
    public void Conta_as_linhas_faladas_por_personagem()
    {
        Assert.Equal(2, Usage.LinesSpokenBy("美咲"));
        Assert.Equal(1, Usage.LinesSpokenBy("健太"));
        Assert.Equal(0, Usage.LinesSpokenBy("先生"));
        Assert.Equal(0, Usage.LinesSpokenBy(""));
    }

    [Fact]
    public void Projeto_sem_linhas_conta_zero_em_tudo()
    {
        var usage = GlossaryUsage.From([]);

        Assert.Equal(0, usage.LinesContaining("先輩"));
        Assert.Equal(0, usage.LinesSpokenBy("美咲"));
        Assert.Same(GlossaryUsage.Empty, GlossaryUsage.Empty);
        Assert.Equal(0, GlossaryUsage.Empty.LinesContaining("先輩"));
    }
}
