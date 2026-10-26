using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Aviso de gênero, sugestões da IA (voz, gênero e termos que se repetem) e salvamento automático da página Glossário (#311).</summary>
public class GlossarySuggestionsTests
{
    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> OpenAsync(
        TempFolder temp, IGlossarySuggester? suggester = null, FakeTimeProvider? clock = null, IReadOnlyList<CharacterEntry>? characters = null)
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
    }

    [Fact]
    public async Task Avisa_quantos_personagens_estao_sem_genero_e_destaca_a_linha()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.Equal(1, vm.MissingGenderCount);
        Assert.True(vm.HasMissingGender);
        Assert.StartsWith("1 personagem está sem gênero.", vm.MissingGenderText);
        Assert.False(vm.Characters[0].NeedsGender);
        Assert.True(vm.Characters[1].NeedsGender);

        vm.Characters[1].Gender = CharacterGender.Male;

        Assert.Equal(0, vm.MissingGenderCount);
        Assert.False(vm.HasMissingGender);
        Assert.Equal(string.Empty, vm.MissingGenderText);
    }

    [Fact]
    public async Task Linha_em_branco_nao_conta_como_sem_genero()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddCharacterCommand.Execute(null);

        Assert.Equal(1, vm.MissingGenderCount);
    }

    [Fact]
    public async Task A_aba_de_personagens_abre_primeiro()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.Equal(GlossaryTab.Characters, vm.SelectedTab);
        Assert.Equal(0, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task Sugerir_voz_cobre_os_sem_genero_e_os_falantes_fora_da_tabela_e_nao_sobrescreve()
    {
        var fake = new FakeGlossarySuggester(voice: c => new CharacterVoiceSuggestion(c.Name, CharacterGender.Male, "direto"));
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, fake);

        Assert.True(vm.CanSuggestVoices);
        Assert.Equal(2, vm.VoiceRequestCount);

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        var asked = Assert.Single(fake.ReceivedVoices);
        Assert.Equal(["健太", "先生"], asked.Select(r => r.Name));
        Assert.Equal(["遅いぞ", "早くしろ", "行くぞ"], asked[0].Lines);
        Assert.DoesNotContain(vm.Characters, c => c.Source == "？？？");
        Assert.Equal(CharacterGender.Female, vm.Characters.Single(c => c.Source == "美咲").Gender);
        var kenta = vm.Characters.Single(c => c.Source == "健太");
        Assert.Equal(CharacterGender.Male, kenta.Gender);
        Assert.Equal("direto", kenta.Tone);
        var teacher = vm.Characters.Single(c => c.Source == "先生");
        Assert.Equal((CharacterGender.Male, string.Empty, "direto"), (teacher.Gender, teacher.Target, teacher.Tone));
        Assert.Equal(0, vm.MissingGenderCount);
        Assert.Contains("2 personagens receberam", vm.SuggestionNotice);
        Assert.Null(vm.SuggestionError);
        Assert.False(vm.IsSuggesting);
    }

    [Fact]
    public async Task Sugerir_voz_nao_troca_o_que_a_pessoa_ja_preencheu()
    {
        var fake = new FakeGlossarySuggester(voice: c => new CharacterVoiceSuggestion(c.Name, CharacterGender.Female, "sugerido"));
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, fake, characters: [new CharacterEntry("健太", "Kenta", CharacterGender.Unknown, "meu jeito")]);

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        var kenta = vm.Characters.Single(c => c.Source == "健太");
        Assert.Equal((CharacterGender.Female, "meu jeito"), (kenta.Gender, kenta.Tone));
    }

    [Fact]
    public async Task O_preco_da_sugestao_sai_em_reais_pela_cotacao_da_pessoa()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new FakeGlossarySuggester());
        var cheap = vm.VoicePriceText;

        vm.Rate = () => new ExchangeRate(5000m, new DateOnly(2026, 10, 25));
        vm.AddCharacterCommand.Execute(null);

        Assert.StartsWith("R$ ", vm.VoicePriceText);
        Assert.NotEqual(cheap, vm.VoicePriceText);
        Assert.DoesNotContain("US$", vm.VoicePriceText);
        Assert.DoesNotContain("cotação de exemplo", vm.VoicePriceText);
    }

    [Fact]
    public async Task Com_a_cotacao_de_exemplo_o_preco_leva_a_marca_nos_dois_botoes()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new FakeGlossarySuggester());
        vm.Rate = () => new ExchangeRate(5000m);
        vm.AddCharacterCommand.Execute(null);
        vm.RefreshPrices();

        Assert.StartsWith("≈ R$ ", vm.VoicePriceText);
        Assert.EndsWith("(cotação de exemplo)", vm.VoicePriceText);
        Assert.StartsWith("≈ R$ ", vm.TermsPriceText);
        Assert.EndsWith("(cotação de exemplo)", vm.TermsPriceText);
    }

    private static void BreakCharactersFile(ProjectFolder folder)
    {
        File.Delete(folder.CharactersPath);
        Directory.CreateDirectory(folder.CharactersPath);
    }

    [Fact]
    public async Task Nome_repetido_ou_vazio_nao_impede_gravar_as_outras_linhas()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);

        vm.Characters[1].Gender = CharacterGender.Male;
        vm.AddCharacterCommand.Execute(null);
        vm.Characters[^1].Source = "美咲";
        vm.AddCharacterCommand.Execute(null);
        vm.Characters[^1].Target = "Sem nome";
        vm.AddCharacterCommand.Execute(null);
        vm.Characters[^1].Source = "先生";
        vm.Characters[^1].Target = "Sensei";

        Assert.True(vm.Flush());

        Assert.Equal(["美咲", "健太", "先生"], folder.LoadCharacters().Select(c => c.Source));
        Assert.Equal(CharacterGender.Male, folder.LoadCharacters().Single(c => c.Source == "健太").Gender);
        Assert.Equal([false, false, true, true, false], vm.Characters.Select(c => c.HasProblem));
        Assert.Equal("Já existe outra linha com este nome.", vm.Characters[2].Problem);
        Assert.Equal("Falta o nome do personagem no jogo.", vm.Characters[3].Problem);
        Assert.Equal(2, vm.ProblemCount);
        Assert.StartsWith("2 linhas ainda não foram salvas", vm.ProblemsText);
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveFailed);
        Assert.Equal("Salvo automaticamente", vm.SaveStatus);
    }

    [Fact]
    public async Task Corrigir_a_linha_marcada_tira_o_aviso_e_grava_a_linha()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        vm.AddTermCommand.Execute(null);
        vm.Terms[^1].Target = "sem original";
        vm.Flush();
        Assert.Equal(1, vm.ProblemCount);

        vm.Terms[^1].Source = "文化祭";
        vm.Flush();

        Assert.Equal(0, vm.ProblemCount);
        Assert.False(vm.HasProblems);
        Assert.Contains(folder.LoadGlossary(), t => t.Source == "文化祭" && t.Target == "sem original");
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

    [Fact]
    public async Task Trocar_de_projeto_com_falha_de_gravacao_mantem_as_edicoes_e_espera_a_decisao()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        var original = vm.Project;
        vm.Characters[1].Tone = "casual";
        BreakCharactersFile(folder);
        var other = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated));

        Assert.False(vm.Open(other));

        Assert.Same(original, vm.Project);
        Assert.Same(other, vm.PendingProject);
        Assert.True(vm.HasPendingSwitch);
        Assert.Equal("Descartar e abrir o outro projeto", vm.DiscardLabel);
        Assert.Equal("casual", vm.Characters[1].Tone);
        Assert.True(vm.IsDirty);

        vm.DiscardChangesCommand.Execute(null);

        Assert.Same(other, vm.Project);
        Assert.Null(vm.PendingProject);
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveFailed);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Tentar_de_novo_abre_o_projeto_que_esperava_quando_a_gravacao_passa()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        vm.Characters[1].Tone = "casual";
        BreakCharactersFile(folder);
        var other = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated));
        vm.Open(other);

        vm.RetrySaveCommand.Execute(null);
        Assert.NotSame(other, vm.Project);

        Directory.Delete(folder.CharactersPath);
        File.WriteAllText(folder.CharactersPath, "{\"version\":1,\"entries\":[]}");
        vm.RetrySaveCommand.Execute(null);

        Assert.Same(other, vm.Project);
        Assert.Equal("casual", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    [Fact]
    public async Task Gravacao_automatica_que_passa_depois_da_falha_abre_o_projeto_que_esperava()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);
        vm.Characters[1].Tone = "casual";
        BreakCharactersFile(folder);
        var other = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated));
        vm.Open(other);
        Directory.Delete(folder.CharactersPath);

        vm.Characters[1].Tone = "casual de novo";
        clock.Advance(GlossaryViewModel.SaveDelay);

        Assert.Same(other, vm.Project);
        Assert.Null(vm.PendingProject);
        Assert.Equal("casual de novo", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    [Fact]
    public async Task Abrir_outro_projeto_grava_o_pendente_e_abre()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        vm.Characters[1].Tone = "casual";
        var other = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated));

        Assert.True(vm.Open(other));

        Assert.Same(other, vm.Project);
        Assert.Equal("casual", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    private sealed class RemovingSuggester(Action beforeAnswer) : IGlossarySuggester
    {
        public Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct)
        {
            beforeAnswer();
            IReadOnlyList<CharacterVoiceSuggestion> all = [.. characters.Select(c => new CharacterVoiceSuggestion(c.Name, CharacterGender.Male, "x"))];
            return Task.FromResult(all);
        }

        public Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Personagem_removido_durante_o_pedido_nao_volta_com_a_sugestao()
    {
        using var temp = new TempFolder();
        GlossaryViewModel? page = null;
        var (vm, _) = await OpenAsync(temp, new RemovingSuggester(() => page!.Characters.Remove(page.Characters.Single(c => c.Source == "健太"))));
        page = vm;

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Characters, c => c.Source == "健太");
        Assert.Contains(vm.Characters, c => c.Source == "先生");
    }

    [Fact]
    public async Task Nome_de_personagem_igual_ao_termo_tira_o_termo_do_painel()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        Assert.Single(vm.RepeatedTermRows);

        vm.Characters.Add(new CharacterRow("魔法学院", "Academia"));

        Assert.Empty(vm.RepeatedTermRows);
    }

    [Fact]
    public async Task Mudar_a_cotacao_refaz_o_preco_mostrado()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new FakeGlossarySuggester());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RefreshPrices();

        Assert.Contains(nameof(GlossaryViewModel.VoicePriceText), changed);
        Assert.Contains(nameof(GlossaryViewModel.TermsPriceText), changed);
    }

    [Fact]
    public async Task Sem_provedor_ou_sem_o_que_sugerir_o_botao_fica_desligado()
    {
        using var temp = new TempFolder();
        var (withoutSuggester, _) = await OpenAsync(temp);
        Assert.False(withoutSuggester.CanSuggestVoices);
        Assert.False(withoutSuggester.SuggestVoicesCommand.CanExecute(null));

        using var other = new TempFolder();
        var (done, _) = await OpenAsync(
            other,
            new FakeGlossarySuggester(),
            characters: [new CharacterEntry("美咲", "Misaki", CharacterGender.Female), new CharacterEntry("健太", "Kenta", CharacterGender.Male), new CharacterEntry("先生", "Sensei", CharacterGender.Male)]);
        Assert.False(done.CanSuggestVoices);
        Assert.Equal(string.Empty, done.VoicePriceText);
    }

    [Fact]
    public async Task Recusa_da_ia_vira_mensagem_e_nao_muda_a_tabela()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new ThrowingSuggester(new ClaudeRefusalException("O modelo recusou fazer esta sugestão (stop_reason: refusal).")));

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        Assert.Equal("O modelo recusou fazer esta sugestão (stop_reason: refusal).", vm.SuggestionError);
        Assert.Null(vm.SuggestionNotice);
        Assert.Equal(CharacterGender.Unknown, vm.Characters.Single(c => c.Source == "健太").Gender);
        Assert.False(vm.IsSuggesting);
        Assert.True(vm.CanSuggestVoices);
    }

    [Theory]
    [InlineData("refusal", "", "A IA não quis sugerir para este lote. Tente de novo ou preencha à mão.")]
    [InlineData("max_tokens", "", "A IA não terminou a resposta. Tente de novo ou preencha à mão.")]
    [InlineData("end_turn", "{ lixo", "A resposta da IA veio incompleta ou fora do formato. Tente de novo ou preencha à mão.")]
    public async Task A_tela_mostra_so_a_frase_para_a_pessoa_quando_a_ia_falha(string stopReason, string text, string expected)
    {
        using var temp = new TempFolder();
        var client = new StubClaudeClient(new ClaudeReply(stopReason, text));
        var (vm, _) = await OpenAsync(temp, new ClaudeGlossarySuggester(client));

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.SuggestionError);
        foreach (var technical in new[] { "stop_reason", "token", "modelo", "json" })
        {
            Assert.DoesNotContain(technical, vm.SuggestionError, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class StubClaudeClient(ClaudeReply reply) : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) => Task.FromResult(reply);
    }

    [Fact]
    public async Task Resposta_invalida_da_ia_vira_mensagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new ThrowingSuggester(new ClaudeResponseException("A resposta do Claude não é o JSON esperado.")));

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        Assert.Equal("A resposta do Claude não é o JSON esperado.", vm.SuggestionError);
    }

    [Fact]
    public async Task Sem_chave_a_mensagem_manda_informar_a_chave_em_ajustes()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new UserGlossarySuggester(() => null));

        await vm.SuggestVoicesCommand.ExecuteAsync(null);

        Assert.Equal("Informe a chave de API em Ajustes para pedir sugestões à IA.", vm.SuggestionError);
    }

    [Fact]
    public async Task O_sugestor_do_usuario_reaproveita_o_provedor_enquanto_a_chave_nao_muda()
    {
        var created = 0;
        var key = "chave-1";
        var suggester = UserGlossarySuggester.ForUser("sem-pasta", _ => key, _ =>
        {
            created++;
            return new FakeGlossarySuggester();
        });
        IReadOnlyList<CharacterVoiceRequest> one = [new("美咲", ["あ"])];

        await suggester.SuggestVoicesAsync(one, CancellationToken.None);
        await suggester.SuggestVoicesAsync(one, CancellationToken.None);
        key = "chave-2";
        await suggester.SuggestVoicesAsync(one, CancellationToken.None);

        Assert.Equal(2, created);
    }

    [Fact]
    public async Task Painel_lista_os_termos_que_se_repetem_fora_do_glossario()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        var row = Assert.Single(vm.RepeatedTermRows);
        Assert.Equal(("魔法学院", 6), (row.Source, row.Lines));
        Assert.Equal("6 linhas", row.LinesLabel);
        Assert.True(vm.HasRepeatedTerms);
        Assert.False(vm.NoRepeatedTerms);
        Assert.Equal("1 termo aparece 5 vezes ou mais e não está no glossário.", vm.RepeatedSummary);
    }

    [Fact]
    public async Task Termo_que_ja_esta_no_glossario_ou_vira_personagem_some_do_painel()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddTermCommand.Execute(null);
        vm.Terms[^1].Source = "魔法学院";

        Assert.Empty(vm.RepeatedTermRows);
        Assert.True(vm.NoRepeatedTerms);
        Assert.Equal("Nenhum termo novo aparece 5 vezes ou mais.", vm.RepeatedSummary);
    }

    [Fact]
    public async Task Traduzir_com_a_ia_preenche_traducao_e_motivo_e_envia_uma_linha_de_exemplo()
    {
        var fake = new FakeGlossarySuggester(term: t => new TermTranslationSuggestion(t.Source, "Academia de Magia", "Nome da escola."));
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, fake);
        Assert.True(vm.CanSuggestTerms);
        Assert.Contains("R$", vm.TermsPriceText);

        await vm.SuggestTermsCommand.ExecuteAsync(null);

        var asked = Assert.Single(Assert.Single(fake.ReceivedTerms));
        Assert.Equal(("魔法学院", 6), (asked.Source, asked.Lines));
        Assert.Contains("魔法学院", asked.Example);
        var row = Assert.Single(vm.RepeatedTermRows);
        Assert.Equal(("Academia de Magia", "Nome da escola."), (row.Target, row.Reason));
        Assert.True(row.HasSuggestion);
        Assert.False(vm.CanSuggestTerms);
        Assert.Equal(string.Empty, vm.TermsPriceText);
        Assert.Null(vm.SuggestionError);
    }

    [Fact]
    public async Task O_exemplo_enviado_e_uma_janela_em_volta_do_termo_sem_partir_par_substituto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("longa"), "Longa", TranslationStatus.Untranslated);
        var longLine = new string('あ', 29) + "𠮷" + new string('あ', 29) + "魔法学院" + new string('あ', 29) + "𠮷" + new string('あ', 20);
        ProjectFolder.Open(root).SaveText("yst00001", Enumerable.Range(0, 5).Select(n => Line(n, longLine)).ToList());
        var fake = new FakeGlossarySuggester();
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), Suggester = fake };
        await vm.Loading;

        await vm.SuggestTermsCommand.ExecuteAsync(null);

        var example = Assert.Single(Assert.Single(fake.ReceivedTerms)).Example!;
        Assert.Contains("魔法学院", example);
        Assert.True(example.Length <= 4 + (2 * (30 + 1)));
        Assert.False(char.IsLowSurrogate(example[0]));
        Assert.False(char.IsHighSurrogate(example[^1]));
        Assert.Equal(0, example.Count(char.IsSurrogate) % 2);
    }

    [Fact]
    public async Task Adicionar_poe_o_termo_na_tabela_com_a_traducao_sugerida_e_tira_do_painel()
    {
        var fake = new FakeGlossarySuggester(term: t => new TermTranslationSuggestion(t.Source, "Academia de Magia", "Nome da escola."));
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake);
        await vm.SuggestTermsCommand.ExecuteAsync(null);

        vm.AddRepeatedTermCommand.Execute(vm.RepeatedTermRows[0]);

        Assert.Empty(vm.RepeatedTermRows);
        var added = vm.Terms[^1];
        Assert.Equal(("魔法学院", "Academia de Magia", string.Empty), (added.Source, added.Target, added.Notes));
        Assert.True(vm.IsDirty);
        vm.Flush();
        Assert.Contains(folder.LoadGlossary(), t => t.Source == "魔法学院" && t.Target == "Academia de Magia");
    }

    [Fact]
    public async Task Adicionar_sem_traducao_leva_o_termo_com_o_campo_vazio_para_a_pessoa_preencher()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddRepeatedTermCommand.Execute(vm.RepeatedTermRows[0]);

        Assert.Equal(("魔法学院", string.Empty), (vm.Terms[^1].Source, vm.Terms[^1].Target));
    }

    [Fact]
    public async Task Falha_ao_traduzir_termos_mostra_o_motivo_e_mantem_o_painel()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new ThrowingSuggester(new ClaudeRefusalException("recusou")));

        await vm.SuggestTermsCommand.ExecuteAsync(null);

        Assert.Equal("recusou", vm.SuggestionError);
        Assert.False(Assert.Single(vm.RepeatedTermRows).HasSuggestion);
    }

    [Fact]
    public async Task Salva_sozinho_depois_do_atraso_e_so_depois()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);

        vm.Characters[1].Gender = CharacterGender.Male;
        vm.Characters[1].Tone = "casual";

        Assert.True(vm.IsDirty);
        Assert.Equal("Salvando…", vm.SaveStatus);
        clock.Advance(GlossaryViewModel.SaveDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(CharacterGender.Unknown, folder.LoadCharacters().Single(c => c.Source == "健太").Gender);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        var saved = folder.LoadCharacters().Single(c => c.Source == "健太");
        Assert.Equal((CharacterGender.Male, "casual"), (saved.Gender, saved.Tone));
        Assert.False(vm.IsDirty);
        Assert.Equal("Salvo automaticamente", vm.SaveStatus);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Cada_edicao_adia_a_gravacao()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);

        vm.Characters[1].Tone = "a";
        clock.Advance(GlossaryViewModel.SaveDelay - TimeSpan.FromMilliseconds(100));
        vm.Characters[1].Tone = "ab";
        clock.Advance(GlossaryViewModel.SaveDelay - TimeSpan.FromMilliseconds(100));

        Assert.True(vm.IsDirty);
        Assert.Null(folder.LoadCharacters().Single(c => c.Source == "健太").Tone);

        clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal("ab", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    [Fact]
    public async Task Sem_relogio_nada_grava_sozinho()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);

        vm.Characters[1].Tone = "casual";

        Assert.True(vm.IsDirty);
        Assert.Null(folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
    }

    [Fact]
    public async Task Flush_grava_na_hora_e_o_atraso_vencido_depois_nao_grava_de_novo()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);

        vm.Characters[1].Tone = "casual";
        vm.Flush();

        Assert.False(vm.IsDirty);
        Assert.Equal("casual", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
        File.Delete(folder.CharactersPath);
        clock.Advance(GlossaryViewModel.SaveDelay * 2);
        Assert.False(File.Exists(folder.CharactersPath));
    }

    [Fact]
    public async Task Trocar_de_projeto_grava_o_que_estava_pendente_no_projeto_antigo()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);
        vm.Characters[1].Tone = "casual";

        var otherRoot = SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Untranslated);
        vm.Project = ProjectSummaryReader.Read(otherRoot);

        Assert.Equal("casual", folder.LoadCharacters().Single(c => c.Source == "健太").Tone);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task O_arquivo_de_personagens_continua_no_formato_que_o_QA_le()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);
        vm.Characters[1].Gender = CharacterGender.Female;
        vm.Characters[1].Tone = "formal";

        vm.Flush();

        var json = File.ReadAllText(folder.CharactersPath);
        Assert.Contains("\"gender\":\"female\"", json.Replace(" ", string.Empty));
        Assert.Contains("\"tone\":\"formal\"", json.Replace(" ", string.Empty));
        Assert.Equal(new CharacterEntry("健太", "Kenta", CharacterGender.Female, "formal"), folder.LoadCharacters().Single(c => c.Source == "健太"));
    }

    [Fact]
    public async Task Arquivo_ilegivel_nao_agenda_gravacao()
    {
        var clock = new FakeTimeProvider();
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, clock: clock);
        File.WriteAllText(folder.CharactersPath, "{ isto não é json");
        vm.Reload();

        Assert.False(vm.CanEdit);
        clock.Advance(GlossaryViewModel.SaveDelay * 2);

        Assert.Equal("{ isto não é json", File.ReadAllText(folder.CharactersPath));
    }
}
