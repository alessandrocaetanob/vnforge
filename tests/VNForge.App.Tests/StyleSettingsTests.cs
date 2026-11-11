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
    public void O_exemplo_muda_com_honorifico_tratamento_e_onomatopeia()
    {
        var style = new StyleSettingsViewModel();
        var seen = new List<string?>();
        style.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StyleSettingsViewModel.ExampleTranslation))
            {
                seen.Add(style.ExampleTranslation);
            }
        };

        Assert.Equal("Misaki, o veterano está te chamando. O coração não dispara?", style.ExampleTranslation);

        style.SelectedHonorific = style.Honorifics[0];
        Assert.Equal("Misaki-chan, o senpai está te chamando. O coração não dispara?", style.ExampleTranslation);

        style.SelectedAddress = style.Addresses[1];
        Assert.EndsWith(" Não ficas nervosa?", style.ExampleTranslation);

        style.TranslateOnomatopoeia = false;
        Assert.Equal("Misaki-chan, o senpai está te chamando. Não fica doki doki? Não ficas nervosa?", style.ExampleTranslation);
        Assert.Equal(3, seen.Count);
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
