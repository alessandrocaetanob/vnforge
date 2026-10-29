using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A IA aprende com as correções (#473): a troca que a pessoa repete vira uma oferta de regra para o estilo ou para o "Como fala"; nada chama a IA.</summary>
public sealed class CorrectionOfferTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string root;

    public CorrectionOfferTests()
    {
        root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00001", TranslationStatus.Translated)]);
    }

    public void Dispose() => temp.Dispose();

    private static TextUnit Edited(int index, string speaker, string ai, string person) =>
        new($"yst00001:{index:D6}", TextUnitKind.Dialogue, speaker, $"原文{index}", person, TranslationStatus.Translated) { AiTranslation = ai };

    private static IReadOnlyList<RepeatedCorrection> Found(params TextUnit[] lines) =>
        CorrectionLearner.Find(lines.Select(l => ("yst00001", l)));

    private async Task<MainWindowViewModel> OpenAsync(params TextUnit[] lines)
    {
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("健太", "Kenta", Tone: "direto."), new CharacterEntry("美咲", "Misaki")]);
        folder.SaveText("yst00001", lines);
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        main.Toasts.Clear();
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        await main.Glossary.Loading;
        return main;
    }

    private static TextUnit[] KentaEdits() =>
    [
        Edited(0, "健太", "Ei, vem.", "Ô, vem."),
        Edited(1, "健太", "Ei, anda.", "Ô, anda."),
        Edited(2, "健太", "Ei, olha!", "Ô, olha!"),
    ];

    [Fact]
    public async Task Troca_repetida_de_um_personagem_oferece_o_Como_fala_e_aceitar_grava_no_glossario()
    {
        var main = await OpenAsync(KentaEdits());

        await main.OfferCorrectionsAsync(root);

        var offer = Assert.Single(main.Toasts.Toasts, t => t.Key == "correction-offer");
        Assert.Contains("“Ei” por “Ô” em 3 falas de Kenta", offer.Message, StringComparison.Ordinal);
        Assert.Equal("Adicionar ao Como fala", offer.UndoLabel);

        offer.UndoCommand.Execute(null);

        var tone = main.Glossary.ToneOf("健太");
        Assert.Equal("direto; diz \"Ô\" no lugar de \"Ei\"", tone);
        Assert.Contains(main.Toasts.Toasts, t => t.Key == "correction-offer" && t.Message.StartsWith("Regra adicionada ao Como fala de Kenta", StringComparison.Ordinal));
        Assert.True(main.Glossary.Flush());
        Assert.Equal(tone, ProjectFolder.Open(root).LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    [Fact]
    public async Task Troca_espalhada_pelo_projeto_oferece_o_guia_de_estilo_e_aceitar_acrescenta_ao_style_md()
    {
        var main = await OpenAsync(
            Edited(0, "健太", "Ele disse oi.", "Ele falou oi."),
            Edited(1, "美咲", "Ela disse tchau.", "Ela falou tchau."),
            Edited(2, "健太", "Eu disse não.", "Eu falou não."));

        await main.OfferCorrectionsAsync(root);

        var offer = Assert.Single(main.Toasts.Toasts, t => t.Key == "correction-offer");
        Assert.Contains("“disse” por “falou” em 3 falas. Adicionar ao guia de estilo?", offer.Message, StringComparison.Ordinal);

        offer.UndoCommand.Execute(null);

        Assert.Contains("- Use \"falou\" no lugar de \"disse\".", main.Settings.StyleGuide, StringComparison.Ordinal);
        var path = Path.Combine(root, "style.md");
        Assert.True(File.Exists(path));
        Assert.Contains("Use \"falou\" no lugar de \"disse\".", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abaixo_do_limite_nada_e_oferecido()
    {
        var main = await OpenAsync(KentaEdits()[..2]);

        await main.OfferCorrectionsAsync(root);

        Assert.DoesNotContain(main.Toasts.Toasts, t => t.Key == "correction-offer");
    }

    [Fact]
    public async Task A_mesma_regra_so_e_oferecida_uma_vez_na_sessao()
    {
        var main = await OpenAsync(KentaEdits());

        await main.OfferCorrectionsAsync(root);
        main.Toasts.Clear();
        await main.OfferCorrectionsAsync(root);

        Assert.DoesNotContain(main.Toasts.Toasts, t => t.Key == "correction-offer");
    }

    [Fact]
    public async Task Regra_que_ja_esta_no_Como_fala_nao_e_oferecida_de_novo()
    {
        var main = await OpenAsync(KentaEdits());
        Assert.True(main.Glossary.AddToneRule("健太", Found(KentaEdits()).Single()));

        await main.OfferCorrectionsAsync(root);

        Assert.DoesNotContain(main.Toasts.Toasts, t => t.Key == "correction-offer");
    }

    [Fact]
    public async Task Personagem_fora_do_Glossario_nao_recebe_oferta_nem_regra()
    {
        TextUnit[] lines = [Edited(0, "学生", "Ei, a.", "Ô, a."), Edited(1, "学生", "Ei, b.", "Ô, b."), Edited(2, "学生", "Ei, c.", "Ô, c.")];
        var main = await OpenAsync(lines);

        await main.OfferCorrectionsAsync(root);

        Assert.DoesNotContain(main.Toasts.Toasts, t => t.Key == "correction-offer");
        Assert.False(main.Glossary.AddToneRule("学生", Found(lines).Single()));
    }

    [Fact]
    public void Toast_de_oferta_fica_mais_tempo_na_tela_que_o_aviso_comum()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var toasts = new ToastService(clock, a => a());

        var offer = toasts.Show("Adicionar?", ToastKind.Info, lifetime: TimeSpan.FromSeconds(20));
        toasts.Show("Salvo.", ToastKind.Success);
        clock.Advance(ToastService.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Equal([offer], toasts.Toasts);
    }
}
