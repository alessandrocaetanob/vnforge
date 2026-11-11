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

    [Theory]
    [InlineData("trocar \"você\" por \"tu\"", "você")]
    [InlineData("substituir “você” por tu", "você")]
    [InlineData("troque 'não sei' por não faço ideia", "não sei")]
    [InlineData("tirar o \"né\" das falas", "né")]
    public void O_termo_a_trocar_vem_do_pedido(string request, string term)
    {
        Assert.Equal([term], Parse(request).Terms);
    }

    [Fact]
    public void Pedido_de_estilo_ainda_restringe_pelo_personagem()
    {
        var scope = Parse("mudar as falas do Kenta para o tom formal");

        Assert.Equal(["健太"], scope.Speakers);
        Assert.Equal(["a1", "a3", "b1"], scope.Select(Texts(), Characters, TestContext.Current.CancellationToken).Select(c => c.Unit.Id));
    }

    [Fact]
    public void Pedido_sem_filtro_nao_restringe_nada()
    {
        var scope = Parse("deixar tudo mais natural");

        Assert.False(scope.IsNarrowed);
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
}
