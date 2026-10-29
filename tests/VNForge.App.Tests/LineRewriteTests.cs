using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class LineRewriteTests : IDisposable
{
    private static readonly FormattingProfile Caixa = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) };

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineRewriteTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Machine);
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            new("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"原文\p", @"Olá, tudo bem?\p", TranslationStatus.Machine),
            new("yst00001:000001", TextUnitKind.Dialogue, "？？？", @"原文2\p", @"Quem é?\p", TranslationStatus.Machine),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static string Palavras(int quantas) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", quantas));

    private LineDetailViewModel Abrir(ILineSuggester? sugestor, string? voz = "seco e resmungão", bool comCaixa = true, int indice = 0, FormattingProfile? perfil = null, IReadOnlyList<ContextLine>? cena = null)
    {
        var unidade = ProjectFolder.Open(raiz).LoadText("yst00001")[indice];
        var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorNulo(), relogio, acao => acao())
        {
            Suggester = sugestor,
            Context = new LineContext("Misaki", voz, [new GlossaryTerm("先輩", "senpai")]),
            Characters = [new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "seco e resmungão"), new CharacterEntry("？？？", "???", CharacterGender.Unknown, "misterioso")],
            ProjectRoot = raiz,
            Scene = cena ?? [],
        };
        if (comCaixa)
        {
            vm.AttachPreview(perfil ?? Caixa);
        }

        return vm;
    }

    private static RewriteChip Chip(LineDetailViewModel vm, RewriteDirection direcao) => vm.RewriteChips.Single(c => c.Direction == direcao);

    [Fact]
    public void Os_cinco_chips_estao_na_ordem_do_inspetor()
    {
        using var vm = Abrir(new LineSuggester(() => null));

        Assert.Equal(
            ["Mais curta", "Mais natural", "Mais fiel", "Mais formal", "Como o personagem falaria"],
            vm.RewriteChips.Select(c => c.Label));
        Assert.True(vm.HasRewriter);
    }

    [Fact]
    public void Sem_sugestor_que_reescreva_os_chips_somem_e_nao_executam()
    {
        using var vm = Abrir(null);

        Assert.False(vm.HasRewriter);
        Assert.False(Chip(vm, RewriteDirection.Shorter).Command.CanExecute(null));
    }

    [Fact]
    public async Task Chip_pede_duas_versoes_com_a_direcao_a_traducao_o_como_fala_o_glossario_e_a_caixa()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor));

        await Chip(vm, RewriteDirection.AsCharacter).Command.ExecuteAsync(null);

        var (lote, pedido) = Assert.Single(provedor.ReceivedRewrites);
        Assert.Equal(new RewriteRequest(RewriteDirection.AsCharacter, @"Olá, tudo bem?\p", 3, 40), pedido);
        var personagem = Assert.Single(lote.Characters);
        Assert.Equal(new CharacterInfo("美咲", "Misaki", "seco e resmungão", "female"), personagem);
        Assert.Equal([new GlossaryEntry("先輩", "senpai", null)], lote.Glossary);
        Assert.Equal(2, vm.RewriteOptions.Count);
        Assert.Equal("Como o personagem falaria · 2 versões", vm.RewriteTitle);
        Assert.Equal(@"Olá, tudo bem?\p", vm.Translation);
        Assert.False(vm.IsRewriting);
    }

    [Fact]
    public async Task Sem_chave_de_api_usa_o_provedor_falso_e_avisa()
    {
        using var vm = Abrir(new LineSuggester(() => null));

        await Chip(vm, RewriteDirection.MoreFormal).Command.ExecuteAsync(null);

        Assert.Equal(2, vm.RewriteOptions.Count);
        Assert.Contains("sem chave", vm.RewriteNote);
        Assert.Null(vm.RewriteError);
    }

    [Fact]
    public async Task Cada_versao_vem_medida_contra_a_caixa_do_jogo()
    {
        var provedor = new ProvedorDeReescrita(_ => [@"Oi.\p", Palavras(20) + @"\p"]);
        using var vm = Abrir(new LineSuggester(() => provedor));

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        var (cabe, passa) = (vm.RewriteOptions[0], vm.RewriteOptions[1]);
        Assert.Equal("Cabe · 1 de 3 linhas", cabe.FitText);
        Assert.True(cabe.Fits);
        Assert.StartsWith("Passa ", passa.FitText);
        Assert.True(passa.Overflows);
        Assert.False(passa.Fits);
    }

    [Fact]
    public async Task Sem_perfil_de_formatacao_as_versoes_vem_sem_medida_e_sem_caixa_no_pedido()
    {
        var provedor = new ProvedorDeReescrita(_ => [@"Oi.\p", @"E aí.\p"]);
        using var vm = Abrir(new LineSuggester(() => provedor), comCaixa: false);

        await Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        Assert.All(vm.RewriteOptions, o => Assert.False(o.HasFit));
        Assert.Equal(new RewriteRequest(RewriteDirection.MoreNatural, @"Olá, tudo bem?\p"), provedor.Pedidos.Single());
    }

    [Fact]
    public async Task Usar_a_versao_troca_a_traducao_e_limpa_as_opcoes()
    {
        var provedor = new ProvedorDeReescrita(_ => [@"Oi.\p", @"E aí.\p"]);
        using var vm = Abrir(new LineSuggester(() => provedor));
        await Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        vm.RewriteOptions[1].UseCommand.Execute(null);

        Assert.Equal(@"E aí.\p", vm.Translation);
        Assert.Empty(vm.RewriteOptions);
        Assert.False(vm.ShowRewriteResult);
        Assert.True(vm.Flush());
        Assert.Equal(@"E aí.\p", ProjectFolder.Open(raiz).LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Descartar_limpa_as_opcoes_sem_tocar_na_traducao()
    {
        using var vm = Abrir(new LineSuggester(() => new FakeTranslationProvider()));
        await Chip(vm, RewriteDirection.MoreFormal).Command.ExecuteAsync(null);

        vm.DiscardRewriteCommand.Execute(null);

        Assert.Empty(vm.RewriteOptions);
        Assert.Equal(@"Olá, tudo bem?\p", vm.Translation);
    }

    [Fact]
    public async Task Como_o_personagem_falaria_sem_o_como_fala_explica_e_nao_chama_a_ia()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor), voz: null);

        await Chip(vm, RewriteDirection.AsCharacter).Command.ExecuteAsync(null);

        Assert.Empty(provedor.ReceivedRewrites);
        Assert.Contains("Como fala", vm.RewriteError);
        Assert.Contains("Misaki", vm.RewriteError);
        Assert.False(vm.HasRewriteOptions);
    }

    [Fact]
    public async Task Linha_sem_traducao_explica_e_nao_chama_a_ia()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor));
        vm.Translation = string.Empty;

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        Assert.Empty(provedor.ReceivedRewrites);
        Assert.NotNull(vm.RewriteError);
    }

    [Fact]
    public async Task Versao_que_perde_marcador_ou_repete_a_traducao_sai_da_lista()
    {
        var provedor = new ProvedorDeReescrita(_ => ["sem o marcador", @"Olá, tudo bem?\p", @"Tudo bem?\p"]);
        using var vm = Abrir(new LineSuggester(() => provedor));

        await Chip(vm, RewriteDirection.MoreFaithful).Command.ExecuteAsync(null);

        Assert.Equal([@"Tudo bem?\p"], vm.RewriteOptions.Select(o => o.Text));
        Assert.Equal("Mais fiel · 1 versão", vm.RewriteTitle);
    }

    [Fact]
    public async Task Falha_do_provedor_vira_mensagem_em_portugues_sem_versoes()
    {
        using var vm = Abrir(new LineSuggester(() => new ProvedorDeReescrita(_ => throw new HttpRequestException("connection reset"))));

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        Assert.StartsWith("Não foi possível reescrever", vm.RewriteError);
        Assert.False(vm.HasRewriteOptions);
        Assert.False(vm.IsRewriting);
        Assert.Equal(@"Olá, tudo bem?\p", vm.Translation);
    }

    [Fact]
    public async Task Recusa_da_ia_vira_mensagem_em_portugues()
    {
        using var vm = Abrir(new LineSuggester(() => new ProvedorDeReescrita(_ => [], refused: true)));

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        Assert.Contains("recus", vm.RewriteError);
    }

    [Fact]
    public async Task Novo_pedido_apaga_as_versoes_e_o_erro_anteriores_ao_comecar()
    {
        var espera = new TaskCompletionSource<IReadOnlyList<string>>();
        var primeiro = true;
        var provedor = new ProvedorDeReescrita(_ => primeiro ? [@"Oi.\p", @"E aí.\p"] : throw new InvalidOperationException("não deve chamar sincrono"))
        {
            Espera = () => primeiro ? null : espera.Task,
        };
        using var vm = Abrir(new LineSuggester(() => provedor));
        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);
        Assert.True(vm.HasRewriteOptions);

        primeiro = false;
        var pedido = Chip(vm, RewriteDirection.MoreFormal).Command.ExecuteAsync(null);

        Assert.True(vm.IsRewriting);
        Assert.False(vm.HasRewriteOptions);
        Assert.False(Chip(vm, RewriteDirection.Shorter).Command.CanExecute(null));

        espera.SetResult([@"Boa tarde.\p"]);
        await pedido;
        Assert.Equal([@"Boa tarde.\p"], vm.RewriteOptions.Select(o => o.Text));
        Assert.True(Chip(vm, RewriteDirection.Shorter).Command.CanExecute(null));
    }

    [Fact]
    public async Task Editar_a_traducao_enquanto_a_ia_responde_descarta_as_versoes_atrasadas()
    {
        var espera = new TaskCompletionSource<IReadOnlyList<string>>();
        var provedor = new ProvedorDeReescrita(_ => []) { Espera = () => espera.Task };
        using var vm = Abrir(new LineSuggester(() => provedor));
        var pedido = Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        vm.Translation = @"Editei à mão.\p";
        espera.SetResult([@"Oi.\p", @"E aí.\p"]);
        await pedido;

        Assert.False(vm.HasRewriteOptions);
        Assert.Contains("mudou", vm.RewriteError);
        Assert.Equal(@"Editei à mão.\p", vm.Translation);
    }

    [Fact]
    public async Task Editar_a_traducao_depois_das_versoes_impede_que_Usar_apague_a_edicao()
    {
        var provedor = new ProvedorDeReescrita(_ => [@"Oi.\p", @"E aí.\p"]);
        using var vm = Abrir(new LineSuggester(() => provedor));
        await Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        vm.Translation = @"Editei à mão.\p";
        vm.RewriteOptions[0].UseCommand.Execute(null);

        Assert.Equal(@"Editei à mão.\p", vm.Translation);
        Assert.Empty(vm.RewriteOptions);
        Assert.Contains("mudou", vm.RewriteError);
    }

    [Fact]
    public async Task Cancelar_interrompe_o_pedido_sem_mostrar_erro()
    {
        var espera = new TaskCompletionSource<IReadOnlyList<string>>();
        var provedor = new ProvedorDeReescrita(_ => [])
        {
            Espera = () => espera.Task,
        };
        using var vm = Abrir(new LineSuggester(() => provedor));
        var pedido = Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);
        Assert.True(vm.IsRewriting);

        vm.CancelRewriteCommand.Execute(null);
        espera.SetResult([@"tarde demais.\p"]);
        await pedido;

        Assert.False(vm.IsRewriting);
        Assert.Null(vm.RewriteError);
        Assert.False(vm.HasRewriteOptions);
    }

    [Fact]
    public async Task Depois_de_trocar_o_falante_para_Narracao_a_medida_e_o_pedido_usam_o_falante_de_agora()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor));
        vm.SelectedSpeakerChoice = vm.SpeakerChoices.First(c => c.Value == string.Empty);

        await Chip(vm, RewriteDirection.MoreFormal).Command.ExecuteAsync(null);

        var (lote, _) = Assert.Single(provedor.ReceivedRewrites);
        Assert.Null(lote.Lines[0].Speaker);
        Assert.Empty(lote.Characters);
        Assert.Equal(2, vm.RewriteOptions.Count);
        Assert.All(vm.RewriteOptions, o => Assert.StartsWith("Cabe", o.FitText));
    }

    [Fact]
    public async Task O_pedido_leva_o_estilo_e_o_guia_do_projeto_e_as_falas_vizinhas_da_cena()
    {
        var pasta = ProjectFolder.Open(raiz);
        pasta.SaveConfig(pasta.LoadConfig() with { Style = StyleConfig.Default with { Address = AddressForm.Tu } });
        File.WriteAllText(Path.Combine(raiz, "style.md"), "  Tom de comédia.  ");
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor), cena: [new ContextLine("A", "antes"), new ContextLine("B", "depois", "Depois.", Following: true)]);

        await Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        var (lote, _) = Assert.Single(provedor.ReceivedRewrites);
        Assert.Equal(AddressForm.Tu, lote.Style.Address);
        Assert.Equal("Tom de comédia.", lote.StyleGuide);
        Assert.Equal(["antes"], lote.PreviousLines.Select(l => l.Source));
        Assert.Equal(["depois"], lote.NextLines.Select(l => l.Source));
    }

    [Fact]
    public async Task Falante_so_de_sinais_nao_leva_personagem_ao_pedido()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor), indice: 1);

        await Chip(vm, RewriteDirection.MoreNatural).Command.ExecuteAsync(null);

        Assert.Empty(Assert.Single(provedor.ReceivedRewrites).Batch.Characters);
    }

    [Fact]
    public async Task Com_a_quebra_desligada_a_caixa_nao_vai_ao_pedido()
    {
        var provedor = new FakeTranslationProvider();
        using var vm = Abrir(new LineSuggester(() => provedor), perfil: FormattingProfile.Default with { Wrap = new WrapRule(Enabled: false, MaxWidth: 40, MaxLines: 3) });

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        Assert.Equal(new RewriteRequest(RewriteDirection.Shorter, @"Olá, tudo bem?\p"), Assert.Single(provedor.ReceivedRewrites).Request);
    }

    [Fact]
    public async Task Mais_curta_sem_chave_explica_em_vez_de_oferecer_uma_versao_de_teste_maior()
    {
        using var vm = Abrir(new LineSuggester(() => null));

        await Chip(vm, RewriteDirection.Shorter).Command.ExecuteAsync(null);

        Assert.False(vm.HasRewriteOptions);
        Assert.Contains("chave", vm.RewriteError);
    }

    [Fact]
    public async Task Os_botoes_Usar_tem_nome_para_leitor_de_tela()
    {
        using var vm = Abrir(new LineSuggester(() => new FakeTranslationProvider()));

        await Chip(vm, RewriteDirection.MoreFaithful).Command.ExecuteAsync(null);

        Assert.Equal(["Usar a versão 1", "Usar a versão 2"], vm.RewriteOptions.Select(o => o.UseLabel));
    }

    private sealed class NavegadorNulo : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    private sealed class ProvedorDeReescrita(Func<RewriteRequest, IReadOnlyList<string>> opcoes, bool refused = false) : ITranslationProvider, IRewriteProvider
    {
        public List<RewriteRequest> Pedidos { get; } = [];

        /// <summary>Quando devolve uma tarefa, o pedido espera por ela e devolve o que ela trouxer.</summary>
        public Func<Task<IReadOnlyList<string>>?>? Espera { get; init; }

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new NotSupportedException();

        public async Task<RewriteResult> RewriteAsync(TranslationBatch batch, RewriteRequest request, CancellationToken ct)
        {
            Pedidos.Add(request);
            var id = batch.Lines[0].Id;
            if (refused)
            {
                return new RewriteResult(id, [], Refused: true);
            }

            if (Espera?.Invoke() is { } tarefa)
            {
                return new RewriteResult(id, await tarefa);
            }

            return new RewriteResult(id, opcoes(request));
        }
    }
}
