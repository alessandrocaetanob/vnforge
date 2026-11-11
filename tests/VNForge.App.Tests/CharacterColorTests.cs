using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Screens;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A cor do personagem (#467): a paleta fixa com contraste nos dois temas, o Glossário que a troca e o nome pintado na grade, na placa da caixa do jogo e no Modo revisão.</summary>
public sealed class CharacterColorTests
{

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

    private static TextUnit Linha(string? falante) =>
        new("yst00001:000000", TextUnitKind.Dialogue, falante, "原文", "Bom dia!", TranslationStatus.Translated);

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
    }

    [Fact]
    public void Voltar_a_automatica_tira_a_cor_do_arquivo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", Color: "green")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };
        var row = vm.Characters[0];
        Assert.True(row.HasCustomColor);

        row.UseAutomaticColorCommand.Execute(null);

        Assert.False(row.HasCustomColor);
        Assert.Same(CharacterPalette.Resolve(null, "美咲"), row.Color);
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);
        Assert.Null(Assert.Single(folder.LoadCharacters()).Color);
        Assert.DoesNotContain("color", File.ReadAllText(folder.CharactersPath));
    }

    [Fact]
    public async Task Inspetor_e_Modo_revisao_trazem_a_cor_so_do_falante_cadastrado()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.Path, "Teste");
        var projeto = ProjectFolder.Open(root);
        projeto.SaveText("yst00001",
        [
            new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", "「おはよう」", "Bom dia.", TranslationStatus.Machine),
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, "犬井", "「うん」", "Aham.", TranslationStatus.Machine),
            new TextUnit("yst00001:000002", TextUnitKind.Narration, null, "雨が降っている。", "Chove.", TranslationStatus.Machine),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", Color: "teal")]);
        var vm = new EditorViewModel(postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", root);
        await vm.Loading;

        vm.SelectedLine = vm.Lines[0];
        Assert.Same(CharacterPalette.Find("teal"), vm.Detail!.SpeakerColor);
        Assert.Same(CharacterPalette.Find("teal"), vm.Lines[0].SpeakerColor);
        vm.SelectedLine = vm.Lines[1];
        Assert.Null(vm.Detail!.SpeakerColor);
        Assert.Null(vm.Lines[1].SpeakerColor);
        vm.SelectedLine = vm.Lines[2];
        Assert.Null(vm.Detail!.SpeakerColor);
        Assert.Null(vm.Lines[2].SpeakerColor);

        vm.SelectedLine = vm.Lines[0];
        vm.StartReview();
        Assert.Same(CharacterPalette.Find("teal"), vm.Review!.Detail!.SpeakerColor);
        vm.Review.SkipCommand.Execute(null);
        Assert.Null(vm.Review.Detail!.SpeakerColor);
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
}
