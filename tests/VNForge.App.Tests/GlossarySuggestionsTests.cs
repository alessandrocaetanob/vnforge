using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Glossary;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Aviso de gênero, sugestões da IA (voz, gênero e termos que se repetem) e salvamento automático da página Glossário (#311).</summary>
[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
public class GlossarySuggestionsTests
{
    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> OpenAsync(
        TempFolder temp, IGlossarySuggester? suggester = null, FakeTimeProvider? clock = null, IReadOnlyList<CharacterEntry>? characters = null, IEnumerable<TextUnit>? extraUnits = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        List<TextUnit> units =
        [
            Line(0, "「先輩、遅刻です」", "美咲"),
            Line(1, "遅いぞ", "健太"),
            Line(2, "静かにしなさい", "先生"),
            Line(3, "narração"),
            Line(4, "早くしろ", "健太"),
            Line(5, "行くぞ", "健太"),
            Line(6, "席に着きなさい", "先生"),
            Line(7, "宿題を出しなさい", "先生"),
        ];
        // Um rótulo de desconhecido que fala bastante: não é personagem e não vai à IA.
        units.AddRange(Enumerable.Range(40, 5).Select(n => Line(n, $"誰かの声その{n}", "？？？")));
        units.AddRange(Enumerable.Range(10, 6).Select(n => Line(n, $"魔法学院の噂その{n}")));
        units.AddRange(extraUnits ?? []);
        folder.SaveText("yst00001", units);
        folder.SaveGlossary([new GlossaryTerm("先輩", "veterano")]);
        folder.SaveCharacters(characters ?? [new CharacterEntry("美咲", "Misaki", CharacterGender.Female), new CharacterEntry("健太", "Kenta")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), Suggester = suggester, AutosaveClock = clock, PostToUi = action => action() };
        await vm.Loading;
        return (vm, folder);
    }

    private sealed class ThrowingSuggester(Exception error) : IGlossarySuggester
    {
        public Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<CharacterVoiceSuggestion>>(error);

        public Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<TermTranslationSuggestion>>(error);

        public Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<AddressTermSuggestion>>(error);
    }

    [Fact]
    public async Task Pedir_de_novo_nao_manda_quem_ja_tem_sugestao()
    {
        var fake = new FakeGlossarySuggester(voice: c => c.Name == "健太" ? new CharacterVoiceSuggestion(c.Name, CharacterGender.Male, "x") : new CharacterVoiceSuggestion(c.Name, CharacterGender.Unknown, string.Empty));
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, fake);

        await vm.SuggestVoicesCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.PendingSuggestionCount);
        Assert.Equal(1, vm.VoiceRequestCount);

        await vm.SuggestVoicesCommand.ExecuteAsync(null);
        Assert.Equal(["先生"], fake.ReceivedVoices.Last().Select(r => r.Name));
    }

    private static void BreakCharactersFile(ProjectFolder folder)
    {
        File.Delete(folder.CharactersPath);
        Directory.CreateDirectory(folder.CharactersPath);
    }

    [Fact]
    public async Task Falha_ao_gravar_mantem_as_edicoes_e_diz_o_motivo_sem_caminho()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        vm.Characters[1].Tone = "casual";
        BreakCharactersFile(folder);

        Assert.False(vm.Flush());

        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveFailed);
        Assert.Equal("Não foi possível salvar", vm.SaveStatus);
        Assert.Equal("casual", vm.Characters[1].Tone);
        Assert.DoesNotContain(temp.Path, vm.ErrorMessage);
        Assert.DoesNotContain("characters.json", vm.ErrorMessage);
        Assert.Contains("Não foi possível salvar", vm.ErrorMessage);
    }

    private sealed class StubClaudeClient(ClaudeReply reply) : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => Task.FromResult(reply);
    }

}
