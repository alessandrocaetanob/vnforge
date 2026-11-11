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
}
