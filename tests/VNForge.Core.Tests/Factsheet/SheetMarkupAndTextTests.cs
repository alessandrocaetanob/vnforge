using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetMarkupAndTextTests
{
    [Fact]
    public void Normalize_tira_a_formatacao_do_VNDB_mas_deixa_os_marcadores_de_spoiler()
    {
        var text = SheetMarkup.Normalize("Um [b]jogo[/b] de [url=https://vndb.org/v1]Kenta[/url].\r\n\r\n\r\n\r\nFim [SPOILER]ele morre[/Spoiler].");

        Assert.Equal("Um jogo de Kenta.\n\nFim [spoiler]ele morre[/spoiler].", text);
    }

    [Theory]
    [InlineData("[b] [/b]")]
    public void Normalize_de_texto_sem_conteudo_devolve_nulo(string? text) => Assert.Null(SheetMarkup.Normalize(text));

    [Fact]
    public void StripSpoilers_tira_os_trechos_fechados_e_o_aberto_ate_o_fim()
    {
        Assert.Equal("Antes. Depois.", SheetMarkup.StripSpoilers("Antes. [spoiler]segredo[/spoiler]Depois."));
        Assert.Equal("Antes.", SheetMarkup.StripSpoilers("Antes. [spoiler]sem fechar\n\nmais segredo"));
        Assert.Null(SheetMarkup.StripSpoilers("[spoiler]tudo[/spoiler]"));
    }

    [Fact]
    public void SpoilerMarkers_conta_os_que_abrem_e_os_que_fecham()
    {
        Assert.Equal((2, 1), SheetMarkup.SpoilerMarkers("[spoiler]a[/spoiler] [SPOILER]b"));
        Assert.Equal((0, 0), SheetMarkup.SpoilerMarkers(null));
        Assert.True(SheetMarkup.HasSpoilers("x [spoiler]y"));
    }

    [Fact]
    public void O_texto_passa_de_pendente_a_IA_a_revisado_e_a_aprovado()
    {
        var text = SheetText.Pending("Hello");
        Assert.Equal("Hello", text.Shown);

        text = text.TranslatedByAi("  Olá ");
        Assert.Equal(("Olá", TranslationStatus.Machine), (text.Shown, text.Status));

        text = text.Reviewed();
        Assert.Equal(TranslationStatus.Reviewed, text.Status);
        Assert.Equal(TranslationStatus.Approved, text.Approved().Status);
        Assert.Equal(text.Approved(), text.Approved().Approved());
    }

    [Fact]
    public void Aprovar_o_que_nao_foi_revisado_ou_nao_tem_traducao_recusa()
    {
        var machine = SheetText.Pending("Hello").TranslatedByAi("Olá");

        Assert.Contains("revise", Assert.Throws<LibraryException>(machine.Approved).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<LibraryException>(SheetText.Pending("Hello").Reviewed);
    }

    [Fact]
    public void A_traducao_precisa_manter_os_marcadores_de_spoiler_do_original()
    {
        var text = SheetText.Pending("A [spoiler]B[/spoiler] C");

        Assert.Equal("A [spoiler]B[/spoiler] C", text.TranslatedByAi("A [spoiler]B[/spoiler] C").Target);
        Assert.Throws<LibraryException>(() => text.TranslatedByAi("A B C"));
        Assert.Throws<LibraryException>(() => text.EditedByPerson("A [spoiler]B C"));
        Assert.Throws<LibraryException>(() => text.TranslatedByAi("  "));
    }

    [Fact]
    public void Original_que_mudou_descarta_a_traducao_da_IA_e_marca_a_da_pessoa_como_desatualizada()
    {
        var machine = SheetText.Pending("old").TranslatedByAi("velho");
        var reviewed = SheetText.Pending("old").TranslatedByAi("velho").Reviewed();

        Assert.Equal(SheetText.Pending("new"), machine.WithSource("new"));
        Assert.Same(reviewed, reviewed.WithSource("old"));
        var outdated = reviewed.WithSource("new");
        Assert.Equal(("new", "velho", TranslationStatus.Translated, true), (outdated.Source, outdated.Target, outdated.Status, outdated.Outdated));
        Assert.False(outdated.Reviewed().Outdated);
    }
}
