using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Glossary;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba Tratamentos do Glossário (#470): a tabela, o salvamento junto com os outros arquivos e as sugestões da IA.</summary>
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

    private static readonly AddressTerm[] Saved = [new("健太", "美咲", "美咲ちゃん", "Misaki-chan")];

    [Fact]
    public async Task Projeto_sem_o_arquivo_abre_com_a_tabela_vazia_e_nada_e_criado()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);

        Assert.Empty(vm.AddressTerms.Rows);
        Assert.True(vm.AddressTerms.NoRows);
        Assert.Equal(["美咲", "健太", "先生"], vm.AddressTerms.CharacterNames);

        vm.Flush();

        Assert.False(File.Exists(folder.AddressTermsPath));
    }

    [Fact]
    public async Task Le_os_tratamentos_do_arquivo_sem_sujar_a_pagina()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, terms: Saved);

        var row = Assert.Single(vm.AddressTerms.Rows);

        Assert.Equal(("健太", "美咲", "美咲ちゃん", "Misaki-chan"), (row.From, row.To, row.Source, row.Target));
        Assert.False(vm.IsDirty);
        Assert.Equal("1 tratamento", vm.AddressTerms.Summary);
    }

    [Fact]
    public async Task Adicionar_e_preencher_uma_linha_grava_o_arquivo_com_o_resto_do_glossario()
    {
        using var temp = new TempFolder();
        var clock = new FakeTimeProvider();
        var (vm, folder) = await OpenAsync(temp, clock: clock);
        vm.SelectedTab = GlossaryTab.Address;

        Assert.Equal("Adicionar tratamento", vm.AddLabel);
        vm.AddCommand.Execute(null);
        var row = Assert.Single(vm.AddressTerms.Rows);
        row.From = "健太";
        row.To = "美咲";
        row.Source = " 美咲ちゃん ";
        row.Target = "Misaki-chan";

        Assert.True(vm.IsDirty);
        clock.Advance(GlossaryViewModel.SaveDelay);

        Assert.Equal(Saved, folder.LoadAddressTerms());
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Linha_em_branco_nao_cria_o_arquivo_e_incompleta_ou_repetida_fica_de_fora_com_o_motivo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        var table = vm.AddressTerms;

        table.Add();
        vm.Flush();
        Assert.False(File.Exists(folder.AddressTermsPath));

        var incomplete = table.Rows[0];
        incomplete.Source = "あいつ";
        table.Add();
        table.Rows[1].From = "健太";
        table.Rows[1].To = "美咲";
        table.Rows[1].Source = "美咲ちゃん";
        table.Rows[1].Target = "Misaki-chan";
        table.Add();
        table.Rows[2].From = "健太";
        table.Rows[2].To = "美咲";
        table.Rows[2].Source = "美咲";
        table.Rows[2].Target = "Misaki";
        table.Add();
        table.Rows[3].From = "美咲";
        table.Rows[3].To = "美咲";
        table.Rows[3].Source = "a";
        table.Rows[3].Target = "b";
        table.Add();
        table.Rows[4].From = "凛";
        table.Rows[4].To = "美咲";
        vm.Flush();

        Assert.Equal(Saved, folder.LoadAddressTerms());
        Assert.NotNull(incomplete.Problem);
        Assert.Null(table.Rows[1].Problem);
        Assert.Contains("já está", table.Rows[2].Problem);
        Assert.Contains("mesma pessoa", table.Rows[3].Problem);
        Assert.Contains("Falta como", table.Rows[4].Problem);
        Assert.Equal(4, vm.ProblemCount);
        Assert.Equal(3, table.PairCount);
        Assert.StartsWith("4 linhas ainda não foram salvas", vm.ProblemsText);
    }

    [Fact]
    public async Task Remover_a_ultima_linha_deixa_o_arquivo_vazio_em_vez_de_apagar_o_que_ja_existia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Saved);

        vm.AddressTerms.RemoveCommand.Execute(vm.AddressTerms.Rows[0]);
        vm.Flush();

        Assert.Empty(folder.LoadAddressTerms());
        Assert.True(File.Exists(folder.AddressTermsPath));
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

    [Fact]
    public async Task O_seletor_ganha_o_personagem_novo_e_perde_o_removido_sem_apagar_a_escolha_da_linha()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, terms: Saved);

        vm.AddCharacterCommand.Execute(null);
        vm.Characters[^1].Source = "凛";
        Assert.Contains("凛", vm.AddressTerms.CharacterNames);

        // Editar o nome de uma linha que já existe também atualiza o seletor, sem chamar nada à mão.
        vm.Characters.Single(c => c.Source == "凛").Source = "凛子";
        Assert.Contains("凛子", vm.AddressTerms.CharacterNames);
        Assert.DoesNotContain("凛", vm.AddressTerms.CharacterNames);

        vm.RemoveCharacterCommand.Execute(vm.Characters.Single(c => c.Source == "美咲"));
        var row = vm.AddressTerms.Rows[0];
        row.To = null!;

        Assert.DoesNotContain("美咲", vm.AddressTerms.CharacterNames);
        Assert.Equal("美咲", row.To);
    }

    [Fact]
    public async Task Pedir_a_IA_lee_as_falas_que_chamam_alguem_e_a_resposta_fica_como_sugestao()
    {
        var fake = new FakeGlossarySuggester(address: (_, _) =>
        [
            new AddressTermSuggestion("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
            new AddressTermSuggestion("美咲", "健太", "先輩", "senpai"),
        ]);
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake);
        var table = vm.AddressTerms;

        Assert.True(table.CanSuggest);
        Assert.NotEmpty(table.PriceText);
        await table.SuggestCommand.ExecuteAsync(null);

        var asked = Assert.Single(fake.ReceivedAddress);
        Assert.Equal(["健太", "美咲"], asked.Select(r => r.Speaker));
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, r => Assert.True(r.IsSuggestion));
        Assert.Equal(2, table.PendingCount);
        Assert.False(vm.IsDirty);
        vm.Flush();
        Assert.False(File.Exists(folder.AddressTermsPath));

        table.AcceptCommand.Execute(table.Rows[0]);
        table.DismissCommand.Execute(table.Rows[1]);

        Assert.True(vm.IsDirty);
        vm.Flush();
        Assert.Equal(Saved, folder.LoadAddressTerms());
    }

    [Fact]
    public async Task Aceitar_todas_e_a_IA_nao_repete_um_par_que_a_tabela_ja_tem()
    {
        var fake = new FakeGlossarySuggester(address: (_, _) =>
        [
            new AddressTermSuggestion("健太", "美咲", "美咲", "Misaki"),
            new AddressTermSuggestion("美咲", "健太", "先輩", "senpai"),
        ]);
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake, terms: Saved);
        var table = vm.AddressTerms;

        await table.SuggestCommand.ExecuteAsync(null);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Misaki-chan", table.Rows[0].Target);
        table.AcceptAllCommand.Execute(null);
        vm.Flush();

        Assert.Equal(["Misaki-chan", "senpai"], folder.LoadAddressTerms().Select(t => t.Target));
        Assert.False(table.HasPending);
    }

    [Fact]
    public async Task Sem_IA_ou_sem_falas_que_chamam_alguem_o_botao_fica_desligado()
    {
        using var temp = new TempFolder();
        var (without, _) = await OpenAsync(temp);

        Assert.False(without.AddressTerms.CanSuggest);
        Assert.False(without.AddressTerms.SuggestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Recusa_da_IA_vira_aviso_em_portugues_e_nao_muda_a_tabela()
    {
        var failing = new ThrowingAddressSuggester(new ClaudeRefusalException("recusou"));
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, failing);

        await vm.AddressTerms.SuggestCommand.ExecuteAsync(null);

        Assert.NotNull(vm.AddressTerms.SuggestionError);
        Assert.Empty(vm.AddressTerms.Rows);
        Assert.False(vm.AddressTerms.IsSuggesting);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_tabela()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, terms: Saved);

        vm.Project = null;

        Assert.Empty(vm.AddressTerms.Rows);
        Assert.Empty(vm.AddressTerms.CharacterNames);
    }

    [Fact]
    public async Task Aceitar_com_a_pagina_travada_nao_muda_a_sugestao()
    {
        var fake = new FakeGlossarySuggester(address: (_, _) => [new AddressTermSuggestion("健太", "美咲", "美咲ちゃん", "Misaki-chan")]);
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake);
        await vm.AddressTerms.SuggestCommand.ExecuteAsync(null);
        File.WriteAllText(folder.CharactersPath, "{ isto não é json");
        vm.Reload();
        Assert.False(vm.CanEdit);

        vm.AddressTerms.Rows.Add(new AddressTermRow("健太", "美咲", "美咲ちゃん", "Misaki-chan") { IsSuggestion = true });
        vm.AddressTerms.AcceptCommand.Execute(vm.AddressTerms.Rows[0]);

        Assert.True(vm.AddressTerms.Rows[0].IsSuggestion);
    }

    [Fact]
    public async Task Editar_o_nome_de_um_personagem_refaz_o_pedido_da_IA_na_hora_de_pedir()
    {
        var fake = new FakeGlossarySuggester();
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, fake);

        vm.Characters.Single(c => c.Source == "先生").Source = "先生B";
        await vm.AddressTerms.SuggestCommand.ExecuteAsync(null);

        Assert.Single(fake.ReceivedAddress);
    }

    [Fact]
    public async Task Linha_incompleta_nao_conta_nos_tratamentos()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddressTerms.Add();
        vm.AddressTerms.Rows[0].From = "健太";
        vm.AddressTerms.Rows[0].To = "美咲";

        Assert.Equal(0, vm.AddressTerms.PairCount);
        Assert.Equal("Nenhum tratamento ainda", vm.AddressTerms.Summary);

        vm.AddressTerms.Rows[0].Source = "美咲ちゃん";
        vm.AddressTerms.Rows[0].Target = "Misaki-chan";

        Assert.Equal(1, vm.AddressTerms.PairCount);
        Assert.Equal("1 tratamento", vm.AddressTerms.Summary);
    }

    private static readonly AddressTerm[] Both =
    [
        new("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
        new("美咲", "健太", "先輩", "senpai"),
    ];

    [Fact]
    public async Task Renomear_o_personagem_leva_os_dois_lados_do_par_na_tabela_e_no_arquivo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);

        vm.Characters.Single(c => c.Source == "美咲").Source = "みさき";
        vm.Flush();

        Assert.Equal(
        [
            new AddressTerm("健太", "みさき", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("みさき", "健太", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
        Assert.Equal([("健太", "みさき"), ("みさき", "健太")], vm.AddressTerms.Rows.Select(r => (r.From, r.To)));
        Assert.Contains("みさき", vm.AddressTerms.CharacterNames);
    }

    [Fact]
    public async Task Digitar_o_nome_aos_poucos_e_salvar_no_meio_nao_perde_o_par()
    {
        using var temp = new TempFolder();
        var clock = new FakeTimeProvider();
        var (vm, folder) = await OpenAsync(temp, clock: clock, terms: Both);
        var misaki = vm.Characters.Single(c => c.Source == "美咲");

        misaki.Source = "み";
        clock.Advance(GlossaryViewModel.SaveDelay);
        misaki.Source = "みさ";
        misaki.Source = "みさき";
        clock.Advance(GlossaryViewModel.SaveDelay);

        Assert.Equal([
            new AddressTerm("健太", "みさき", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("みさき", "健太", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
    }

    [Fact]
    public async Task Renomear_para_um_nome_que_ja_existe_nao_mexe_nos_pares_ate_a_linha_ficar_certa()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);

        var misaki = vm.Characters.Single(c => c.Source == "美咲");
        misaki.Source = "健太";
        vm.Flush();

        Assert.Equal(Both, folder.LoadAddressTerms());
        Assert.Equal([("健太", "美咲"), ("美咲", "健太")], vm.AddressTerms.Rows.Select(r => (r.From, r.To)));

        misaki.Source = "みさき";
        vm.Flush();

        Assert.Equal([
            new AddressTerm("健太", "みさき", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("みさき", "健太", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
    }

    [Fact]
    public async Task Dois_personagens_que_trocam_de_nome_trocam_os_pares()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);

        vm.Characters.Single(c => c.Source == "美咲").Source = "tmp";
        vm.Characters.Single(c => c.Source == "健太").Source = "美咲";
        vm.Characters.Single(c => c.Source == "tmp").Source = "健太";
        vm.Flush();

        Assert.Equal(
        [
            new AddressTerm("美咲", "健太", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("健太", "美咲", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
    }

    [Fact]
    public async Task Renomear_um_personagem_novo_que_ainda_nao_tem_par_nao_mexe_nos_tratamentos()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Saved);

        vm.AddCharacterCommand.Execute(null);
        vm.Characters[^1].Source = "凛";
        vm.Characters[^1].Source = "凛子";
        vm.Flush();

        Assert.Equal(Saved, folder.LoadAddressTerms());
    }

    [Fact]
    public async Task Renomear_com_um_par_orfao_no_destino_fica_o_que_ja_existia_como_no_Core()
    {
        var existente = new AddressTerm("健太", "みさき", "みさきさん", "Misaki-san");
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: [new("健太", "美咲", "美咲ちゃん", "Misaki-chan"), existente]);

        vm.Characters.Single(c => c.Source == "美咲").Source = "みさき";
        vm.Flush();

        Assert.Equal([existente], folder.LoadAddressTerms());
        Assert.Equal([("健太", "みさき")], vm.AddressTerms.Rows.Select(r => (r.From, r.To)));
        Assert.Equal("Misaki-san", Assert.Single(vm.AddressTerms.Rows).Target);
    }

    [Fact]
    public async Task Remover_um_personagem_e_renomear_outro_para_o_nome_dele_descarta_o_par_que_viraria_consigo_mesmo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms:
        [
            new("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
            new("美咲", "先生", "田中先生", "professor Tanaka"),
            new("美咲", "健太", "先輩", "senpai"),
        ]);

        vm.RemoveCharacterCommand.Execute(vm.Characters.Single(c => c.Source == "先生"));
        vm.Characters.Single(c => c.Source == "美咲").Source = "先生";
        vm.Flush();

        Assert.Equal(
        [
            new AddressTerm("健太", "先生", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("先生", "健太", "先輩", "senpai"),
        ], folder.LoadAddressTerms());
        Assert.Equal(2, vm.AddressTerms.Rows.Count);
    }

    [Fact]
    public async Task Depois_de_renomear_a_pagina_fica_salva_sem_falha()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, terms: Both);

        vm.Characters.Single(c => c.Source == "美咲").Source = "みさき";
        Assert.True(vm.IsDirty);
        vm.Flush();

        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveFailed);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Sugestao_pendente_da_IA_segue_o_nome_novo_e_continua_sugestao()
    {
        var fake = new FakeGlossarySuggester(address: (_, _) => [new AddressTermSuggestion("健太", "美咲", "美咲ちゃん", "Misaki-chan")]);
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake);
        await vm.AddressTerms.SuggestCommand.ExecuteAsync(null);

        vm.Characters.Single(c => c.Source == "美咲").Source = "みさき";
        vm.Flush();

        var row = Assert.Single(vm.AddressTerms.Rows);
        Assert.Equal(("健太", "みさき", true), (row.From, row.To, row.IsSuggestion));
        Assert.False(File.Exists(folder.AddressTermsPath));
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

    [Fact]
    public async Task Renomear_de_novo_depois_da_falha_o_aviso_cita_o_nome_que_o_arquivo_ainda_tem()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);
        File.Delete(folder.AddressTermsPath);
        Directory.CreateDirectory(folder.AddressTermsPath);
        var misaki = vm.Characters.Single(c => c.Source == "美咲");

        misaki.Source = "みさき";
        vm.Flush();
        misaki.Source = "ミサキ";
        vm.Flush();

        Assert.True(vm.SaveFailed);
        Assert.Contains("美咲 agora é ミサキ", vm.ErrorMessage);
        Assert.DoesNotContain("みさき agora", vm.ErrorMessage);
    }

    [Fact]
    public async Task Descartar_depois_da_falha_entre_os_arquivos_avisa_quais_nomes_os_tratamentos_ainda_citam()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, terms: Both);
        File.Delete(folder.AddressTermsPath);
        Directory.CreateDirectory(folder.AddressTermsPath);
        vm.Characters.Single(c => c.Source == "美咲").Source = "みさき";
        vm.Flush();
        Directory.Delete(folder.AddressTermsPath);
        folder.SaveAddressTerms(Both);

        vm.DiscardChangesCommand.Execute(null);

        Assert.Contains("美咲", vm.NoticeMessage);
        Assert.Contains("みさき", vm.NoticeMessage);
        Assert.Contains("Tratamentos", vm.NoticeMessage);
    }

    private sealed class ThrowingAddressSuggester(Exception error) : IGlossarySuggester
    {
        public Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<CharacterVoiceSuggestion>>(error);

        public Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<TermTranslationSuggestion>>(error);

        public Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<AddressTermSuggestion>>(error);
    }
}
