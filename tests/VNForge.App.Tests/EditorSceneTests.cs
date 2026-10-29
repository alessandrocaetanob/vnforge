using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Na cena" (a fala anterior e a seguinte) e "Usar em todos" (a mesma fala em outras linhas) no inspetor do Editor.</summary>
public sealed class EditorSceneTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorSceneTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, null, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "健太", "「うん」\\p", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "「うん」\\p", "Sim.\\p", TranslationStatus.Machine),
            Fala(2, 1, "美咲", "「うん」", null, TranslationStatus.Untranslated),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private async Task<EditorViewModel> Abrir(Func<string, LineEditor?>? gravador = null)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao()) { EditorFactory = gravador };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public void A_contagem_da_mesma_fala_usa_o_separador_de_milhar_do_pt_BR()
    {
        var unidade = Fala(1, 0, "美咲", "「うん」\\p", "Aham.\\p", TranslationStatus.Machine);
        var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorMudo(), relogio, acao => acao())
        {
            SameSource = new FalasIguais(4016),
        };

        Assert.Equal("Esta fala aparece em mais 4.016 linhas.", vm.SameSourceText);
        Assert.Equal("Esta fala aparece em mais 12 linhas.", new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorMudo(), relogio, acao => acao()) { SameSource = new FalasIguais(12) }.SameSourceText);
    }

    private sealed class NavegadorMudo : VNForge.App.Services.ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    [Theory]
    [InlineData(false, "1.500 destas linhas já têm outra tradução (1.200 aprovadas). Substituir todas?", "Aplicada a 4.016 linhas.")]
    [InlineData(true, "1.500 destas linhas já têm outra tradução (1.200 aprovadas). Substituir todas?", "Aplicada a 1.234 de 4.016 linhas; as outras não foram gravadas. Tente de novo.")]
    public async Task As_frases_do_Usar_em_todos_separam_o_milhar(bool failed, string confirm, string result)
    {
        var unidade = Fala(1, 0, "美咲", "「うん」\\p", "Aham.\\p", TranslationStatus.Machine);
        var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorMudo(), relogio, acao => acao())
        {
            SameSource = new FalasIguais(4016, new(1_500, 1_200), failed ? new(1_234, 4_016, 1_234, true) : new(4_016, 4_016, 4_016, false)),
        };

        await vm.UseForAllCommand.ExecuteAsync(null);

        Assert.Equal(confirm, vm.UseForAllConfirmText);
        await vm.ConfirmUseForAllCommand.ExecuteAsync(null);
        Assert.Equal(result, failed ? vm.ErrorMessage : vm.SameSourceResult);
    }

    private sealed class FalasIguais(int count, VNForge.App.Services.SameSourcePreview? preview = null, VNForge.App.Services.SameSourceOutcome? outcome = null, TaskCompletionSource<VNForge.App.Services.SameSourceOutcome>? gate = null) : VNForge.App.Services.ISameSourceLines
    {
        public int Count => count;

        public VNForge.App.Services.SameSourceBatch Batch { get; } = new();

        public CancellationToken Token { get; private set; }

        public VNForge.App.Services.SameSourcePreview Inspect(string translation) => preview ?? new(0, 0);

        public async Task<VNForge.App.Services.SameSourceOutcome> ApplyAsync(string translation)
        {
            if (!Batch.TryStart(out var token))
            {
                throw new InvalidOperationException("Já há um Usar em todos gravando.");
            }

            Token = token;
            try
            {
                return await (gate?.Task ?? Task.FromResult(outcome ?? new(0, 0, 0, false)));
            }
            finally
            {
                Batch.Finish();
            }
        }
    }

    private LineDetailViewModel DetalheComFalas(FalasIguais falas)
    {
        var unidade = Fala(1, 0, "美咲", "「うん」\\p", "Aham.\\p", TranslationStatus.Machine);
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorMudo(), relogio, acao => acao()) { SameSource = falas };
    }

    private static TaskCompletionSource<VNForge.App.Services.SameSourceOutcome> Porta() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Usar_em_todos_devolve_o_controle_enquanto_grava_e_mostra_o_andamento_ate_o_fim()
    {
        var porta = Porta();
        var falas = new FalasIguais(40, gate: porta);
        var vm = DetalheComFalas(falas);

        var execucao = vm.UseForAllCommand.ExecuteAsync(null);

        Assert.False(execucao.IsCompleted);
        Assert.True(vm.IsApplyingToAll);
        Assert.False(vm.UseForAllCommand.CanExecute(null));
        Assert.True(vm.CancelApplyToAllCommand.CanExecute(null));
        falas.Batch.Report(new BatchProgress(1, 4));
        Assert.Equal("Aplicando nas outras linhas... 25%", vm.ApplyProgressText);
        Assert.Equal(0.25, vm.ApplyProgress);

        porta.SetResult(new VNForge.App.Services.SameSourceOutcome(40, 40, 40, false));
        await execucao;

        Assert.False(vm.IsApplyingToAll);
        Assert.False(vm.CancelApplyToAllCommand.CanExecute(null));
        Assert.True(vm.UseForAllCommand.CanExecute(null));
        Assert.Equal("Aplicada a 40 linhas.", vm.SameSourceResult);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Durante_o_lote_iniciado_pelo_Confirmar_os_tres_comandos_ficam_desligados()
    {
        var porta = Porta();
        var vm = DetalheComFalas(new FalasIguais(40, new(3, 0), gate: porta));
        await vm.UseForAllCommand.ExecuteAsync(null);
        Assert.True(vm.IsUseForAllConfirmOpen);

        var execucao = vm.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplyingToAll);
        Assert.False(vm.UseForAllCommand.CanExecute(null));
        Assert.False(vm.ConfirmUseForAllCommand.CanExecute(null));
        Assert.False(vm.UseVariantCommand.CanExecute(null));
        await vm.UseForAllCommand.ExecuteAsync(null);
        Assert.False(vm.IsUseForAllConfirmOpen);

        porta.SetResult(new VNForge.App.Services.SameSourceOutcome(40, 40, 40, false));
        await execucao;
        Assert.True(vm.UseForAllCommand.CanExecute(null));
        Assert.True(vm.ConfirmUseForAllCommand.CanExecute(null));
        Assert.True(vm.UseVariantCommand.CanExecute(null));
    }

    [Fact]
    public async Task Trocar_de_linha_no_meio_do_lote_mantem_o_andamento_e_o_Cancelar_e_barra_um_segundo_lote()
    {
        var porta = Porta();
        var falas = new FalasIguais(40, gate: porta);
        var primeira = DetalheComFalas(falas);
        var execucao = primeira.UseForAllCommand.ExecuteAsync(null);
        primeira.Dispose();

        var segunda = DetalheComFalas(falas);
        falas.Batch.Report(new BatchProgress(1, 2));

        Assert.True(segunda.IsApplyingToAll);
        Assert.Equal("Aplicando nas outras linhas... 50%", segunda.ApplyProgressText);
        Assert.False(segunda.UseForAllCommand.CanExecute(null));
        await segunda.UseForAllCommand.ExecuteAsync(null);
        Assert.Null(segunda.SameSourceResult);
        segunda.CancelApplyToAllCommand.Execute(null);
        Assert.True(falas.Token.IsCancellationRequested);

        porta.SetResult(new VNForge.App.Services.SameSourceOutcome(10, 40, 10, false, Canceled: true));
        await execucao;
        Assert.False(segunda.IsApplyingToAll);
        Assert.True(segunda.UseForAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelar_o_Usar_em_todos_passa_o_pedido_adiante_e_diz_quantas_linhas_ficaram()
    {
        var porta = Porta();
        var falas = new FalasIguais(40, gate: porta);
        var vm = DetalheComFalas(falas);
        var execucao = vm.UseForAllCommand.ExecuteAsync(null);
        Assert.False(falas.Token.IsCancellationRequested);

        vm.CancelApplyToAllCommand.Execute(null);

        Assert.True(falas.Token.IsCancellationRequested);
        porta.SetResult(new VNForge.App.Services.SameSourceOutcome(12, 40, 12, false, Canceled: true));
        await execucao;
        Assert.False(vm.IsApplyingToAll);
        Assert.Equal("Cancelado. Aplicada a 12 de 40 linhas; as outras continuam como estavam.", vm.SameSourceResult);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Se_a_gravacao_do_Usar_em_todos_falha_o_andamento_some_e_o_erro_aparece()
    {
        var porta = Porta();
        var vm = DetalheComFalas(new FalasIguais(40, gate: porta));
        var execucao = vm.UseForAllCommand.ExecuteAsync(null);

        porta.SetException(new IOException("disco cheio"));
        await execucao;

        Assert.False(vm.IsApplyingToAll);
        Assert.Contains("disco cheio", vm.ErrorMessage);
        Assert.Null(vm.SameSourceResult);
    }

    [Fact]
    public void Um_segundo_lote_so_comeca_depois_do_primeiro_terminar()
    {
        var lote = new VNForge.App.Services.SameSourceBatch();

        Assert.True(lote.TryStart(out var token));
        Assert.False(lote.TryStart(out _));
        lote.Cancel();
        Assert.True(token.IsCancellationRequested);
        lote.Finish();

        Assert.False(lote.IsRunning);
        lote.Report(new BatchProgress(1, 2));
        Assert.Equal(0, lote.Progress);
        Assert.True(lote.TryStart(out _));
    }

    [Fact]
    public async Task Usar_em_todos_do_Editor_grava_as_outras_linhas_e_atualiza_a_grade_no_fim()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        await vm.Detail.UseForAllCommand.ExecuteAsync(null);
        await vm.Detail.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.False(vm.Detail.IsApplyingToAll);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal("Aplicada a 2 linhas.", vm.Detail.SameSourceResult);
    }

    [Fact]
    public async Task Reler_o_mesmo_projeto_no_meio_do_lote_deixa_a_grade_igual_ao_disco()
    {
        var entrou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var liberar = new ManualResetEventSlim();
        var projeto = ProjectFolder.Open(raiz);
        var vm = await Abrir(_ => new LineEditor(projeto, relogio, (chave, unidades) =>
        {
            if (chave == "yst00002")
            {
                entrou.TrySetResult();
                liberar.Wait(TimeSpan.FromSeconds(30));
            }

            projeto.SaveText(chave, unidades);
        }));
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        await vm.Detail.UseForAllCommand.ExecuteAsync(null);
        var lote = vm.Detail.ConfirmUseForAllCommand.ExecuteAsync(null);
        await entrou.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        vm.Reload();
        liberar.Set();
        await lote;
        await vm.Loading;

        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal("Tá bom.", vm.Lines[2].TranslationPlain);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[2].Status);
        Assert.False(vm.Detail!.IsApplyingToAll);
    }

    [Fact]
    public async Task Na_cena_mostra_a_fala_anterior_e_a_seguinte_sem_marcadores()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[1];

        Assert.Equal(
            [new ContextLine("美咲", "「おはよう」", "Bom dia."), new ContextLine("健太", "「うん」", "Aham.", Following: true)],
            vm.Detail!.Scene);
        Assert.True(vm.Detail.HasScene);
    }

    [Fact]
    public async Task Na_primeira_e_na_ultima_linha_so_existe_um_lado()
    {
        var vm = await Abrir();

        Assert.Equal([new ContextLine("Narração", "雨が降っている。", Following: true)], vm.Detail!.Scene);

        vm.SelectedLine = vm.Lines[^1];

        Assert.Equal([new ContextLine("健太", "「うん」", "Aham.")], vm.Detail!.Scene);
    }

    [Fact]
    public async Task A_cena_ignora_o_filtro_da_grade()
    {
        var vm = await Abrir();
        vm.ShowPendingLines();

        vm.SelectedLine = vm.Lines[0];

        Assert.Equal("美咲", vm.Detail!.Scene[0].Who);
    }

    [Fact]
    public async Task A_mesma_fala_em_outras_linhas_aparece_no_inspetor_com_a_contagem()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[2];

        Assert.Equal(2, vm.Detail!.SameSourceCount);
        Assert.True(vm.Detail.HasSameSource);
        Assert.Equal("Esta fala aparece em mais 2 linhas.", vm.Detail.SameSourceText);
    }

    [Fact]
    public async Task Fala_unica_nao_oferece_usar_em_todos()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[0];

        Assert.False(vm.Detail!.HasSameSource);
        Assert.False(vm.Detail.UseForAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Usar_em_todos_grava_a_traducao_nas_linhas_do_mesmo_original_e_em_nenhuma_outra()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);

        Assert.True(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("2 destas linhas já têm outra tradução (1 aprovada). Substituir todas?", vm.Detail.UseForAllConfirmText);
        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);

        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.False(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 3).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal("Bom dia.\\p", Gravada("yst00001", 0).Translation);
        Assert.Null(Gravada("yst00001", 1).Translation);
        Assert.Null(Gravada("yst00002", 1).Translation);
        Assert.Equal("Aplicada a 2 linhas.", vm.Detail.SameSourceResult);
        Assert.Null(vm.Detail.ErrorMessage);
    }

    [Fact]
    public async Task Usar_em_todos_atualiza_a_grade_do_script_aberto()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);
        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.Equal("Tá bom.", vm.Lines[2].TranslationPlain);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[2].Status);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[3].Status);
        Assert.Equal(TranslationStatus.Machine, vm.Lines[0].Status);
    }

    [Fact]
    public async Task Usar_em_todos_sem_traducao_no_campo_avisa_e_nao_grava()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);

        Assert.Equal("Escreva a tradução desta linha antes de usá-la nas outras.", vm.Detail.ErrorMessage);
        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);
    }

    [Fact]
    public async Task Voltar_a_uma_linha_do_script_mostra_o_texto_aplicado()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);
        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        vm.SelectedLine = vm.Lines[2];

        Assert.Equal("Tá bom.\\p", vm.Detail!.Translation);
    }

    [Fact]
    public async Task Cancelar_a_pergunta_nao_grava_nada_nas_outras_linhas()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);
        vm.Detail.CancelUseForAllCommand.Execute(null);

        Assert.False(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal("Sim.\\p", Gravada("yst00002", 0).Translation);
    }

    [Fact]
    public async Task Sem_traducao_diferente_nas_outras_linhas_grava_sem_perguntar()
    {
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「やあ」\\p", null, TranslationStatus.Untranslated),
            Fala(1, 1, "美咲", "「やあ」\\p", null, TranslationStatus.Untranslated),
        ]);
        var vm = await Abrir();
        vm.Detail!.FieldText = "Oi.";

        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);

        Assert.False(vm.Detail.IsUseForAllConfirmOpen);
        Assert.Equal("Oi.\\p", Gravada("yst00001", 1).Translation);
        Assert.Equal("Aplicada a 1 linha.", vm.Detail.SameSourceResult);
    }

    [Fact]
    public async Task Usar_em_todos_registra_o_desfazer_do_lote_e_desfazer_volta_todas()
    {
        var vm = await Abrir();
        LineSavedEventArgs? lote = null;
        vm.LineSaved += (_, e) => lote = e.IsBatch ? e : lote;
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);
        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.NotNull(lote);
        Assert.Equal(2, lote.Batch.Count);
        Assert.False(lote.IsApproval);

        Assert.True(vm.Undo(lote));

        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00001", 2).Status);
        Assert.Equal("Sim.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal(TranslationStatus.Machine, Gravada("yst00002", 0).Status);
    }

    [Fact]
    public async Task Se_um_script_nao_grava_a_grade_mostra_o_que_ja_foi_gravado_e_a_mensagem_e_sem_caminho()
    {
        var falhar = true;
        var vm = await Abrir(root => new LineEditor(ProjectFolder.Open(root), relogio, (chave, unidades) =>
        {
            if (falhar && chave == "yst00002")
            {
                throw new IOException("H:\\Projetos\\segredo\\text\\yst00002.json");
            }

            ProjectFolder.Open(root).SaveText(chave, unidades);
        }));
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);

        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.Equal("Aplicada a 1 de 2 linhas; as outras não foram gravadas. Tente de novo.", vm.Detail.ErrorMessage);
        Assert.Equal("Tá bom.", vm.Lines[2].TranslationPlain);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[2].Status);
        Assert.Equal("Sim.", ProjectFolder.Open(raiz).LoadText("yst00002")[0].Translation?.Replace("\\p", string.Empty, StringComparison.Ordinal));

        falhar = false;
        await vm.Detail!.UseForAllCommand.ExecuteAsync(null);
        await vm.Detail!.ConfirmUseForAllCommand.ExecuteAsync(null);

        Assert.Null(vm.Detail.ErrorMessage);
        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
    }
}
