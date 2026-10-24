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
    public void Sem_traducao_atual_mostra_a_sugestao_inteira_sem_marcacao()
    {
        var diff = WordDiff.Compute(null, "Ehehe… Desculpaaa.\\p");

        Assert.Equal(["Ehehe…", "Desculpaaa."], diff.Select(s => s.Text));
        Assert.All(diff, s => Assert.Equal(DiffKind.Equal, s.Kind));
    }

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

    [Fact]
    public void Pausas_nao_contam_e_quebra_de_linha_vira_espaco()
    {
        var diff = WordDiff.Compute("…Tem certeza?  Você vai se molhar.", "…Tem certeza?\\nVocê vai se molhar.\\p");

        Assert.All(diff, s => Assert.Equal(DiffKind.Equal, s.Kind));
    }

    [Fact]
    public void Sugestao_vazia_marca_tudo_como_remocao()
    {
        var diff = WordDiff.Compute("uma frase", null);

        Assert.All(diff, s => Assert.Equal(DiffKind.Delete, s.Kind));
        Assert.Equal(2, diff.Count);
    }
}
