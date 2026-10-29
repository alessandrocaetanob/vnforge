using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public sealed class AddressSamplesTests
{
    private static TextUnit Line(string speaker, string source) =>
        new($"s:{Guid.NewGuid():N}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static readonly CharacterEntry[] Cast =
    [
        new("健太", "Kenta"),
        new("佐藤 美咲", "Misaki Sato"),
        new("凛", "Rin"),
    ];

    [Fact]
    public void Entram_as_falas_que_citam_outro_personagem_ou_trazem_honorifico()
    {
        var samples = AddressSamples.From(
            [
                Line("健太", "美咲ちゃん、おはよう"),
                Line("健太", "今日は暑いね"),
                Line("佐藤 美咲", "先輩、遅いです"),
                Line("凛", "一人でいい"),
            ], Cast);

        Assert.Equal(["健太", "佐藤 美咲"], samples.Keys);
        Assert.Equal(["美咲ちゃん、おはよう"], samples["健太"].Select(l => l.Text));
        Assert.Equal(["先輩、遅いです"], samples["佐藤 美咲"].Select(l => l.Text));
    }

    [Fact]
    public void O_nome_do_proprio_falante_nao_conta_e_o_sobrenome_sozinho_casa()
    {
        var samples = AddressSamples.From([Line("健太", "健太は行くよ"), Line("健太", "佐藤、待って")], Cast);

        Assert.Equal(["佐藤、待って"], samples["健太"].Select(l => l.Text));
    }

    [Fact]
    public void O_falante_escolhido_na_linha_vale_no_lugar_do_extraido()
    {
        var chosen = Line("凛", "美咲ちゃん、おはよう") with { ChosenSpeaker = "健太" };

        var samples = AddressSamples.From([chosen], Cast);

        Assert.Equal(["健太"], samples.Keys);
    }

    [Fact]
    public void A_fala_diz_com_quem_o_falante_estava_respondendo()
    {
        var samples = AddressSamples.From([Line("凛", "ねえ"), Line("健太", "美咲ちゃん？"), Line("健太", "先輩はどこ？")], Cast);

        Assert.Equal(["凛", null], samples["健太"].Select(l => l.ReplyingTo));
    }

    [Fact]
    public void Narracao_falante_desconhecido_e_so_sinais_ficam_de_fora_e_nao_repetem_a_fala()
    {
        var narration = new TextUnit("s:n", TextUnitKind.Dialogue, null, "美咲ちゃん", null, TranslationStatus.Untranslated);

        var samples = AddressSamples.From([narration, Line("？？？", "美咲ちゃん"), Line("誰か", "美咲ちゃん"), Line("健太", "美咲ちゃん"), Line("健太", "美咲ちゃん")], Cast);

        Assert.Equal(["健太"], samples.Keys);
        Assert.Single(samples["健太"]);
    }

    [Fact]
    public void Muitas_falas_viram_uma_amostra_espalhada_pelo_texto()
    {
        var lines = Enumerable.Range(0, 100).Select(i => Line("健太", $"美咲ちゃん、{i}")).ToList();

        var picked = AddressSamples.From(lines, Cast, maxLines: 10)["健太"];

        Assert.Equal(10, picked.Count);
        Assert.Equal("美咲ちゃん、0", picked[0].Text);
        Assert.Equal("美咲ちゃん、90", picked[^1].Text);
    }
}
