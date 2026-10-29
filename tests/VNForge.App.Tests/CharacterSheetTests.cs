using System.ComponentModel;
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
    private static TextUnit Line(int n, string source, string speaker, string? translation, TranslationStatus status, string? chosen = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, translation, status) { ChosenSpeaker = chosen };

    private static readonly IReadOnlyList<TextUnit> Units =
    [
        Line(0, "あ", "美咲", "Ah.", TranslationStatus.Machine),
        Line(1, "おはようございます", "美咲", "Bom dia.", TranslationStatus.Machine),
        Line(2, "遅いぞ", "健太", null, TranslationStatus.Untranslated),
        Line(3, "今日もいい天気ですね", "美咲", "Que dia lindo hoje.", TranslationStatus.Translated),
        Line(4, "ええ", "美咲", "Sim.", TranslationStatus.Machine),
        Line(5, "それはどういう意味ですか", "美咲", "O que isso quer dizer?", TranslationStatus.Machine),
        Line(6, "なるほど、そうですか", "美咲", null, TranslationStatus.Untranslated),
        Line(7, "ありがとう", "美咲", "Obrigada.", TranslationStatus.Approved),
    ];

    private static (GlossaryViewModel Vm, ProjectFolder Folder, string Root) Create(TempFolder temp, IReadOnlyList<TextUnit>? units = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", units ?? Units);
        folder.SaveCharacters(
        [
            new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada, fala com educação", "violet"),
            new CharacterEntry("健太", "Kenta"),
        ]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PostToUi = action => action() };
        return (vm, folder, root);
    }

    private static async Task<GlossaryViewModel> OpenAsync(TempFolder temp)
    {
        var (vm, _, _) = Create(temp);
        await vm.Loading;
        return vm;
    }

    private static async Task OpenSheetAsync(GlossaryViewModel vm, string source)
    {
        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == source));
        await vm.SheetLoading;
    }

    [Fact]
    public async Task Abrir_a_ficha_mostra_genero_como_fala_cor_e_as_cinco_falas_mais_longas_nos_dois_idiomas()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        await OpenSheetAsync(vm, "美咲");

        var sheet = Assert.IsType<CharacterSheetViewModel>(vm.Sheet);
        Assert.True(vm.HasSheet);
        Assert.Equal("Misaki", sheet.Name);
        Assert.Equal("美咲", sheet.SourceName);
        Assert.Equal("Feminino", sheet.GenderLabel);
        Assert.Equal("animada, fala com educação", sheet.ToneText);
        Assert.Same(CharacterPalette.Find("violet"), sheet.Color);
        Assert.Equal("7 falas", sheet.UsesText);
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

        await OpenSheetAsync(vm, "美咲");

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
        await OpenSheetAsync(vm, "美咲");

        Assert.True(vm.Sheet!.ReviewCommand.CanExecute(null));
        vm.Sheet.ReviewCommand.Execute(null);

        Assert.Equal(["美咲"], requested);
    }

    [Fact]
    public async Task Sem_linhas_esperando_revisao_o_botao_fica_desligado_e_o_texto_explica()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        await OpenSheetAsync(vm, "健太");

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
    public async Task A_ficha_le_o_texto_a_cada_abertura_e_acompanha_o_status_que_mudou_no_Editor()
    {
        using var temp = new TempFolder();
        var (vm, folder, _) = Create(temp);
        await vm.Loading;
        await OpenSheetAsync(vm, "美咲");
        Assert.Equal(4, vm.Sheet!.AwaitingReview);
        Assert.True(vm.Sheet.ReviewCommand.CanExecute(null));

        // Revisou tudo no Editor: as linhas da IA viram traduzidas.
        folder.SaveText("yst00001", [.. Units.Select(u => u.Status == TranslationStatus.Machine ? u with { Status = TranslationStatus.Translated } : u)]);
        await OpenSheetAsync(vm, "美咲");

        Assert.Equal(0, vm.Sheet!.AwaitingReview);
        Assert.False(vm.Sheet.ReviewCommand.CanExecute(null));

        // Traduziu em lote: o personagem que não tinha nada esperando agora tem.
        folder.SaveText("yst00001", [.. Units.Select(u => u.Speaker == "健太" ? u with { Translation = "Está atrasado!", Status = TranslationStatus.Machine } : u)]);
        await OpenSheetAsync(vm, "健太");

        Assert.Equal(1, vm.Sheet!.AwaitingReview);
        Assert.True(vm.Sheet.ReviewCommand.CanExecute(null));
        Assert.Equal("Está atrasado!", vm.Sheet.Lines[0].TranslationText);
    }

    [Fact]
    public async Task Antes_da_leitura_terminar_a_ficha_avisa_e_o_botao_fica_desligado()
    {
        using var temp = new TempFolder();
        var (vm, _, _) = Create(temp);
        await vm.Loading;

        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));
        var sheet = vm.Sheet!;

        if (sheet.State == SheetLoadState.Loading)
        {
            Assert.Equal("Lendo o texto do projeto…", sheet.AwaitingReviewText);
            Assert.Equal("…", sheet.UsesText);
            Assert.False(sheet.ReviewCommand.CanExecute(null));
            Assert.False(sheet.HasLines);
        }

        await vm.SheetLoading;
        Assert.Equal(SheetLoadState.Ready, sheet.State);
        Assert.True(sheet.HasLines);
    }

    [Fact]
    public async Task Se_o_texto_nao_puder_ser_lido_a_ficha_diz_isso_e_nao_que_o_personagem_nao_fala()
    {
        using var temp = new TempFolder();
        var (vm, folder, _) = Create(temp);
        await vm.Loading;
        File.WriteAllText(Path.Combine(folder.TextDirectory, "yst00001.json"), "{ isto não é json");

        await OpenSheetAsync(vm, "美咲");

        var sheet = vm.Sheet!;
        Assert.Equal(SheetLoadState.Failed, sheet.State);
        Assert.Equal("Não foi possível ler o texto do projeto.", sheet.LinesEmptyText);
        Assert.Equal("Não foi possível contar as linhas esperando revisão.", sheet.AwaitingReviewText);
        Assert.False(sheet.ReviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Contagem_e_lista_seguem_o_falante_escolhido_e_juntam_as_duas_larguras_do_nome()
    {
        using var temp = new TempFolder();
        var (vm, _, _) = Create(temp,
        [
            Line(0, "はい", "美咲", "Sim.", TranslationStatus.Machine),
            Line(1, "いいえ", "健太", "Não.", TranslationStatus.Machine, chosen: "美咲"),
            Line(2, "ええと", "美咲", null, TranslationStatus.Untranslated, chosen: string.Empty),
            Line(3, "長い長い長い台詞", "Ａ子", null, TranslationStatus.Untranslated),
            Line(4, "短い", "A子", null, TranslationStatus.Untranslated),
        ]);
        await vm.Loading;

        await OpenSheetAsync(vm, "美咲");

        Assert.Equal("2 falas", vm.Sheet!.UsesText);
        Assert.Equal(["いいえ", "はい"], vm.Sheet.Lines.Select(l => l.Source));
        Assert.Equal(2, vm.Sheet.AwaitingReview);

        vm.Characters.Add(new CharacterRow("A子", "Ako"));
        await OpenSheetAsync(vm, "A子");

        Assert.Equal("2 falas", vm.Sheet!.UsesText);
        Assert.Equal(["長い長い長い台詞", "短い"], vm.Sheet.Lines.Select(l => l.Source));
    }

    [Fact]
    public async Task Renomear_o_personagem_com_a_ficha_aberta_troca_as_falas_pelas_do_novo_nome()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var row = vm.Characters.Single(c => c.Source == "美咲");
        await OpenSheetAsync(vm, "美咲");

        row.Source = "健太";

        Assert.Equal(["遅いぞ"], vm.Sheet!.Lines.Select(l => l.Source));
        Assert.Equal(0, vm.Sheet.AwaitingReview);
        Assert.False(vm.Sheet.ReviewCommand.CanExecute(null));

        row.Source = "名無し";

        Assert.Empty(vm.Sheet.Lines);
        Assert.Equal("Nenhuma fala", vm.Sheet.UsesText);
        Assert.Equal("Este personagem ainda não fala em nenhuma linha do texto.", vm.Sheet.LinesEmptyText);
    }

    [Fact]
    public async Task Editar_a_linha_so_avisa_as_propriedades_curtas_e_as_falas_so_mudam_com_o_nome_no_jogo()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var row = vm.Characters.Single(c => c.Source == "美咲");
        await OpenSheetAsync(vm, "美咲");
        var lines = vm.Sheet!.Lines;
        var changed = new List<string?>();
        vm.Sheet.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Gender = CharacterGender.Other;
        row.Tone = "séria";
        row.Target = "Misa";

        Assert.Equal(["GenderLabel", "ToneText", "Name", "NameIsJapanese", "ShowsSourceName"], changed);
        Assert.Same(lines, vm.Sheet.Lines);
        Assert.Equal(("Outro", "séria", "Misa"), (vm.Sheet.GenderLabel, vm.Sheet.ToneText, vm.Sheet.Name));
    }

    [Fact]
    public async Task Remover_o_personagem_fecha_a_ficha_e_Fechar_ou_recarregar_o_projeto_tambem()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var row = vm.Characters.Single(c => c.Source == "美咲");
        await OpenSheetAsync(vm, "美咲");

        vm.RemoveCharacterCommand.Execute(row);
        Assert.Null(vm.Sheet);
        Assert.False(vm.HasSheet);

        vm.OpenSheetCommand.Execute(new CharacterRow());
        Assert.Null(vm.Sheet);

        await OpenSheetAsync(vm, "健太");
        vm.Sheet!.CloseCommand.Execute(null);
        Assert.Null(vm.Sheet);

        await OpenSheetAsync(vm, "健太");
        vm.Reload();
        Assert.Null(vm.Sheet);
    }

    [Fact]
    public async Task Trocar_de_projeto_com_a_ficha_aberta_a_fecha_sem_erro_mesmo_com_a_leitura_em_andamento()
    {
        using var temp = new TempFolder();
        var (vm, _, _) = Create(temp);
        await vm.Loading;
        vm.OpenSheetCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));
        var pending = vm.SheetLoading;

        using var other = new TempFolder();
        var otherRoot = SampleProjects.Create(other.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated);
        vm.Project = ProjectSummaryReader.Read(otherRoot);
        await pending;
        await vm.Loading;

        Assert.Null(vm.Sheet);
    }

    [Fact]
    public async Task A_linha_que_sai_do_filtro_da_busca_nao_fecha_a_ficha()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        await OpenSheetAsync(vm, "美咲");

        vm.Search = "Kenta";

        Assert.DoesNotContain(vm.VisibleCharacters, c => c.Source == "美咲");
        Assert.Equal("Misaki", vm.Sheet!.Name);
        Assert.True(vm.Sheet.HasLines);
    }

    [Fact]
    public async Task Abrir_outra_ficha_troca_a_anterior_e_solta_a_assinatura_da_linha()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        var misaki = vm.Characters.Single(c => c.Source == "美咲");
        await OpenSheetAsync(vm, "美咲");
        var first = vm.Sheet!;
        var changed = 0;
        first.PropertyChanged += (_, _) => changed++;

        await OpenSheetAsync(vm, "健太");
        misaki.Gender = CharacterGender.Other;

        Assert.Equal("健太", vm.Sheet!.SourceName);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void A_linha_nova_avisa_IsBlank_conforme_se_escreve_e_o_botao_da_ficha_segue()
    {
        var row = new CharacterRow();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.True(row.IsBlank);

        row.Source = "美咲";

        Assert.False(row.IsBlank);
        Assert.Contains(nameof(CharacterRow.IsBlank), changed);
        Assert.Equal("Abrir a ficha de 美咲", row.OpenSheetLabel);

        changed.Clear();
        row.Target = "Misaki";
        Assert.Contains(nameof(CharacterRow.OpenSheetLabel), changed);
        Assert.Equal("Abrir a ficha de Misaki", row.OpenSheetLabel);

        row.Source = string.Empty;
        row.Target = string.Empty;
        Assert.True(row.IsBlank);

        changed.Clear();
        row.Tone = "calmo";
        Assert.Contains(nameof(CharacterRow.IsBlank), changed);
        row.Tone = string.Empty;
        row.Gender = CharacterGender.Male;
        Assert.False(row.IsBlank);
    }
}
