using VNForge.Core.Glossary;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Search;

public class BatchRequestScopeTests
{
    private static readonly CharacterEntry[] Characters =
    [
        new("健太", "Kenta"),
        new("美咲", "Misaki Sakura"),
    ];

    private static TextUnit Line(string id, string? speaker, string? translation, TranslationStatus status = TranslationStatus.Machine) =>
        new(id, TextUnitKind.Dialogue, speaker, "原文", translation, status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts() =>
        new()
        {
            ["cena01"] =
            [
                Line("a1", "健太", "Você sabe o que houve?"),
                Line("a2", "美咲", "Você chegou cedo."),
                Line("a3", "健太", "Vamos embora."),
                Line("a4", "健太", null, TranslationStatus.Untranslated),
            ],
            ["cena02"] =
            [
                Line("b1", "健太", "Eu vi voce ontem."),
                Line("b2", null, "Você não veio."),
            ],
        };

    private static BatchRequestScope Parse(string request) => BatchRequestScope.Parse(request, Characters, ["cena01", "cena02"]);

    [Fact]
    public void O_pedido_do_Kenta_acha_o_personagem_e_o_termo()
    {
        var scope = Parse("nas falas do Kenta, trocar você por tu");

        Assert.Equal(["健太"], scope.Speakers);
        Assert.Equal(["você"], scope.Terms);
        Assert.Empty(scope.Scripts);
        Assert.True(scope.IsNarrowed);
    }

    [Fact]
    public void Um_nome_solto_do_personagem_basta()
    {
        Assert.Equal(["美咲"], Parse("deixar as falas da Sakura mais formais").Speakers);
    }

    [Fact]
    public void Palavra_dentro_de_outra_nao_acha_personagem()
    {
        Assert.Empty(Parse("trocar Kentaro por tu").Speakers);
    }

    [Fact]
    public void A_cena_citada_restringe_o_pedido()
    {
        var scope = Parse("na cena02 trocar você por tu");

        Assert.Equal(["cena02"], scope.Scripts);
    }

    [Theory]
    [InlineData("trocar \"você\" por \"tu\"", "você")]
    [InlineData("substituir “você” por tu", "você")]
    [InlineData("troque 'não sei' por não faço ideia", "não sei")]
    [InlineData("tirar o \"né\" das falas", "né")]
    public void O_termo_a_trocar_vem_do_pedido(string request, string term)
    {
        Assert.Equal([term], Parse(request).Terms);
    }

    [Theory]
    [InlineData("mudar as falas do Kenta para o tom formal")]
    [InlineData("mudar o tom para mais formal")]
    [InlineData("trocar o estilo das frases para algo mais seco")]
    public void Pedido_de_estilo_nao_vira_termo_literal(string request)
    {
        Assert.Empty(Parse(request).Terms);
    }

    [Fact]
    public void Pedido_de_estilo_ainda_restringe_pelo_personagem()
    {
        var scope = Parse("mudar as falas do Kenta para o tom formal");

        Assert.Equal(["健太"], scope.Speakers);
        Assert.Equal(["a1", "a3", "b1"], scope.Select(Texts(), Characters, TestContext.Current.CancellationToken).Select(c => c.Unit.Id));
    }

    [Fact]
    public void O_nome_que_e_o_termo_da_troca_nao_conta_como_falante()
    {
        var scope = Parse("trocar Kenta por Kento");

        Assert.Equal(["Kenta"], scope.Terms);
        Assert.Empty(scope.Speakers);
    }

    [Fact]
    public void Pedido_sem_filtro_nao_restringe_nada()
    {
        var scope = Parse("deixar tudo mais natural");

        Assert.False(scope.IsNarrowed);
    }

    [Fact]
    public void Candidatas_sao_as_falas_do_personagem_com_o_termo_ignorando_acento_e_caixa()
    {
        var scope = Parse("nas falas do Kenta, trocar você por tu");

        var candidates = scope.Select(Texts(), Characters, TestContext.Current.CancellationToken);

        Assert.Equal(["a1", "b1"], candidates.Select(c => c.Unit.Id));
        Assert.Equal(["cena01", "cena02"], candidates.Select(c => c.ScriptKey));
        Assert.Equal([1, 1], candidates.Select(c => c.Number));
    }

    [Fact]
    public void Sem_personagem_vale_o_termo_em_todas_as_falas_traduzidas()
    {
        var candidates = Parse("trocar você por tu").Select(Texts(), Characters, TestContext.Current.CancellationToken);

        Assert.Equal(["a1", "a2", "b1", "b2"], candidates.Select(c => c.Unit.Id));
    }

    [Fact]
    public void Linha_sem_traducao_nunca_e_candidata()
    {
        var candidates = Parse("nas falas do Kenta").Select(Texts(), Characters, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(candidates, c => c.Unit.Id == "a4");
        Assert.Equal(["a1", "a3", "b1"], candidates.Select(c => c.Unit.Id));
    }

    [Fact]
    public void O_termo_vale_so_como_palavra_inteira()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>
        {
            ["cena01"] = [Line("x1", null, "Vocês chegaram."), Line("x2", null, "Você chegou.")],
        };

        var candidates = Parse("trocar você por tu").Select(texts, Characters, TestContext.Current.CancellationToken);

        Assert.Equal(["x2"], candidates.Select(c => c.Unit.Id));
    }

    [Fact]
    public void A_busca_pode_ser_cancelada()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => Parse("trocar você por tu").Select(Texts(), Characters, cancel.Token));
    }
}
