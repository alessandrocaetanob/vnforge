using System.Net;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O dicionário offline do inspetor (#538): a palavra sob o mouse vira a entrada do JMdict, com o aviso de download na primeira vez. O dicionário é inventado no teste.</summary>
public sealed class LineDictionaryTests : IDisposable
{
    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineDictionaryTests() => raiz = SampleProjects.Create(Path.Combine(pasta.Path, "atual"), "Amaoto no Hakoniwa", TranslationStatus.Machine);

    public void Dispose() => pasta.Dispose();

    private string Dicionario => Path.Combine(pasta.Path, "config", "dictionary");

    private LineDetailViewModel Abrir(OfflineDictionary? dicionario, string original = Frase, Action<string>? glossario = null, params GlossaryTerm[] termos)
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", original, "Aizawa, os dois, atrasados.", TranslationStatus.Machine);
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new Navegador(), relogio, acao => acao())
        {
            Dictionary = dicionario,
            GlossaryTermStarter = glossario,
            Context = new LineContext("Misaki", null, termos),
        };
    }

    private static int Em(string texto, string trecho, int deslocamento = 0) => texto.IndexOf(trecho, StringComparison.Ordinal) + deslocamento;

    private sealed class Navegador : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    [Fact]
    public async Task O_mouse_sobre_a_palavra_abre_a_entrada_com_leitura_classe_e_sentidos()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario));

        vm.HoverSource(Em(Frase, "遅刻", 1));

        Assert.True(vm.IsEntryCard);
        Assert.Equal("遅刻", vm.WordText);
        Assert.Equal("ちこく", vm.WordReading);
        Assert.Equal("n, vs", vm.WordTag);
        Assert.Equal([new WordSenseRow(1, "lateness; tardiness"), new WordSenseRow(2, "being late (for work, school)")], vm.WordSenses);
        Assert.Equal(Em(Frase, "遅刻"), vm.WordStart);
        Assert.Equal(2, vm.WordLength);
        Assert.False(vm.HasWordMore);
        Assert.False(vm.HasWordNote);
        Assert.Equal("JMdict · offline · EDRDG", vm.WordFootnote);
        Assert.Contains("CC BY-SA 4.0", vm.WordFootnoteTip);
    }

    [Fact]
    public async Task Palavra_que_o_glossario_ja_tem_mostra_a_traducao_fixada_em_vez_do_botao()
    {
        var pedidos = new List<string>();
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), glossario: pedidos.Add, termos: [new GlossaryTerm("遅刻", "atraso")]);

        vm.HoverSource(Em(Frase, "遅刻"));

        Assert.True(vm.InGlossary);
        Assert.Equal("No glossário: atraso", vm.GlossaryChipText);
        Assert.False(vm.CanAddWordToGlossary);
        Assert.False(vm.AddWordToGlossaryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Palavra_fora_do_glossario_oferece_adicionar_e_leva_o_trecho_do_original()
    {
        var pedidos = new List<string>();
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), glossario: pedidos.Add, termos: [new GlossaryTerm("傘", "guarda-chuva")]);

        vm.HoverSource(Em(Frase, "五分"));
        Assert.False(vm.InGlossary);
        Assert.True(vm.CanAddWordToGlossary);
        vm.AddWordToGlossaryCommand.Execute(null);

        Assert.Equal(["五分"], pedidos);
    }

    [Fact]
    public async Task Palavra_conjugada_mostra_a_forma_de_dicionario_a_nota_e_as_quatro_primeiras_acepcoes()
    {
        const string original = "「平気平気！ほら、行こう！」";
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), original);

        vm.HoverSource(Em(original, "行こう", 1));

        Assert.Equal("行く", vm.WordText);
        Assert.Equal("いく · ゆく", vm.WordReading);
        Assert.Equal("v5k-s, vi", vm.WordTag);
        Assert.Equal("行こう é a forma volitiva de 行く", vm.WordNote);
        Assert.Equal(4, vm.WordSenses.Count);
        Assert.Equal("to go; to move (towards)", vm.WordSenses[0].Text);
        Assert.Equal("Mais 2 sentidos", vm.WordMore);
        Assert.True(vm.HasWordMore);
        Assert.Equal(Em(original, "行こう"), vm.WordStart);
        Assert.Equal(3, vm.WordLength);
    }

    [Fact]
    public async Task Mais_sentidos_abre_a_lista_inteira_e_as_outras_entradas_da_mesma_grafia()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), "行って");

        vm.HoverSource(0);
        Assert.Equal("Mais 2 sentidos · 1 outra entrada", vm.WordMore);
        vm.ExpandWordCommand.Execute(null);

        Assert.Equal(6, vm.WordSenses.Count);
        Assert.False(vm.HasWordMore);
        Assert.True(vm.HasWordOthers);
        var outra = Assert.Single(vm.WordOthers);
        Assert.Equal("行う · おこなう", outra.Title);
        Assert.Equal("v5u, vt", outra.Tag);
        Assert.Equal("to perform", outra.Senses[0].Text);
    }

    [Fact]
    public async Task Sobre_a_pontuacao_ou_o_que_o_dicionario_nao_tem_nada_muda_e_trocar_de_palavra_troca_a_entrada()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario));

        vm.HoverSource(0);
        vm.HoverSource(Em(Frase, "相"));
        Assert.False(vm.ShowWordCard);

        vm.HoverSource(Em(Frase, "二人"));
        Assert.Equal("二人", vm.WordText);
        vm.HoverSource(Em(Frase, "。"));
        Assert.Equal("二人", vm.WordText);
        vm.HoverSource(Em(Frase, "五分"));
        Assert.Equal("五分", vm.WordText);
        Assert.Equal("ごふん", vm.WordReading);

        vm.CloseWordCardCommand.Execute(null);
        Assert.False(vm.ShowWordCard);
        Assert.Equal(-1, vm.WordStart);
        Assert.Empty(vm.WordSenses);
    }

    [Fact]
    public async Task Marcador_do_jogo_ocupa_uma_posicao_e_a_palavra_seguinte_continua_achavel()
    {
        const string original = "{player}は遅刻だ";
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), original);

        Assert.Equal("￼は遅刻だ", vm.DictionarySource);
        vm.HoverSource(0);
        Assert.False(vm.ShowWordCard);
        vm.HoverSource(3);

        Assert.Equal("遅刻", vm.WordText);
        Assert.Equal(2, vm.WordStart);
    }

    [Fact]
    public void Sem_dicionario_montado_o_mouse_nao_faz_nada()
    {
        using var vm = Abrir(null);

        vm.HoverSource(Em(Frase, "遅刻"));

        Assert.False(vm.ShowWordCard);
    }

    [Fact]
    public void Sem_o_dicionario_instalado_a_primeira_passada_oferece_o_download_e_Agora_nao_cala_o_aviso()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario);
        using var vm = Abrir(dicionario);

        vm.HoverSource(Em(Frase, "「"));
        Assert.False(vm.ShowWordCard);
        vm.HoverSource(Em(Frase, "遅刻"));

        Assert.True(vm.IsOfferCard);
        Assert.Contains("10 MB", vm.OfferText);
        Assert.Contains("EDRDG", vm.OfferText);
        Assert.False(File.Exists(Path.Combine(Dicionario, "jmdict.vnfdict")));

        vm.DismissDictionaryOfferCommand.Execute(null);
        Assert.False(vm.ShowWordCard);
        vm.HoverSource(Em(Frase, "二人"));
        Assert.False(vm.ShowWordCard);
        using var outra = Abrir(dicionario);
        outra.HoverSource(Em(Frase, "二人"));
        Assert.False(outra.ShowWordCard);
    }

    [Fact]
    public async Task Baixar_instala_o_dicionario_e_abre_a_entrada_da_palavra_sob_o_mouse()
    {
        var servidor = new SyntheticDictionary.Server();
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(servidor));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));
        Assert.True(vm.IsOfferCard);

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.Equal(1, servidor.Requests);
        Assert.Equal(OfflineDictionaryState.Ready, dicionario.State);
        Assert.True(vm.IsEntryCard);
        Assert.Equal("遅刻", vm.WordText);
        Assert.True(File.Exists(Path.Combine(Dicionario, "jmdict.vnfdict")));
        Assert.Equal("2026-11-09", dicionario.Version);
    }

    [Fact]
    public async Task Falha_do_download_mostra_o_erro_no_cartao_e_nao_deixa_dicionario()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.Server(HttpStatusCode.ServiceUnavailable)));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailedCard);
        Assert.Contains("503", vm.InstallProblem);
        Assert.Equal(OfflineDictionaryState.Failed, dicionario.State);
        Assert.False(File.Exists(Path.Combine(Dicionario, "jmdict.vnfdict")));
    }

    [Fact]
    public async Task Dicionario_com_mais_de_30_dias_oferece_atualizar_e_atualizar_baixa_de_novo()
    {
        var servidor = new SyntheticDictionary.Server();
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario, () => new HttpClient(servidor));
        File.SetLastWriteTimeUtc(dicionario.IndexPath, relogio.GetUtcNow().UtcDateTime.AddDays(-45));
        var velho = new OfflineDictionary(new Core.Jmdict.JmdictStore(Dicionario), () => new HttpClient(servidor), relogio);
        await velho.Loaded;
        using var vm = Abrir(velho);

        vm.HoverSource(Em(Frase, "遅刻"));
        Assert.True(vm.WordNeedsUpdate);

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.Equal(1, servidor.Requests);
        Assert.False(velho.IsStale);
        Assert.True(vm.IsEntryCard);
        Assert.False(vm.WordNeedsUpdate);
    }

    [Fact]
    public async Task O_dicionario_instalado_responde_sem_nenhuma_chamada_de_rede()
    {
        var servidor = new SyntheticDictionary.Server();
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario, () => new HttpClient(servidor));
        using var vm = Abrir(dicionario);

        vm.HoverSource(Em(Frase, "遅刻"));
        vm.HoverSource(Em(Frase, "二人"));

        Assert.Equal(0, servidor.Requests);
        Assert.Equal("二人", vm.WordText);
    }

    [Fact]
    public async Task Arquivo_do_dicionario_estragado_vira_o_cartao_de_erro_com_o_caminho()
    {
        Directory.CreateDirectory(Dicionario);
        await File.WriteAllTextAsync(Path.Combine(Dicionario, "jmdict.vnfdict"), "lixo", TestContext.Current.CancellationToken);
        var dicionario = new OfflineDictionary(new Core.Jmdict.JmdictStore(Dicionario));
        await dicionario.Loaded;
        using var vm = Abrir(dicionario);

        vm.HoverSource(Em(Frase, "遅刻"));

        Assert.True(vm.IsFailedCard);
        Assert.Contains("jmdict.vnfdict", vm.InstallProblem);
    }
}
