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

    private static SpeakerNaming Nomes(string? cor = "rose") =>
        new(new CharacterBook([new CharacterEntry("美咲", "Misaki", Color: cor)]), TextEncoderRegistry.Default.Create("sjis"));

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

    private static readonly string[] SimilarNames = ["学生A", "学生I", "学生Q", "学生Y"];

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
    public async Task Abrir_a_tela_do_Glossario_com_personagens_sem_cor_nao_suja_o_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("健太", "Kenta", Color: "rose")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };
        vm.SelectedTab = GlossaryTab.Characters;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new Views.GlossaryView { DataContext = vm } };
                window.Show();
                ScreenHost.Settle(window);

                var combos = window.GetVisualDescendants().OfType<ComboBox>().Where(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Cor do personagem").ToList();
                Assert.Equal(2, combos.Count);
                Assert.Same(CharacterPalette.Resolve(null, "美咲"), combos[0].SelectedItem);
                Assert.Same(CharacterPalette.Find("rose"), combos[1].SelectedItem);
                Assert.False(vm.IsDirty);
                Assert.Null(vm.Characters[0].ColorKey);
                Assert.Equal(["Voltar à cor automática"], window.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Voltar à cor automática")
                    .Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
                window.Close();
            },
            CancellationToken.None);
    }
}
