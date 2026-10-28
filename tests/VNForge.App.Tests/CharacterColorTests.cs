using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A cor do personagem (#467): a paleta fixa com contraste nos dois temas, o Glossário que a troca e o nome pintado na grade, na placa da caixa do jogo e no Modo revisão.</summary>
public sealed class CharacterColorTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Perfil = """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""";

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo)
        {
            (hi, lo) = (lo, hi);
        }

        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Uma cor de Tokens.axaml: o primeiro valor é o do tema escuro e o segundo, o do claro (a ordem do arquivo gerado).</summary>
    private static Color Token(string name, bool dark)
    {
        var axaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design", "Tokens.axaml"));
        var hits = Regex.Matches(axaml, $@"x:Key=""Vnf\.{name}Color"">(#[0-9A-Fa-f]{{6}})<");
        return Color.Parse(hits[dark ? 0 : 1].Groups[1].Value);
    }

    [Fact]
    public void Paleta_tem_ids_unicos_e_ao_menos_cinco_cores_para_uma_cena_cheia()
    {
        Assert.True(CharacterPalette.All.Count >= 5);
        Assert.Equal(CharacterPalette.All.Count, CharacterPalette.All.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CharacterPalette.All.Count, CharacterPalette.All.Select(c => c.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("BgBase")]
    [InlineData("BgLayer")]
    [InlineData("BgCard")]
    [InlineData("BgCardHover")]
    [InlineData("BgInset")]
    [InlineData("AccentSoft")]
    public void Cada_cor_passa_de_4_5_para_1_nos_fundos_de_cada_tema(string fundo)
    {
        foreach (var cor in CharacterPalette.All)
        {
            Assert.True(Contrast(cor.Light, Token(fundo, dark: false)) >= 4.5, $"{cor.Key} no tema claro sobre {fundo}");
            Assert.True(Contrast(cor.Dark, Token(fundo, dark: true)) >= 4.5, $"{cor.Key} no tema escuro sobre {fundo}");
        }
    }

    [Fact]
    public void Versao_escura_passa_de_4_5_para_1_na_placa_da_caixa_do_jogo_nos_dois_temas()
    {
        foreach (var cor in CharacterPalette.All)
        {
            Assert.True(Contrast(cor.Dark, Token("TextboxPlate", dark: true)) >= 4.5, cor.Key);
            Assert.True(Contrast(cor.Dark, Token("TextboxPlate", dark: false)) >= 4.5, cor.Key);
        }
    }

    [Fact]
    public void Sem_cor_gravada_o_app_escolhe_pelo_nome_e_a_escolha_nao_depende_da_ordem()
    {
        var a = CharacterPalette.Resolve(null, "美咲");

        Assert.Same(a, CharacterPalette.Resolve(null, "美咲"));
        Assert.Same(a, CharacterPalette.Resolve(null, " 美咲 "));
        Assert.Same(CharacterPalette.Resolve(null, "学生Ｃ"), CharacterPalette.Resolve(null, "学生C"));
        Assert.Contains(a, CharacterPalette.All);
    }

    [Fact]
    public void Cor_gravada_vale_sobre_a_automatica_e_id_desconhecido_cai_na_automatica()
    {
        var automatica = CharacterPalette.Resolve(null, "美咲");
        var outra = CharacterPalette.All.First(c => c != automatica);

        Assert.Same(outra, CharacterPalette.Resolve(outra.Key, "美咲"));
        Assert.Same(automatica, CharacterPalette.Resolve("neon", "美咲"));
        Assert.Null(CharacterPalette.For(null));
    }

    private static SpeakerNaming Nomes(string? cor = "rose") =>
        new(new CharacterBook([new CharacterEntry("美咲", "Misaki", Color: cor)]), TextEncoderRegistry.Default.Create("sjis"));

    private static TextUnit Linha(string? falante) =>
        new("yst00001:000000", TextUnitKind.Dialogue, falante, "原文", "Bom dia!", TranslationStatus.Translated);

    [Fact]
    public void Placa_da_caixa_leva_a_cor_do_personagem_e_a_narracao_nao_tem()
    {
        var perfil = FormattingProfileSerializer.Parse(Perfil);

        Assert.Same(CharacterPalette.Find("rose"), new TextboxPreviewViewModel(Linha("美咲"), perfil, "Oi", naming: Nomes()).Layout!.SpeakerColor);
        Assert.Null(new TextboxPreviewViewModel(Linha(null), perfil, "Oi", naming: Nomes()).Layout!.SpeakerColor);
        Assert.Null(new TextboxPreviewViewModel(Linha("犬井"), perfil, "Oi", naming: Nomes()).Layout!.SpeakerColor);
    }

    [Fact]
    public void Contexto_da_linha_e_a_linha_da_grade_trazem_a_cor_so_de_quem_esta_cadastrado()
    {
        var contexto = new EditorContext(new CharacterBook([new CharacterEntry("美咲", "Misaki", Color: "teal")]), []);

        Assert.Same(CharacterPalette.Find("teal"), contexto.For("美咲", "原文").SpeakerColor);
        Assert.Null(contexto.For("犬井", "原文").SpeakerColor);
        Assert.Null(contexto.For(null, "原文").SpeakerColor);

        var entry = new LineEntry("yst00001", 1, Linha("美咲"), []);
        var line = new LineViewModel(entry, "Misaki", CharacterPalette.Find("teal"));
        Assert.Same(CharacterPalette.Find("teal"), line.SpeakerColor);
        Assert.Null(new LineViewModel(new LineEntry("yst00001", 1, Linha(null), []), null, CharacterPalette.Find("teal")).SpeakerColor);
    }

    [Fact]
    public void Linha_da_grade_avisa_quando_o_glossario_muda_a_cor_e_fica_quieta_quando_nao_muda()
    {
        var line = new LineViewModel(new LineEntry("yst00001", 1, Linha("美咲"), []), "Misaki", CharacterPalette.Find("teal"));
        var changed = new List<string?>();
        line.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        line.UpdateSpeaker("Misaki", CharacterPalette.Find("teal"));
        Assert.Empty(changed);

        line.UpdateSpeaker("Misaki", CharacterPalette.Find("rose"));
        Assert.Contains(nameof(LineViewModel.SpeakerColor), changed);
        Assert.Same(CharacterPalette.Find("rose"), line.SpeakerColor);
    }

    [Fact]
    public void Glossario_grava_a_cor_escolhida_e_deixa_de_fora_a_que_o_app_escolhe()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", Color: "rose"), new CharacterEntry("健太", "Kenta")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };

        Assert.Same(CharacterPalette.Find("rose"), vm.Characters[0].Color);
        Assert.Same(CharacterPalette.Resolve(null, "健太"), vm.Characters[1].Color);
        Assert.False(vm.IsDirty);

        vm.Characters[0].Color = CharacterPalette.Find("green")!;
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);

        Assert.Equal(
            [new CharacterEntry("美咲", "Misaki", Color: "green"), new CharacterEntry("健太", "Kenta")],
            folder.LoadCharacters());
        Assert.Equal(CharacterPalette.All, vm.Colors);
    }

    [Fact]
    public async Task Nome_da_linha_leva_a_cor_nos_dois_temas_e_a_narracao_e_o_falante_trocado_ficam_sem_ela()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var teal = CharacterPalette.Find("teal")!;
                var list = new StackPanel();
                var colored = new LineRow { Number = 1, Speaker = "Misaki", SpeakerColor = teal, Original = "原文" };
                var narration = new LineRow { Number = 2, SpeakerColor = teal, Original = "原文" };
                var changed = new LineRow { Number = 3, Speaker = "Misaki", SpeakerChanged = true, SpeakerColor = teal, Original = "原文" };
                list.Children.Add(colored);
                list.Children.Add(narration);
                list.Children.Add(changed);
                var window = new Window { Width = 900, Height = 300, Content = list, RequestedThemeVariant = ThemeVariant.Light };
                window.Show();
                ScreenRenderer.Settle(window);

                static SelectableTextBlock NameOf(LineRow row) => row.GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Name == "PART_Speaker");

                Assert.Same(teal.LightBrush, NameOf(colored).Foreground);
                Assert.NotSame(teal.LightBrush, NameOf(narration).Foreground);
                Assert.NotSame(teal.LightBrush, NameOf(changed).Foreground);

                window.RequestedThemeVariant = ThemeVariant.Dark;
                ScreenRenderer.Settle(window);
                Assert.Same(teal.DarkBrush, NameOf(colored).Foreground);

                colored.SpeakerColor = null;
                Assert.NotSame(teal.DarkBrush, NameOf(colored).Foreground);
                window.Close();
            },
            CancellationToken.None);
    }
}
