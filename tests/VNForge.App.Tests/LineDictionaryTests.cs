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
        Assert.Equal([new WordSenseRow(1, "lateness; tardiness"), new WordSenseRow(2, "being late (for work, school)", "vs")], vm.WordSenses);
        Assert.Equal("being late (for work, school)", vm.WordSenses[1].Text);
        Assert.Equal("lateness; tardiness", vm.WordSenses[0].Display);
        Assert.Equal("(vs) being late (for work, school)", vm.WordSenses[1].Display);
        Assert.Equal(Em(Frase, "遅刻"), vm.WordStart);
        Assert.Equal(2, vm.WordLength);
        Assert.False(vm.HasWordMore);
        Assert.False(vm.HasWordNote);
        Assert.Equal("JMdict · offline · EDRDG", LineDetailViewModel.WordFootnote);
        Assert.Contains("CC BY-SA", vm.WordFootnoteTip);
        Assert.Contains("https://www.edrdg.org/edrdg/licence.html", vm.WordFootnoteTip);
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
        Assert.Equal("行こう é a forma volitiva (\"vamos\") de 行く", vm.WordNote);
        Assert.Equal(4, vm.WordSenses.Count);
        Assert.Equal("to go; to move (towards)", vm.WordSenses[0].Text);
        Assert.Equal("Mais 6 sentidos · 2 leituras", vm.WordMore);
        Assert.True(vm.HasWordMore);
        Assert.Equal(Em(original, "行こう"), vm.WordStart);
        Assert.Equal(3, vm.WordLength);
    }

    [Fact]
    public async Task A_classe_da_palavra_so_aparece_no_sentido_em_que_ela_muda()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), "行って");

        vm.HoverSource(0);
        vm.ExpandWordCommand.Execute(null);

        // 行く não troca de classe entre os sentidos: nenhum leva o marcador. 行う, a outra entrada, tem um sentido só.
        Assert.All(vm.WordSenses, row => Assert.Null(row.Pos));
        Assert.Null(Assert.Single(vm.WordOthers).Senses[0].Pos);
    }

    [Fact]
    public async Task Mais_sentidos_abre_a_lista_inteira_e_as_outras_entradas_da_mesma_grafia()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario), "行って");

        vm.HoverSource(0);
        Assert.Equal("Mais 6 sentidos · 2 leituras · 1 outra entrada", vm.WordMore);
        vm.ExpandWordCommand.Execute(null);

        Assert.Equal(10, vm.WordSenses.Count);
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
        Assert.Contains("10 MB", LineDetailViewModel.OfferText);
        Assert.Contains("EDRDG", LineDetailViewModel.OfferText);
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
    public async Task Tempo_esgotado_da_conexao_mostra_o_erro_no_cartao_e_nao_derruba_o_comando()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.TimeoutServer()));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailedCard);
        Assert.NotEmpty(vm.InstallProblem!);
        Assert.Equal(OfflineDictionaryState.Failed, dicionario.State);
        Assert.False(vm.IsInstallingCard);
    }

    [Fact]
    public async Task Download_que_para_de_receber_dados_estoura_o_prazo_e_sai_do_estado_de_baixando()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.HangingServer()), TimeSpan.FromMilliseconds(150));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailedCard);
        Assert.Contains("sem receber dados", vm.InstallProblem);
        Assert.Equal(OfflineDictionaryState.Failed, dicionario.State);
    }

    [Fact]
    public async Task Cancelar_interrompe_o_download_fecha_o_cartao_e_o_aviso_volta_na_proxima_passada()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.HangingServer()));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        var baixando = vm.InstallDictionaryCommand.ExecuteAsync(null);
        Assert.True(vm.IsInstallingCard);
        vm.CancelDictionaryInstallCommand.Execute(null);
        await baixando;

        Assert.False(vm.ShowWordCard);
        Assert.Equal(OfflineDictionaryState.NotInstalled, dicionario.State);
        Assert.Null(dicionario.Problem);
        vm.HoverSource(Em(Frase, "二人"));
        Assert.True(vm.IsOfferCard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelar_em_qualquer_etapa_deixa_o_estado_de_acordo_com_o_arquivo_no_disco(bool atualizacao)
    {
        var servidor = new SyntheticDictionary.Server();
        var dicionario = atualizacao
            ? await SyntheticDictionary.InstalledAsync(Dicionario, () => new HttpClient(servidor))
            : SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(servidor));
        var indice = Path.Combine(Dicionario, "jmdict.vnfdict");
        var cancelou = false;
        dicionario.Changed += (_, _) =>
        {
            // Cancela no primeiro aviso de montagem do índice (sem o tamanho do download), o ponto em que a importação já pode estar rodando.
            if (!cancelou && dicionario.State == OfflineDictionaryState.Installing && dicionario.Progress < 0)
            {
                cancelou = true;
                dicionario.CancelInstall();
            }
        };

        await dicionario.InstallAsync(TestContext.Current.CancellationToken);

        Assert.Null(dicionario.Problem);
        Assert.True(File.Exists(indice) == (dicionario.State == OfflineDictionaryState.Ready), $"Estado {dicionario.State} e arquivo {(File.Exists(indice) ? "presente" : "ausente")} não combinam.");
        if (dicionario.State == OfflineDictionaryState.Ready)
        {
            Assert.NotNull(dicionario.FindWordAt(Frase, Em(Frase, "遅刻")));
        }
        else
        {
            Assert.Equal(OfflineDictionaryState.NotInstalled, dicionario.State);
        }
    }

    [Fact]
    public async Task Esc_durante_o_download_cancela_e_o_mouse_saindo_do_cartao_nao()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.HangingServer()));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));
        var baixando = vm.InstallDictionaryCommand.ExecuteAsync(null);

        vm.LeaveWordCard();
        Assert.True(vm.IsInstallingCard);
        Assert.True(vm.CloseWordCardCommand.CanExecute(null));
        vm.CloseWordCardCommand.Execute(null);
        await baixando;

        Assert.False(vm.ShowWordCard);
        Assert.Equal(OfflineDictionaryState.NotInstalled, dicionario.State);
    }

    [Fact]
    public async Task Atualizar_que_falha_mostra_o_erro_e_mantem_o_dicionario_antigo()
    {
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario, () => new HttpClient(new SyntheticDictionary.Server(HttpStatusCode.ServiceUnavailable)));
        var antes = File.ReadAllBytes(dicionario.IndexPath);
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        await vm.InstallDictionaryCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailedCard);
        Assert.Contains("503", vm.InstallProblem);
        Assert.Equal(OfflineDictionaryState.Ready, dicionario.State);
        Assert.Equal(antes, File.ReadAllBytes(dicionario.IndexPath));
        Assert.NotNull(dicionario.FindWordAt(Frase, Em(Frase, "遅刻")));

        vm.CloseWordCardCommand.Execute(null);
        vm.HoverSource(Em(Frase, "二人"));
        Assert.True(vm.IsEntryCard);
        Assert.Equal("二人", vm.WordText);
    }

    [Fact]
    public async Task O_mouse_parado_sobre_o_mesmo_caractere_nao_refaz_a_busca()
    {
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario);
        using var vm = Abrir(dicionario);
        var antes = dicionario.LookupCount;

        vm.HoverSource(Em(Frase, "相"));
        vm.HoverSource(Em(Frase, "相"));
        vm.HoverSource(Em(Frase, "相"));
        Assert.Equal(antes + 1, dicionario.LookupCount);

        vm.HoverSource(Em(Frase, "遅刻"));
        vm.CloseWordCardCommand.Execute(null);
        var depois = dicionario.LookupCount;
        vm.HoverSource(Em(Frase, "遅刻"));
        Assert.True(vm.IsEntryCard);
        Assert.Equal(depois, dicionario.LookupCount);
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

    [Fact]
    public async Task Passar_o_mouse_por_outra_letra_da_mesma_palavra_mantem_o_cartao_sem_buscar_de_novo()
    {
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario);
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));
        var buscas = dicionario.LookupCount;

        vm.HoverSource(Em(Frase, "遅刻", 1));

        Assert.True(vm.IsEntryCard);
        Assert.Equal("遅刻", vm.WordText);
        Assert.Equal(buscas, dicionario.LookupCount);
    }

    [Fact]
    public async Task O_mouse_saindo_do_cartao_da_entrada_fecha_o_cartao()
    {
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario));
        vm.HoverSource(Em(Frase, "遅刻"));

        vm.LeaveWordCard();

        Assert.False(vm.ShowWordCard);
        Assert.Equal(-1, vm.WordStart);
    }

    [Fact]
    public void Sem_dicionario_os_comandos_do_cartao_nao_fazem_nada_e_o_estado_de_instalacao_e_o_de_montagem()
    {
        using var vm = Abrir(null);

        vm.ExpandWordCommand.Execute(null);
        vm.InstallDictionaryCommand.Execute(null);
        vm.CancelDictionaryInstallCommand.Execute(null);
        vm.DismissDictionaryOfferCommand.Execute(null);

        Assert.False(vm.ShowWordCard);
        Assert.False(vm.IsWordExpanded);
        Assert.Equal("Montando o dicionário...", vm.InstallStatusText);
        Assert.Equal(0, vm.InstallProgress);
        Assert.True(vm.InstallIsIndeterminate);
        Assert.Null(vm.InstallProblem);
        Assert.False(vm.WordNeedsUpdate);
    }

    [Fact]
    public async Task Durante_o_download_o_cartao_mostra_a_fracao_baixada_e_o_mouse_em_outra_palavra_nao_o_troca()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.StalledServer()));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));

        var baixando = vm.InstallDictionaryCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => vm.InstallProgress > 0, "a primeira fração do download");

        Assert.True(vm.IsInstallingCard);
        Assert.Matches(@"^Baixando o dicionário · \d+%$", vm.InstallStatusText);
        Assert.InRange(vm.InstallProgress, 1, 99);
        Assert.False(vm.InstallIsIndeterminate);
        vm.HoverSource(Em(Frase, "二人"));
        Assert.True(vm.IsInstallingCard);

        vm.CancelDictionaryInstallCommand.Execute(null);
        await baixando;
        Assert.False(vm.ShowWordCard);
    }

    [Fact]
    public async Task Com_o_cartao_de_erro_aberto_o_mouse_em_outra_palavra_deixa_o_erro_onde_esta()
    {
        var dicionario = SyntheticDictionary.NotInstalled(Dicionario, () => new HttpClient(new SyntheticDictionary.TimeoutServer()));
        using var vm = Abrir(dicionario);
        vm.HoverSource(Em(Frase, "遅刻"));
        await vm.InstallDictionaryCommand.ExecuteAsync(null);
        Assert.True(vm.IsFailedCard);

        vm.HoverSource(Em(Frase, "二人"));

        Assert.True(vm.IsFailedCard);
        Assert.NotEmpty(vm.InstallProblem!);
    }

    [Fact]
    public async Task Atualizar_com_o_cartao_fechado_nao_abre_cartao_nenhum()
    {
        var dicionario = await SyntheticDictionary.InstalledAsync(Dicionario, () => new HttpClient(new SyntheticDictionary.Server()));
        using var vm = Abrir(dicionario);

        await dicionario.InstallAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.ShowWordCard);
        Assert.False(vm.WordNeedsUpdate);
        Assert.Equal(OfflineDictionaryState.Ready, dicionario.State);
    }

    [Fact]
    public void O_download_so_e_oferecido_sobre_letra_japonesa_dentro_do_texto()
    {
        const string texto = "A㐀豈ｱあア";
        using var vm = Abrir(SyntheticDictionary.NotInstalled(Dicionario), texto);

        var oferecido = new List<bool>();
        for (var i = 0; i <= texto.Length; i++)
        {
            vm.HoverSource(i);
            oferecido.Add(vm.IsOfferCard);
            vm.CloseWordCard();
        }

        Assert.Equal([false, true, true, true, true, true, false], oferecido);
    }

    [Fact]
    public async Task Entrada_com_um_sentido_escondido_e_outra_so_em_kana_resume_o_que_falta_no_cartao()
    {
        const string extras = """
            <entry><ent_seq>10</ent_seq><k_ele><keb>選択</keb><ke_pri>news1</ke_pri></k_ele><r_ele><reb>せんたく</reb></r_ele>
            <sense><gloss>choice</gloss></sense><sense><gloss>selection</gloss></sense><sense><gloss>option</gloss></sense><sense><gloss>pick</gloss></sense><sense><gloss>alternative</gloss></sense></entry>
            <entry><ent_seq>11</ent_seq><r_ele><reb>せんたく</reb></r_ele><sense><gloss>laundry</gloss></sense></entry>
            """;
        using var vm = Abrir(await SyntheticDictionary.InstalledAsync(Dicionario, extraEntries: extras), "せんたく");

        vm.HoverSource(0);

        Assert.Equal("Mais 1 sentido · 1 outra entrada", vm.WordMore);
        var outra = Assert.Single(vm.WordOthers);
        Assert.Equal("せんたく", outra.Title);
    }
}
