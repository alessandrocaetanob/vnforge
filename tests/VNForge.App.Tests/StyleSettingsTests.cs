using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class StyleSettingsTests
{
    private sealed class NoopClient : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => Task.FromResult(new ClaudeReply("end_turn", string.Empty));
    }

    private static SettingsViewModel Create(TempFolder temp) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, _ => new NoopClient());

    private static async Task<(SettingsViewModel Vm, string Root)> Opened(TempFolder temp, StyleConfig? style = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        if (style is not null)
        {
            var folder = ProjectFolder.Open(root);
            folder.SaveConfig(folder.LoadConfig() with { Style = style });
        }

        var vm = Create(temp);
        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        return (vm, root);
    }

    [Fact]
    public async Task Escolher_guillemets_grava_no_vnforge_json_e_voltar_ao_padrao_tira_o_campo()
    {
        using var temp = new TempFolder();
        var (vm, root) = await Opened(temp);
        var saves = 0;
        vm.ProjectSaved += (_, _) => saves++;

        vm.Style.SelectedSpeakerDelimiter = vm.Style.SpeakerDelimiters.Single(o => o.Value == SpeakerDelimiterStyle.Guillemets);

        Assert.Equal(1, saves);
        Assert.Equal(SpeakerDelimiterStyle.Guillemets, ProjectFolder.Open(root).LoadConfig().Style.SpeakerDelimiter);
        Assert.Contains("\"speakerDelimiter\": \"guillemets\"", File.ReadAllText(Path.Combine(root, ProjectFolder.ConfigFileName)));

        vm.Style.SelectedSpeakerDelimiter = vm.Style.SpeakerDelimiters.Single(o => o.Value == SpeakerDelimiterStyle.None);

        Assert.DoesNotContain("speakerDelimiter", File.ReadAllText(Path.Combine(root, ProjectFolder.ConfigFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cada_mudanca_de_estilo_e_gravada_no_vnforge_json_e_avisa_a_barra_de_status()
    {
        using var temp = new TempFolder();
        var (vm, root) = await Opened(temp);
        var saves = 0;
        vm.ProjectSaved += (_, _) => saves++;

        vm.Style.SelectedHonorific = vm.Style.Honorifics[0];
        vm.Style.SelectedAddress = vm.Style.Addresses[1];
        vm.Style.NarratorWho = "Kenta (protagonista)";
        vm.Style.SelectedNarratorGender = vm.Style.NarratorGenders.Single(g => g.Value == NarratorGender.Male);
        vm.Style.SelectedNarratorPerson = vm.Style.NarratorPersons.Single(p => p.Value == NarratorPerson.Third);
        vm.Style.TranslateOnomatopoeia = false;
        vm.Style.WesternNameOrder = false;
        vm.Style.KeyboardPunctuation = false;

        var style = ProjectFolder.Open(root).LoadConfig().Style;
        Assert.Equal(8, saves);
        Assert.Equal(HonorificsPolicy.Keep, style.Honorifics);
        Assert.Equal(AddressForm.Tu, style.Address);
        Assert.Equal("Kenta (protagonista)", style.Narrator.Who);
        Assert.Equal(NarratorGender.Male, style.Narrator.Gender);
        Assert.Equal(NarratorPerson.Third, style.Narrator.Person);
        Assert.False(style.TranslateOnomatopoeia);
        Assert.False(style.WesternNameOrder);
        Assert.False(style.KeyboardPunctuation);
    }
}
