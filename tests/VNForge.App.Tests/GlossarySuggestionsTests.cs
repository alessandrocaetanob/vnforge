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
        var teacher = vm.Characters.Single(c => c.Source == "先生");

        // A resposta da IA fica como sugestão: o gênero e o jeito de falar só mudam no aceite.
        Assert.Equal((CharacterGender.Unknown, string.Empty), (kenta.Gender, kenta.Tone));
        Assert.Equal((CharacterGender.Male, "direto", true), (kenta.SuggestedGender, kenta.SuggestedTone, kenta.HasSuggestion));
        Assert.Equal((CharacterGender.Unknown, string.Empty, CharacterGender.Male), (teacher.Gender, teacher.Target, teacher.SuggestedGender));
        Assert.Equal("direto", kenta.TonePlaceholder);
        Assert.Equal(2, vm.PendingSuggestionCount);
        Assert.Equal("Aceitar as 2", vm.AcceptAllText);
        Assert.Contains("2 personagens têm sugestão", vm.SuggestionNotice);
        Assert.Null(vm.SuggestionError);
        Assert.False(vm.IsSuggesting);

        vm.AcceptAllSuggestionsCommand.Execute(null);

        Assert.Equal((CharacterGender.Male, "direto"), (kenta.Gender, kenta.Tone));
        Assert.Equal((CharacterGender.Male, string.Empty, "direto"), (teacher.Gender, teacher.Target, teacher.Tone));
        Assert.Equal(0, vm.MissingGenderCount);
        Assert.Equal(0, vm.PendingSuggestionCount);
        Assert.False(vm.HasPendingSuggestions);
        Assert.Equal("Como fala", kenta.TonePlaceholder);
    }

    [Fact]
    public async Task Aceitar_suja_a_pagina_mas_sugerir_e_descartar_nao()
    {
        var fake = new FakeGlossarySuggester(voice: c => new CharacterVoiceSuggestion(c.Name, CharacterGender.Female, "calma"));
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, fake, characters: [new CharacterEntry("美咲", "Misaki", CharacterGender.Female), new CharacterEntry("健太", "Kenta"), new CharacterEntry("先生", "Sensei")]);
        var kenta = vm.Characters.Single(c => c.Source == "健太");
        Assert.False(vm.IsDirty);

        await vm.SuggestVoicesCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.PendingSuggestionCount);
        Assert.False(vm.IsDirty);

        vm.DismissSuggestionCommand.Execute(kenta);
        Assert.False(vm.IsDirty);
        Assert.Equal(CharacterGender.Unknown, folder.LoadCharacters().Single(c => c.Source == "健太").Gender);

        vm.AcceptSuggestionCommand.Execute(vm.Characters.Single(c => c.Source == "先生"));
        Assert.True(vm.IsDirty);
        Assert.True(vm.Flush());
        Assert.Equal(CharacterGender.Female, folder.LoadCharacters().Single(c => c.Source == "先生").Gender);
        Assert.Equal(CharacterGender.Unknown, folder.LoadCharacters().Single(c => c.Source == "健太").Gender);
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

    [Fact]
    public async Task Com_a_cotacao_de_exemplo_o_preco_leva_so_o_aproximado_e_a_marca_vai_para_a_dica_dos_dois_botoes()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, new FakeGlossarySuggester());
        vm.Rate = () => new ExchangeRate(5000m);
        vm.AddCharacterCommand.Execute(null);
        vm.RefreshPrices();

        Assert.StartsWith("≈ ", vm.VoicePriceText);
        Assert.DoesNotContain("cotação de exemplo", vm.VoicePriceText);
        Assert.StartsWith("≈ ", vm.TermsPriceText);
        Assert.DoesNotContain("cotação de exemplo", vm.TermsPriceText);
        Assert.Contains("cotação de exemplo", vm.VoiceHint);
        Assert.Contains("cotação de exemplo", vm.TermsHint);

        vm.Rate = () => new ExchangeRate(5000m, new DateOnly(2026, 10, 25));
        vm.RefreshPrices();

        Assert.DoesNotContain("cotação de exemplo", vm.VoiceHint);
        Assert.DoesNotContain("cotação de exemplo", vm.TermsHint);
    }

    private static void BreakCharactersFile(ProjectFolder folder)
    {
        File.Delete(folder.CharactersPath);
        Directory.CreateDirectory(folder.CharactersPath);
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

        public Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct) =>
            throw new NotSupportedException();
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

    // Três falas com 新入生 em que a IA escreveu "novato" e a pessoa gravou "calouro".
    private static IEnumerable<TextUnit> CorrectedLines() =>
        Enumerable.Range(60, 3).Select(n => new TextUnit($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, $"新入生その{n}", $"O calouro número {n}.", TranslationStatus.Translated)
        {
            AiTranslation = $"O novato número {n}.",
        });

    [Fact]
    public async Task Oferece_o_par_do_trecho_que_a_pessoa_traduziu_do_mesmo_jeito_em_varias_falas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, extraUnits: CorrectedLines());

        var row = Assert.Single(vm.CorrectedTermRows);

        Assert.Equal(("新入生", "calouro", "novato", "3 falas"), (row.Source, row.Target, row.From, row.LinesLabel));
        Assert.True(vm.HasCorrectedTerms);
        Assert.Equal("1 trecho ganhou a mesma tradução em várias falas que você corrigiu.", vm.CorrectedSummary);
    }

    [Fact]
    public async Task Adicionar_o_par_poe_o_termo_na_tabela_e_tira_do_painel()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, extraUnits: CorrectedLines());

        vm.AddCorrectedTermCommand.Execute(vm.CorrectedTermRows[0]);

        Assert.Empty(vm.CorrectedTermRows);
        Assert.Equal(("新入生", "calouro"), (vm.Terms[^1].Source, vm.Terms[^1].Target));
        Assert.True(vm.IsDirty);
        vm.Flush();
        Assert.Contains(folder.LoadGlossary(), t => t.Source == "新入生" && t.Target == "calouro");
    }

    [Fact]
    public async Task Os_comandos_de_aceitar_e_adicionar_ignoram_linha_ausente_ou_sem_sugestao()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        var termos = vm.Terms.Count;
        var semSugestao = vm.Characters[0];

        vm.AcceptSuggestionCommand.Execute(null);
        vm.AcceptSuggestionCommand.Execute(semSugestao);
        vm.AddRepeatedTermCommand.Execute(null);
        vm.AddCorrectedTermCommand.Execute(null);

        Assert.Equal(termos, vm.Terms.Count);
        Assert.Equal(CharacterGender.Female, semSugestao.Gender);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Sem_projeto_adicionar_um_termo_do_painel_nao_mexe_na_tabela()
    {
        var vm = new GlossaryViewModel();

        vm.AddRepeatedTermCommand.Execute(new RepeatedTermRow("魔法学院", 6) { Target = "Academia" });
        vm.AddCorrectedTermCommand.Execute(new CorrectedTermRow(new TermPair("先輩", "veterano", "colega mais velho", 3)));

        Assert.False(vm.CanEdit);
        Assert.Empty(vm.Terms);
    }

}
