using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Trocar o falante de uma linha no Editor: o seletor do inspetor, a marca na grade e o que vai para o arquivo.</summary>
public sealed class LineSpeakerTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineSpeakerTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, null, "雨が降っている。\\p", "Chovia.\\p", TranslationStatus.Machine),
            Fala(2, "健太", "「うん」\\p", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "alegre"), new CharacterEntry("健太", "Kenta")]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst00001:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private static SpeakerChoice Escolha(LineDetailViewModel detail, string? valor) => detail.SpeakerChoices.Single(c => c.Value == valor);

    [Fact]
    public async Task O_seletor_oferece_o_falante_do_jogo_Narracao_e_os_personagens_do_Glossario()
    {
        var vm = await Abrir();

        var detail = vm.Detail!;

        Assert.Equal(["Falante do jogo (美咲)", "Narração", "Misaki · 美咲", "Kenta · 健太"], detail.SpeakerChoices.Select(c => c.Label));
        Assert.Equal([null, string.Empty, "美咲", "健太"], detail.SpeakerChoices.Select(c => c.Value));
        Assert.Equal(detail.SpeakerChoices[0], detail.SelectedSpeakerChoice);
        Assert.Equal("Falante: 美咲 (do jogo)", detail.SpeakerSummary);
        Assert.False(detail.IsSpeakerChanged);
    }

    [Fact]
    public async Task Escolher_um_personagem_grava_so_nessa_linha_e_atualiza_a_grade_o_contexto_e_o_resumo()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var detail = vm.Detail!;

        detail.SelectedSpeakerChoice = Escolha(detail, "美咲");

        Assert.Equal("美咲", Gravada(2).ChosenSpeaker);
        Assert.Equal("健太", Gravada(2).Speaker);
        Assert.Null(Gravada(0).ChosenSpeaker);
        Assert.Equal("Falante trocado: 美咲 (no jogo: 健太)", detail.SpeakerSummary);
        Assert.Equal("美咲 → Misaki", detail.SpeakerPair);
        Assert.Equal("alegre", detail.Voice);
        Assert.True(detail.IsSpeakerChanged);
        Assert.Equal(Escolha(detail, "美咲"), detail.SelectedSpeakerChoice);
        var linha = vm.Lines[2];
        Assert.True(linha.IsSpeakerChanged);
        Assert.Equal("Misaki", linha.SpeakerName);
        Assert.Equal("Falante trocado nesta linha. No jogo: 健太.", linha.SpeakerChangedTip);
        Assert.False(vm.Lines[0].IsSpeakerChanged);
        Assert.Null(detail.ErrorMessage);
    }

    [Fact]
    public async Task O_menu_do_nome_copia_o_falante_trocado_e_a_narracao_fica_sem_menu()
    {
        var vm = await Abrir();
        var detail = vm.Detail!;
        Assert.Equal("美咲", vm.Lines[0].SpeakerOriginal);

        detail.SelectedSpeakerChoice = Escolha(detail, "健太");
        Assert.Equal("健太", vm.Lines[0].SpeakerOriginal);
        Assert.Equal("Kenta", vm.Lines[0].SpeakerName);

        detail.SelectedSpeakerChoice = Escolha(detail, string.Empty);
        Assert.Null(vm.Lines[0].SpeakerOriginal);
    }

    [Fact]
    public async Task Narracao_tira_o_nome_da_linha_e_o_falante_do_jogo_volta_a_ele()
    {
        var vm = await Abrir();
        var detail = vm.Detail!;

        detail.SelectedSpeakerChoice = Escolha(detail, string.Empty);

        Assert.Equal(string.Empty, Gravada(0).ChosenSpeaker);
        Assert.Equal("美咲", Gravada(0).Speaker);
        Assert.True(vm.Lines[0].IsNarration);
        Assert.Equal("Narração", vm.Lines[0].SpeakerLabel);
        Assert.Null(detail.Speaker);
        Assert.Equal("Falante trocado: Narração (no jogo: 美咲)", detail.SpeakerSummary);

        detail.UseGameSpeakerCommand.Execute(null);

        Assert.Null(Gravada(0).ChosenSpeaker);
        Assert.False(vm.Lines[0].IsNarration);
        Assert.Equal("Misaki", vm.Lines[0].SpeakerName);
        Assert.False(vm.Lines[0].IsSpeakerChanged);
        Assert.Equal(detail.SpeakerChoices[0], detail.SelectedSpeakerChoice);
    }

    [Fact]
    public async Task Uma_linha_de_narracao_pode_ganhar_falante_com_um_nome_digitado()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[1];
        var detail = vm.Detail!;
        Assert.False(detail.UseSpeakerDraftCommand.CanExecute(null));

        detail.SpeakerDraft = "  ???  ";
        Assert.True(detail.UseSpeakerDraftCommand.CanExecute(null));
        detail.UseSpeakerDraftCommand.Execute(null);

        Assert.Equal("???", Gravada(1).ChosenSpeaker);
        Assert.Null(Gravada(1).Speaker);
        Assert.Equal(string.Empty, detail.SpeakerDraft);
        Assert.Equal("???", vm.Lines[1].SpeakerName);
        Assert.Null(detail.SelectedSpeakerChoice);
        Assert.Equal("Falante trocado: ??? (no jogo: Narração)", detail.SpeakerSummary);
    }

    [Fact]
    public async Task Trocar_o_falante_nao_apaga_a_traducao_digitada_e_ainda_nao_gravada()
    {
        var vm = await Abrir();
        var detail = vm.Detail!;
        detail.FieldText = "Bom dia, pessoal.";

        detail.SelectedSpeakerChoice = Escolha(detail, string.Empty);

        var gravada = Gravada(0);
        Assert.StartsWith("Bom dia, pessoal.", gravada.Translation, StringComparison.Ordinal);
        Assert.Equal(string.Empty, gravada.ChosenSpeaker);
    }

    [Fact]
    public async Task A_caixa_de_texto_do_inspetor_mostra_o_falante_escolhido()
    {
        var vm = await Abrir();
        var detail = vm.Detail!;
        detail.AttachPreview(ProjectFolder.Open(raiz).LoadFormatting());
        Assert.Equal("美咲", detail.Preview!.Layout!.Speaker);

        detail.SelectedSpeakerChoice = Escolha(detail, "健太");

        Assert.Equal("健太", detail.Preview!.Layout!.Speaker);
    }

    [Fact]
    public void A_linha_da_grade_marca_o_falante_trocado_e_leva_o_nome_do_cadastro()
    {
        var unidade = Fala(0, "美咲", "「おはよう」", "Oi.", TranslationStatus.Machine);
        var linha = new LineViewModel(EditorFixtures.Lines(("yst00001", [unidade])).LinesOf("yst00001").Single(), "Misaki");
        Assert.False(linha.IsSpeakerChanged);
        Assert.Null(linha.SpeakerChangedTip);

        var trocada = unidade with { ChosenSpeaker = "健太" };
        linha.Update(EditorFixtures.Lines(("yst00001", [trocada])).LinesOf("yst00001").Single(), "Kenta");

        Assert.True(linha.IsSpeakerChanged);
        Assert.Equal("Kenta", linha.SpeakerName);
    }
}

/// <summary>A marca de "falante trocado" na linha da grade.</summary>
public sealed class LineSpeakerRowHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Fact]
    public async Task A_linha_da_grade_acende_a_marca_de_falante_trocado()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var row = new VNForge.App.Controls.LineRow { Number = 3, Speaker = "Misaki", SpeakerChanged = true, SpeakerTip = "No jogo: 健太." };

                Assert.Contains(":speaker-changed", row.Classes);
                Assert.Equal("Linha 0003, falante trocado", Avalonia.Automation.AutomationProperties.GetName(row));

                row.SpeakerChanged = false;

                Assert.DoesNotContain(":speaker-changed", row.Classes);
                Assert.Equal("Linha 0003", Avalonia.Automation.AutomationProperties.GetName(row));
            },
            CancellationToken.None);
    }
}
