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
    public void O_padrao_e_adaptar_voce_narrador_neutro_onomatopeia_e_ordem_ocidental()
    {
        var style = new StyleSettingsViewModel();

        Assert.Equal(HonorificsPolicy.Adapt, style.SelectedHonorific?.Value);
        Assert.Equal(AddressForm.Voce, style.SelectedAddress?.Value);
        Assert.Equal(NarratorPerson.First, style.SelectedNarratorPerson?.Value);
        Assert.Equal(NarratorGender.Neutral, style.SelectedNarratorGender?.Value);
        Assert.True(style.TranslateOnomatopoeia);
        Assert.True(style.WesternNameOrder);
        Assert.True(style.KeyboardPunctuation);
        Assert.Equal(StyleConfig.Default, style.ToConfig());
        Assert.Equal(["Manter", "Remover", "Adaptar"], style.Honorifics.Select(o => o.Name));
        Assert.Equal(["Você", "Tu"], style.Addresses.Select(o => o.Name));
        Assert.Equal([false, false, true], style.Honorifics.Select(o => o.IsSelected));
    }

    [Fact]
    public void O_delimitador_da_fala_vem_sem_sinal_e_a_tela_oferece_sem_sinal_ou_guillemets()
    {
        var style = new StyleSettingsViewModel();

        Assert.Equal(SpeakerDelimiterStyle.None, style.SelectedSpeakerDelimiter?.Value);
        Assert.Equal(["Sem sinal", "« »"], style.SpeakerDelimiters.Select(o => o.Label));
        Assert.Equal([SpeakerDelimiterStyle.None, SpeakerDelimiterStyle.Guillemets], style.SpeakerDelimiters.Select(o => o.Value));
    }

    [Fact]
    public void Carregar_o_estilo_com_guillemets_mostra_a_escolha_sem_avisar_e_volta_na_config()
    {
        var style = new StyleSettingsViewModel();
        var changes = 0;
        style.Changed += (_, _) => changes++;
        var loaded = StyleConfig.Default with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets };

        style.Load(loaded);

        Assert.Equal(0, changes);
        Assert.Equal(SpeakerDelimiterStyle.Guillemets, style.SelectedSpeakerDelimiter?.Value);
        Assert.Equal(loaded, style.ToConfig());
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
    public async Task A_escolha_do_delimitador_so_aparece_com_a_acentuacao_cp1252()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Opened(temp);

        vm.EncodingStrategy = "cp1252";
        Assert.True(vm.ShowCp1252Warning);

        vm.EncodingStrategy = "fold";
        Assert.False(vm.ShowCp1252Warning);
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
    public void Escolher_o_cartao_desmarca_os_outros_e_avisa_uma_vez()
    {
        var style = new StyleSettingsViewModel();
        var changes = 0;
        style.Changed += (_, _) => changes++;

        style.Honorifics[1].IsSelected = true;

        Assert.Equal(HonorificsPolicy.Remove, style.SelectedHonorific?.Value);
        Assert.Equal([false, true, false], style.Honorifics.Select(o => o.IsSelected));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Carregar_o_projeto_nao_conta_como_mudanca_da_pessoa()
    {
        var style = new StyleSettingsViewModel();
        var changes = 0;
        style.Changed += (_, _) => changes++;
        var loaded = new StyleConfig
        {
            Honorifics = HonorificsPolicy.Keep,
            Address = AddressForm.Tu,
            Narrator = new NarratorStyle { Who = "Kenta", Person = NarratorPerson.Third, Gender = NarratorGender.Male },
            TranslateOnomatopoeia = false,
            WesternNameOrder = false,
            KeyboardPunctuation = false,
        };

        style.Load(loaded);

        Assert.Equal(0, changes);
        Assert.Equal(loaded, style.ToConfig());
        Assert.Equal("Kenta", style.NarratorWho);
    }

    [Fact]
    public async Task Abrir_o_projeto_mostra_o_estilo_do_vnforge_json()
    {
        using var temp = new TempFolder();
        var saved = StyleConfig.Default with { Honorifics = HonorificsPolicy.Remove, Address = AddressForm.Tu };

        var (vm, _) = await Opened(temp, saved);

        Assert.Equal(saved, vm.Style.ToConfig());
        Assert.Null(vm.ErrorMessage);
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

    [Fact]
    public async Task Mudar_o_estilo_nao_mexe_nas_outras_escolhas_do_vnforge_json()
    {
        using var temp = new TempFolder();
        var (vm, root) = await Opened(temp);
        var before = ProjectFolder.Open(root).LoadConfig();

        vm.Style.SelectedHonorific = vm.Style.Honorifics[1];

        var after = ProjectFolder.Open(root).LoadConfig();
        Assert.Equal(before with { Style = after.Style }, after);
        Assert.Equal(HonorificsPolicy.Remove, after.Style.Honorifics);
    }

    [Fact]
    public async Task Voltar_ao_padrao_tira_o_bloco_style_do_arquivo()
    {
        using var temp = new TempFolder();
        var (vm, root) = await Opened(temp, StyleConfig.Default with { Honorifics = HonorificsPolicy.Keep });

        vm.Style.SelectedHonorific = vm.Style.Honorifics.Single(o => o.Value == HonorificsPolicy.Adapt);

        Assert.DoesNotContain("\"style\"", File.ReadAllText(Path.Combine(root, ProjectFolder.ConfigFileName)));
    }

    [Fact]
    public async Task O_guia_de_estilo_continua_no_style_md_da_pasta_do_projeto()
    {
        using var temp = new TempFolder();
        var (vm, root) = await Opened(temp);

        vm.StyleGuide = "Linguagem jovem e natural.";

        Assert.Equal("Linguagem jovem e natural.", File.ReadAllText(Path.Combine(root, "style.md")));
    }

    [Fact]
    public void Sem_projeto_aberto_mudar_o_estilo_nao_grava_nada()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var saves = 0;
        vm.ProjectSaved += (_, _) => saves++;

        vm.Style.SelectedHonorific = vm.Style.Honorifics[0];

        Assert.Equal(0, saves);
        Assert.Null(vm.ErrorMessage);
    }
}
