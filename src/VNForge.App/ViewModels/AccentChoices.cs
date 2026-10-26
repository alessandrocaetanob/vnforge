using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// Os três cartões de acentos, iguais no passo "Acentos" do Novo projeto e na aba "Acentos" dos Ajustes: os mesmos nomes, a mesma
/// explicação e a mesma frase na caixa do jogo. O <c>sjis</c> não vira cartão.
/// </summary>
public static class AccentChoices
{
    public const string Fold = "fold";

    public const string Remap = "remap";

    public const string AdjustInGame = "cp1252";

    public const string RecommendedBadge = "Recomendada para este jogo";

    /// <summary>Os ids que viram cartão, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> CardIds { get; } = [Fold, Remap, AdjustInGame];

    public static ChoiceGroup CreateGroup(string selectedId = ProjectChoices.DefaultEncoding) =>
        new(ProjectChoices.EncodingStrategies.Where(CardIds.Contains).Select(Option), selectedId);

    /// <summary>Põe ou tira o selo "Recomendada para este jogo" do cartão de ajuste no jogo: vale quando a engine tem patch de executável.</summary>
    public static void Recommend(ChoiceGroup group, IEngine? engine) =>
        group.Options.Single(o => o.Id == AdjustInGame).Badge = engine is IGamePatchProvider ? RecommendedBadge : string.Empty;

    public static ChoiceOption Option(string id) => id switch
    {
        Fold => new(id, "Sem acentos", description: "Funciona em qualquer jogo. As letras acentuadas viram as sem acento, como ação que vira acao.") { Layout = AccentPreview.Layout(id) },
        Remap => new(id, "Com acentos, fonte modificada", description: "Troca a fonte do jogo para desenhar as letras acentuadas. Ainda não está pronta.") { Layout = AccentPreview.Layout(id), IsAvailable = false, Badge = "Em breve" },
        AdjustInGame => new(id, "Com acentos, ajuste no jogo", description: "Mantém todos os acentos ajustando o jogo, sempre com cópia de segurança. Trocar a fonte para MS Gothic é opcional, tira os vãos entre as letras e se faz na tela Testar.") { Layout = AccentPreview.Layout(id) },
        _ => new(id, id),
    };
}
