using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Cadastrar personagem" no card do erro <c>falante-nao-codificavel</c>: o nome vai para Glossário > Personagens exatamente como foi extraído.</summary>
public class ConferenceSpeakerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>O <c>C</c> largo (U+FF23) parece o latino, mas é outro caractere: digitar o nome de cabeça não casa com o cadastro.</summary>
    private const string Wide = "Ｃ氏";

    private static ProjectFolder CreateProject(TempFolder temp, params TextUnit[] units)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("vn"), new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Teste", "Teste.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveText("yst00001", units);
        return folder;
    }

    private static TextUnit Unit(int n, string? speaker, string translation = "Bom dia!") =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, $"原文{n}", translation, TranslationStatus.Translated);

    private static async Task<ConferenceViewModel> Loaded(TempFolder temp, params TextUnit[] units)
    {
        var folder = CreateProject(temp, units);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();
        return vm;
    }

    [Fact]
    public async Task O_erro_do_falante_guarda_o_nome_como_foi_extraido_e_pode_cadastrar()
    {
        using var temp = new TempFolder();
        var vm = await Loaded(temp, Unit(0, Wide));

        var row = Assert.Single(vm.Rows);
        Assert.Equal(SpeakerEncodingRule.RuleId, row.RuleId);
        Assert.True(row.HasSpeaker);
        Assert.Equal(Wide, row.Speaker);
        Assert.True(row.CanRegisterCharacter);
    }

    [Fact]
    public async Task Cadastrar_personagem_pede_o_nome_exato_sem_aparar_nem_normalizar()
    {
        using var temp = new TempFolder();
        var vm = await Loaded(temp, Unit(0, Wide));
        string? requested = null;
        vm.CharacterRegistrationRequested += (_, name) => requested = name;

        Assert.True(vm.RegisterCharacterCommand.CanExecute(vm.Rows[0]));
        vm.RegisterCharacterCommand.Execute(vm.Rows[0]);

        Assert.Equal(Wide, requested);
        Assert.NotEqual("C氏", requested);
    }

    [Fact]
    public async Task Outro_problema_com_falante_mostra_o_nome_mas_nao_oferece_cadastro()
    {
        using var temp = new TempFolder();
        var vm = await Loaded(temp, Unit(0, "Misaki", "duas  palavras"));
        string? requested = null;
        vm.CharacterRegistrationRequested += (_, name) => requested = name;

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Misaki", row.Speaker);
        Assert.False(row.CanRegisterCharacter);
        Assert.False(vm.RegisterCharacterCommand.CanExecute(row));
        vm.RegisterCharacterCommand.Execute(row);
        Assert.Null(requested);
    }

    [Fact]
    public async Task Narracao_nao_tem_falante()
    {
        using var temp = new TempFolder();
        var vm = await Loaded(temp, Unit(0, null, "duas  palavras"));

        var row = Assert.Single(vm.Rows);
        Assert.False(row.HasSpeaker);
        Assert.Null(row.Speaker);
        Assert.False(row.CanRegisterCharacter);
    }

    [Fact]
    public async Task Pela_janela_o_botao_abre_o_Glossario_nos_Personagens_com_o_nome_preenchido()
    {
        using var temp = new TempFolder();
        var folder = CreateProject(temp, Unit(0, Wide));
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(folder.Root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        vm.Glossary.SelectedTab = GlossaryTab.Terms;

        vm.Conference.RegisterCharacterCommand.Execute(vm.Conference.Rows[0]);

        Assert.Same(vm.Glossary, vm.CurrentPage);
        Assert.Equal(NavKey.Glossary, vm.SelectedNav?.Key);
        Assert.Equal(GlossaryTab.Characters, vm.Glossary.SelectedTab);
        var character = Assert.Single(vm.Glossary.Characters);
        Assert.Equal(Wide, character.Source);
        Assert.Equal(string.Empty, character.Target);
    }
}
