using VNForge.App.Controls;

namespace VNForge.App.Tests.Controls;

public class WordDiffTests
{
    private static string Render(IReadOnlyList<DiffSegment> segments) =>
        string.Join(' ', segments.Select(s => s.Kind switch
        {
            DiffKind.Insert => $"+{s.Text}",
            DiffKind.Delete => $"-{s.Text}",
            _ => s.Text,
        }));

    [Fact]
    public void Insercoes_e_remocoes_por_palavra_como_no_canvas()
    {
        var diff = WordDiff.Compute("Não enche… Eu não achava o guarda-chuva.\\p", "Me deixa… Não achei o guarda-chuva.\\p");

        Assert.Equal("+Me +deixa… Não -enche… -Eu -não -achava +achei o guarda-chuva.", Render(diff));
    }

    [Fact]
    public void Textos_iguais_nao_tem_marcacao()
    {
        var diff = WordDiff.Compute("Bom dia.\\p", "Bom dia.\\p");

        Assert.All(diff, s => Assert.Equal(DiffKind.Equal, s.Kind));
        Assert.Equal(2, diff.Count);
    }
}
