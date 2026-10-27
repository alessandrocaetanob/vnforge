using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Glossário &gt; Personagens parte da lista do jogo (<c>ES.CHAR.NAME</c>) e dá uma só tradução a cada nome (#426).</summary>
public class GlossaryGameCharactersTests
{
    private static TextUnit Name(int n, string source, string? translation = null, string script = "yst00073") =>
        new($"{script}:{n:D6}", TextUnitKind.Name, null, source, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> OpenAsync(
        TempFolder temp, IReadOnlyList<TextUnit> names, IReadOnlyList<CharacterEntry>? characters = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [Line(0, "おはよう", "真弓"), Line(1, "やあ", "犬井")]);
        folder.SaveText("yst00073", names);
        if (characters is not null)
        {
            folder.SaveCharacters(characters);
        }

        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PostToUi = action => action() };
        await vm.Loading;
        return (vm, folder);
    }

    [Fact]
    public async Task Propoe_os_personagens_das_unidades_name_com_o_original_exato_e_sem_sujar_a_pagina()
    {
        using var temp = new TempFolder();

        var (vm, folder) = await OpenAsync(temp, [Name(0, "真弓"), Name(1, "犬井"), Name(2, "学生Ａ"), Name(3, "？？？"), Name(4, "真弓")]);

        Assert.Equal(["真弓", "犬井", "学生Ａ", "？？？"], vm.Characters.Select(c => c.Source));
        Assert.All(vm.Characters, c => Assert.Equal(string.Empty, c.Target));
        Assert.False(vm.IsDirty);
        Assert.False(File.Exists(folder.CharactersPath));
        Assert.Equal(4, vm.CharacterCount);
    }

    [Fact]
    public async Task Nao_repete_quem_ja_esta_cadastrado_e_nao_mexe_na_linha_dele()
    {
        using var temp = new TempFolder();

        var (vm, _) = await OpenAsync(
            temp,
            [Name(0, "真弓"), Name(1, "犬井")],
            [new CharacterEntry("犬井", "Inui", CharacterGender.Male, "seco")]);

        Assert.Equal(["犬井", "真弓"], vm.Characters.Select(c => c.Source));
        var inui = vm.Characters[0];
        Assert.Equal(("Inui", CharacterGender.Male, "seco"), (inui.Target, inui.Gender, inui.Tone));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Nome_ja_traduzido_na_unidade_chega_ao_personagem()
    {
        using var temp = new TempFolder();

        var (vm, _) = await OpenAsync(
            temp,
            [Name(0, "真弓", "Mayumi"), Name(1, "犬井", "Inui")],
            [new CharacterEntry("犬井", string.Empty, CharacterGender.Male)]);

        Assert.Equal("Inui", vm.Characters.Single(c => c.Source == "犬井").Target);
        Assert.Equal("Mayumi", vm.Characters.Single(c => c.Source == "真弓").Target);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Nao_sobrescreve_a_traducao_que_o_personagem_ja_tem()
    {
        using var temp = new TempFolder();

        var (vm, _) = await OpenAsync(temp, [Name(0, "真弓", "Outra")], [new CharacterEntry("真弓", "Mayumi")]);

        Assert.Equal("Mayumi", vm.Characters.Single().Target);
    }

    [Fact]
    public async Task Salvar_a_traducao_do_personagem_grava_a_mesma_nas_unidades_name_do_mesmo_original()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, [Name(0, "真弓"), Name(1, "犬井"), Name(2, "真弓")]);
        var changed = 0;
        vm.LinesChanged += (_, _) => changed++;

        vm.Characters.Single(c => c.Source == "真弓").Target = "Mayumi";
        Assert.True(vm.Flush());

        var units = folder.LoadText("yst00073");
        Assert.Equal(["Mayumi", null, "Mayumi"], units.Select(u => u.Translation));
        Assert.Equal(TranslationStatus.Translated, units[0].Status);
        Assert.Equal("Mayumi", folder.LoadCharacters().Single(c => c.Source == "真弓").Target);
        Assert.Null(folder.LoadText("yst00001").First().Translation);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Salvar_sem_mudar_nenhuma_traducao_nao_avisa_as_linhas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, [Name(0, "真弓", "Mayumi")], [new CharacterEntry("真弓", "Mayumi")]);
        var changed = 0;
        vm.LinesChanged += (_, _) => changed++;

        vm.Characters[0].Gender = CharacterGender.Female;
        Assert.True(vm.Flush());

        Assert.Equal(0, changed);
        Assert.Equal(CharacterGender.Female, vm.Characters[0].Gender);
    }
}
