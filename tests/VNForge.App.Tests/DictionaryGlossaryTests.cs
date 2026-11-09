using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Adicionar ao glossário" no cartão do dicionário (#538): a janela abre Glossário > Termos com a palavra do original numa linha nova.</summary>
public sealed class DictionaryGlossaryTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    [Fact]
    public async Task Pela_janela_o_botao_abre_o_Glossario_nos_Termos_com_a_palavra_preenchida_e_nao_duplica()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Amaoto no Hakoniwa", TranslationStatus.Machine);
        ProjectFolder.Open(root).SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", Frase, "Aizawa, os dois, atrasados.", TranslationStatus.Machine)]);
        var dicionario = await SyntheticDictionary.InstalledAsync(temp.SubFolder("dicionario"));
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Agora));
        var editor = new EditorViewModel(clock: new FixedClock(Agora)) { Dictionary = dicionario };
        var vm = new MainWindowViewModel(start) { Editor = editor };
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await editor.GoToLineAsync("yst00001", "yst00001:000000");
        vm.Glossary.SelectedTab = GlossaryTab.Characters;

        editor.Detail!.HoverSource(Frase.IndexOf("五分", StringComparison.Ordinal));
        Assert.True(editor.Detail.CanAddWordToGlossary);
        editor.Detail.AddWordToGlossaryCommand.Execute(null);

        Assert.Same(vm.Glossary, vm.CurrentPage);
        Assert.Equal(NavKey.Glossary, vm.SelectedNav?.Key);
        Assert.Equal(GlossaryTab.Terms, vm.Glossary.SelectedTab);
        var term = Assert.Single(vm.Glossary.Terms);
        Assert.Equal("五分", term.Source);
        Assert.Equal(string.Empty, term.Target);

        Assert.Same(term, vm.Glossary.RegisterTerm("五分"));
        Assert.Single(vm.Glossary.Terms);
    }
}
