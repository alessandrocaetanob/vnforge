using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O painel de detalhe real, com o <see cref="TextboxPreview"/> medido e desenhado a partir do perfil do projeto.</summary>
public class TextboxPreviewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>, IDisposable
{
    private readonly TempFolder pasta = new();

    public void Dispose()
    {
        pasta.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Painel_desenha_a_caixa_e_acompanha_a_edicao_da_traducao()
    {
        var raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated);
        File.WriteAllText(
            ProjectFolder.Open(raiz).FormattingPath,
            """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        var vm = new EditorViewModel(clock: new FakeTimeProvider(), postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1400, Height = 900, Content = new EditorView { DataContext = vm } };
                window.Show();
                Assentar(window);

                var caixa = window.GetVisualDescendants().OfType<TextboxPreview>().Single();
                var estreita = caixa.DesiredSize;
                Assert.True(estreita.Width > 0 && estreita.Height > 0);
                Assert.Equal(["tradução 0"], caixa.Layout!.Boxes[0].Lines.Select(l => l.Text));

                vm.Detail!.Translation = "um dois tres quatro cinco";
                Assentar(window);

                Assert.Equal(["um dois", "tres", "quatro", "cinco"], caixa.Layout!.Boxes[0].Lines.Select(l => l.Text));
                Assert.True(caixa.DesiredSize.Height > estreita.Height);

                DesenharDeVerdade(caixa);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Voltar_a_tela_do_editor_depois_de_mudar_o_perfil_refaz_a_previa_da_linha_aberta()
    {
        var raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated);
        var arquivo = ProjectFolder.Open(raiz).FormattingPath;
        File.WriteAllText(arquivo, """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        var vm = new EditorViewModel(clock: new FakeTimeProvider(), postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        vm.Detail!.Translation = "um dois tres";
        Assert.Equal(2, vm.Detail.Preview!.Layout!.Boxes[0].Lines.Count);

        File.WriteAllText(arquivo, """{ "wrap": { "enabled": true, "maxWidth": 30, "maxLines": 2, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1400, Height = 900, Content = new EditorView { DataContext = vm } };
                window.Show();
                Assentar(window);

                Assert.Single(vm.Detail.Preview!.Layout!.Boxes[0].Lines);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Largura_segue_as_colunas_da_regua_e_nao_o_texto_cru()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var latino = Medir("abcd");
                var comMarcador = Medir("ab{VAR:nome}\\c");
                var japones = Medir("日本");
                var japonesComMarcador = Medir("日{EF:00}本");

                Assert.Equal(Medir("ab").Width, comMarcador.Width);
                Assert.Equal(latino.Width, japones.Width);
                Assert.Equal(latino.Width, japonesComMarcador.Width);
                Assert.True(latino.Width > Medir("ab").Width);
                Assert.Equal(latino.Height, japones.Height);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Render_percorre_marcador_glifo_largo_e_linha_em_excesso_sem_falhar()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var vm = new TextboxPreviewViewModel(
                    new TextUnit("a:1", TextUnitKind.Dialogue, "美咲", "原文", "x", TranslationStatus.Translated),
                    FormattingProfileSerializer.Parse("""{ "wrap": { "enabled": true, "maxWidth": 8, "maxLines": 1, "charWidth": "halfWidthLatin" } }"""),
                    "日本語 {VAR:nome} um dois tres\\cquatro");
                var caixa = new TextboxPreview { Layout = vm.Layout };
                caixa.Measure(Size.Infinity);
                caixa.Arrange(new Rect(caixa.DesiredSize));

                Assert.Contains(vm.Layout!.Boxes[0].Lines, l => l.Overflow);
                DesenharDeVerdade(caixa);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Fonte_padrao_e_a_MS_Gothic_do_sistema_ou_a_BIZ_UDGothic_embutida()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var instalada = FontManager.Current.SystemFonts.Any(f => f.Name is "MS Gothic" or "ＭＳ ゴシック");

                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new TextboxPreview().FontFamily), out var glifos));
                if (instalada)
                {
                    Assert.Contains(glifos.FamilyName, new[] { "MS Gothic", "ＭＳ ゴシック" });
                }
                else
                {
                    Assert.Contains(glifos.FamilyName, new[] { "BIZ UDGothic", "BIZ UDゴシック" });
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Fonte_embutida_resolve_sozinha_e_nao_deixa_glifo_faltando()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var embutida = new FontFamily("avares://VNForge.App/Assets/Fonts#BIZ UDGothic");
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(embutida), out var glifos));
                Assert.Contains(glifos.FamilyName, new[] { "BIZ UDGothic", "BIZ UDゴシック" });
                foreach (var letra in "áãçõÉ")
                {
                    Assert.True(glifos.CharacterToGlyphMap.TryGetGlyph(letra, out var glifo) && glifo != 0, $"sem glifo para {letra}");
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Fonte_padrao_tem_os_acentos_do_portugues_e_o_latim_mede_metade_do_japones()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var tipo = new Typeface(new TextboxPreview().FontFamily);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(tipo, out var glifos));
                foreach (var letra in "áãçõÉ")
                {
                    Assert.True(glifos.CharacterToGlyphMap.TryGetGlyph(letra, out var glifo) && glifo != 0, $"sem glifo para {letra}");
                }

                double Largura(string texto) => new Avalonia.Media.FormattedText(texto, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tipo, 20, Brushes.Black).Width;
                Assert.Equal(Largura("あ") / 2, Largura("a"), 0.5);
                Assert.Equal(Largura("a"), Largura("ã"), 0.5);
                Assert.Equal(Largura("a"), Largura("É"), 0.5);
            },
            CancellationToken.None);
    }

    private static Size Medir(string traducao)
    {
        var vm = new TextboxPreviewViewModel(
            new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "x", TranslationStatus.Translated),
            FormattingProfile.Default,
            traducao);
        var caixa = new TextboxPreview { Layout = vm.Layout };
        caixa.Measure(Size.Infinity);
        return caixa.DesiredSize;
    }

    /// <summary>Chama <c>Render</c> direto num contexto de desenho: uma exceção no desenho apareceria aqui.</summary>
    private static void DesenharDeVerdade(TextboxPreview caixa)
    {
        using var imagem = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)caixa.DesiredSize.Width), Math.Max(1, (int)caixa.DesiredSize.Height)));
        using var contexto = imagem.CreateDrawingContext();
        caixa.Render(contexto);
    }

    private static void Assentar(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
