using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Glossary;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba Tratamentos do Glossário (#470): a tabela, o salvamento junto com os outros arquivos e as sugestões da IA.</summary>
[Trait("Risco", "R2")]
[Trait("Risco", "R7")]
public class AddressTermsViewModelTests
{
    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> OpenAsync(
        TempFolder temp, IGlossarySuggester? suggester = null, FakeTimeProvider? clock = null, IReadOnlyList<AddressTerm>? terms = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line(0, "「美咲ちゃん、おはよう」", "健太"),
            Line(1, "「先輩、遅刻です」", "美咲"),
            Line(2, "「静かに」", "先生"),
            Line(3, "narração"),
        ]);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("健太", "Kenta"), new CharacterEntry("先生", "Professor")]);
        if (terms is not null)
        {
            folder.SaveAddressTerms(terms);
        }

        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), Suggester = suggester, AutosaveClock = clock, PostToUi = action => action() };
        await vm.Loading;
        return (vm, folder);
    }


    [Fact]
    public async Task Arquivo_ilegivel_trava_a_edicao_e_nao_e_sobrescrito()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        File.WriteAllText(folder.AddressTermsPath, "{ isto não é json");
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PostToUi = action => action() };
        await vm.Loading;

        Assert.False(vm.CanEdit);
        Assert.NotNull(vm.ErrorMessage);
        vm.Flush();

        Assert.Equal("{ isto não é json", File.ReadAllText(folder.AddressTermsPath));
    }

    private static readonly AddressTerm[] Both =
    [
        new("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
        new("美咲", "健太", "先輩", "senpai"),
    ];

    private sealed class ThrowingAddressSuggester(Exception error) : IGlossarySuggester
    {
        public Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<CharacterVoiceSuggestion>>(error);

        public Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<TermTranslationSuggestion>>(error);

        public Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<AddressTermSuggestion>>(error);
    }

    [Fact]
    public async Task Falha_ao_gravar_os_tratamentos_avisa_que_ainda_citam_o_nome_antigo_e_a_nova_tentativa_nao_troca_de_novo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);
        File.Delete(folder.AddressTermsPath);
        Directory.CreateDirectory(folder.AddressTermsPath);

        vm.Characters.Single(c => c.Source == "美咲").Source = "tmp";
        vm.Characters.Single(c => c.Source == "健太").Source = "美咲";
        vm.Characters.Single(c => c.Source == "tmp").Source = "健太";
        vm.Flush();

        Assert.True(vm.SaveFailed);
        Assert.Contains("ainda citam o nome antigo", vm.ErrorMessage);
        Assert.Equal(["健太", "美咲", "先生"], folder.LoadCharacters().Select(c => c.Source));

        Directory.Delete(folder.AddressTermsPath);
        vm.Flush();

        Assert.False(vm.SaveFailed);
        Assert.Equal(
        [
            new AddressTerm("美咲", "健太", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("健太", "美咲", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
    }
}
