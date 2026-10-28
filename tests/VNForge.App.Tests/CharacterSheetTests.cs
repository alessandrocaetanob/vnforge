using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A ficha do personagem no Glossário (#469): gênero, como fala, cor, as falas mais longas, as linhas esperando revisão e o botão de revisar.</summary>
public class CharacterSheetTests
{
    private static TextUnit Line(int n, string source, string speaker, string? translation, TranslationStatus status) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, translation, status);

    private static async Task<GlossaryViewModel> OpenAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line(0, "あ", "美咲", "Ah.", TranslationStatus.Machine),
            Line(1, "おはようございます", "美咲", "Bom dia.", TranslationStatus.Machine),
            Line(2, "遅いぞ", "健太", null, TranslationStatus.Untranslated),
            Line(3, "今日もいい天気ですね", "美咲", "Que dia lindo hoje.", TranslationStatus.Translated),
            Line(4, "ええ", "美咲", "Sim.", TranslationStatus.Machine),
            Line(5, "それはどういう意味ですか", "美咲", "O que isso quer dizer?", TranslationStatus.Machine),
            Line(6, "なるほど、そうですか", "美咲", null, TranslationStatus.Untranslated),
            Line(7, "ありがとう", "美咲", "Obrigada.", TranslationStatus.Approved),
        ]);
        folder.SaveCharacters(
        [
            new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada, fala com educação", "violet"),
            new CharacterEntry("健太", "Kenta"),
        ]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PostToUi = action => action() };
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Abrir_a_ficha_mostra_genero_como_fala_cor_e_as_cinco_falas_mais_longas_nos_dois_idiomas()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));

        var sheet = Assert.IsType<CharacterSheetViewModel>(vm.Sheet);
        Assert.True(vm.HasSheet);
        Assert.Equal("Misaki", sheet.Name);
        Assert.Equal("美咲", sheet.SourceName);
        Assert.Equal("Feminino", sheet.GenderLabel);
        Assert.Equal("animada, fala com educação", sheet.ToneText);
        Assert.Same(CharacterPalette.Find("violet"), sheet.Color);
        Assert.Equal(
            ["それはどういう意味ですか", "今日もいい天気ですね", "なるほど、そうですか", "おはようございます", "ありがとう"],
            sheet.Lines.Select(l => l.Source));
        Assert.Equal("O que isso quer dizer?", sheet.Lines[0].TranslationText);
        Assert.Equal("Ainda sem tradução", sheet.Lines[2].TranslationText);
        Assert.False(sheet.Lines[2].HasTranslation);
    }

    [Fact]
    public async Task A_ficha_conta_quantas_linhas_da_IA_faltam_revisar()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));

        Assert.Equal(4, vm.Sheet!.AwaitingReview);
        Assert.Equal("4 linhas da IA esperando revisão.", vm.Sheet.AwaitingReviewText);
    }

    [Fact]
    public async Task Revisar_este_personagem_usa_o_pedido_do_menu_do_Glossario()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var requested = new List<string>();
        vm.ReviewCharacterRequested += (_, name) => requested.Add(name);
        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));

        Assert.True(vm.Sheet!.ReviewCommand.CanExecute(null));
        vm.Sheet.ReviewCommand.Execute(null);

        Assert.Equal(["美咲"], requested);
    }

    [Fact]
    public async Task Sem_linhas_esperando_revisao_o_botao_fica_desligado_e_o_texto_explica()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "健太"));

        var sheet = vm.Sheet!;
        Assert.False(sheet.ReviewCommand.CanExecute(null));
        Assert.Equal("Nenhuma linha da IA esperando revisão.", sheet.AwaitingReviewText);
        Assert.Equal("Kenta", sheet.Name);
        Assert.Equal("Não definido", sheet.GenderLabel);
        Assert.Contains("Ainda sem descrição", sheet.ToneText, StringComparison.Ordinal);
        Assert.Equal(CharacterPalette.Resolve(null, "健太"), sheet.Color);
        Assert.Equal(["遅いぞ"], sheet.Lines.Select(l => l.Source));
    }

    [Fact]
    public async Task A_ficha_acompanha_a_edicao_da_linha_na_tabela_e_fecha_ao_remover_o_personagem()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var row = vm.Characters.Single(c => c.Source == "美咲");
        vm.OpenSheetCommand.Execute(row);
        var changed = new List<string?>();
        vm.Sheet!.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Gender = CharacterGender.Other;
        row.Target = "Misa";

        Assert.Equal("Outro", vm.Sheet.GenderLabel);
        Assert.Equal("Misa", vm.Sheet.Name);
        Assert.NotEmpty(changed);

        vm.RemoveCharacterCommand.Execute(row);

        Assert.Null(vm.Sheet);
        Assert.False(vm.HasSheet);
    }

    [Fact]
    public async Task Fechar_a_ficha_e_recarregar_o_projeto_limpam_a_ficha_e_linha_em_branco_nao_abre()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        vm.OpenSheetCommand.Execute(new CharacterRow());
        Assert.Null(vm.Sheet);

        vm.OpenSheetCommand.Execute(vm.Characters[0]);
        vm.Sheet!.CloseCommand.Execute(null);
        Assert.Null(vm.Sheet);

        vm.OpenSheetCommand.Execute(vm.Characters[0]);
        vm.Reload();
        Assert.Null(vm.Sheet);
    }

    [Fact]
    public async Task Abrir_outra_ficha_troca_a_anterior()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));
        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "健太"));

        Assert.Equal("健太", vm.Sheet!.SourceName);
    }
}
