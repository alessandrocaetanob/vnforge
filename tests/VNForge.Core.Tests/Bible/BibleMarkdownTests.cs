using VNForge.Core.Bible;

namespace VNForge.Core.Tests.Bible;

public sealed class BibleMarkdownTests
{
    [Fact]
    public void Exporta_sinopse_linha_do_tempo_e_cada_cena()
    {
        var bible = GameBible.From(
        [
            new BibleScene("yst1")
            {
                Name = "Atraso no portão",
                Summary = "Kenta e Ren chegam atrasados.",
                Characters = ["Kenta", "Ren"],
                Tone = ["cômico leve"],
                Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância")],
            },
            new BibleScene("yst2"),
        ]) with
        {
            Synopsis = "Uma cidade onde sempre chove.",
            Timeline = [new BibleTimelineEntry("Prólogo", "A chuva")],
        };

        var text = BibleMarkdown.Render(bible, "Amaoto no Hakoniwa");

        Assert.StartsWith("# Bíblia de Amaoto no Hakoniwa\n", text, StringComparison.Ordinal);
        Assert.Contains("## Sinopse\n\nUma cidade onde sempre chove.", text, StringComparison.Ordinal);
        Assert.Contains("- Prólogo: A chuva", text, StringComparison.Ordinal);
        Assert.Contains("### yst1 · Atraso no portão\n\nKenta e Ren chegam atrasados.", text, StringComparison.Ordinal);
        Assert.Contains("- Quem aparece: Kenta; Ren", text, StringComparison.Ordinal);
        Assert.Contains("- Relações: Kenta e Ren: Amigos de infância", text, StringComparison.Ordinal);
        Assert.Contains("### yst2\n\nSem resumo ainda.", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain('—', text);
    }

    [Fact]
    public void Bible_vazia_so_tem_o_titulo()
    {
        Assert.Equal("# Bíblia de o jogo\n", BibleMarkdown.Render(GameBible.Empty, " "));
    }
}
