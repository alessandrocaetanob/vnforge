using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A aba Ficha do jogo (d9, #554): estados, spoiler por nível, tradução e a revisão por blocos, com o catálogo e o tradutor falsos.</summary>
public sealed class GameSheetViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();
    private readonly List<(string Text, ToastKind Kind)> _notices = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _config.Dispose();

    private GameSheetViewModel Create(bool translator = true, bool catalog = true, string? directory = null) =>
        new()
        {
            ConfigDirectory = directory ?? _config.Path,
            CatalogFactory = catalog ? GameSheetSamples.Catalog : null,
            TranslatorFactory = translator ? GameSheetSamples.Translator : null,
            Clock = new FixedClock(GameSheetSamples.Now),
            Notify = (text, kind) => _notices.Add((text, kind)),
        };

    private async Task<GameSheetViewModel> ReadyAsync(bool translate = true, string? directory = null)
    {
        await GameSheetSamples.SeedAsync(directory ?? _config.Path, translate, Ct);
        var vm = Create(directory: directory);
        vm.Open(GameSheetSamples.Id, "Amaoto no Hakoniwa");
        await vm.Loading;
        return vm;
    }

    private SheetStore Store => new(_config.Path);

    [Fact]
    public void Sem_o_vinculo_com_o_VNDB_a_ficha_diz_isso_e_nao_oferece_acao()
    {
        var vm = Create();

        vm.Open(null, "Minha VN");

        Assert.Equal(GameSheetState.NoLink, vm.State);
        Assert.True(vm.ShowEmpty);
        Assert.False(vm.IsReady);
        Assert.Contains("VNDB", vm.EmptyMessage, StringComparison.Ordinal);
        Assert.Null(vm.EmptyActionText);
        Assert.Null(vm.EmptyActionCommand);
        Assert.False(vm.StartReviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sem_a_pasta_de_configuracao_a_leitura_falha_com_uma_frase()
    {
        var vm = new GameSheetViewModel();

        vm.Open(GameSheetSamples.Id, "Jogo");
        await vm.Loading;

        Assert.Equal(GameSheetState.Failed, vm.State);
        Assert.Contains("configuração", vm.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ficha_ilegivel_mostra_o_erro_e_tentar_de_novo_le_outra_vez()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Store.GamePath(GameSheetSamples.Id))!);
        await File.WriteAllTextAsync(Store.GamePath(GameSheetSamples.Id), "{ não é json", Ct);
        var vm = Create();

        vm.Open(GameSheetSamples.Id, "Jogo");
        await vm.Loading;

        Assert.Equal(GameSheetState.Failed, vm.State);
        Assert.Contains(Store.GamePath(GameSheetSamples.Id), vm.EmptyMessage, StringComparison.Ordinal);
        Assert.Equal("Tentar de novo", vm.EmptyActionText);

        File.Delete(Store.GamePath(GameSheetSamples.Id));
        await GameSheetSamples.SeedAsync(_config.Path, translate: false, Ct);
        vm.EmptyActionCommand!.Execute(null);
        await vm.Loading;

        Assert.Equal(GameSheetState.Ready, vm.State);
    }

    [Fact]
    public async Task Ficha_nao_buscada_oferece_buscar_e_depois_de_buscar_abre_a_ficha()
    {
        var vm = Create();
        vm.Open(GameSheetSamples.Id, "Amaoto no Hakoniwa");
        await vm.Loading;

        Assert.Equal(GameSheetState.NotFetched, vm.State);
        Assert.Equal("Buscar a ficha no VNDB", vm.EmptyActionText);
        Assert.Contains("ainda não foi buscada", vm.EmptyMessage, StringComparison.Ordinal);

        await vm.FetchCommand.ExecuteAsync(null);

        Assert.Equal(GameSheetState.Ready, vm.State);
        Assert.False(vm.IsBusy);
        Assert.Single(_notices, n => n.Kind == ToastKind.Success && n.Text.Contains("7 personagens", StringComparison.Ordinal) && n.Text.Contains("8 tags", StringComparison.Ordinal));
        Assert.True(vm.HasPending);
        Assert.Equal("Traduzir com a IA", vm.TranslateLabel);
    }

    [Fact]
    public async Task Buscar_um_jogo_que_o_VNDB_nao_tem_avisa_e_deixa_a_ficha_como_estava()
    {
        var vm = Create();
        vm.Open("v1", "Outro");
        await vm.Loading;

        await vm.FetchCommand.ExecuteAsync(null);

        Assert.Equal(GameSheetState.NotFetched, vm.State);
        Assert.Single(_notices, n => n.Kind == ToastKind.Error && n.Text.Contains("v1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Buscar_sem_catalogo_configurado_avisa()
    {
        var vm = Create(catalog: false);
        vm.Open(GameSheetSamples.Id, "Jogo");
        await vm.Loading;

        await vm.FetchCommand.ExecuteAsync(null);

        Assert.Single(_notices, n => n.Kind == ToastKind.Warning);
        Assert.Equal(GameSheetState.NotFetched, vm.State);
    }

    [Fact]
    public async Task A_ficha_pronta_mostra_sinopse_dados_tags_e_personagens_como_no_quadro()
    {
        var vm = await ReadyAsync();

        Assert.True(vm.IsReady);
        Assert.False(vm.ShowEmpty);
        Assert.Contains("Numa cidade onde chove", vm.SynopsisText, StringComparison.Ordinal);
        Assert.Equal(new SheetPill("Revisada", SheetTone.Reviewed), vm.SynopsisPill);
        Assert.Equal("Média · 18 h (47 votos)", vm.DurationText);
        Assert.Equal("2019, em japonês", vm.ReleasedText);
        Assert.Equal("7,9 de 10", vm.RatingText);
        Assert.Equal(GameSheetSamples.Id, vm.VndbIdText);
        Assert.Equal(["Drama escolar", "Mistério", "Slice of life", "Chuva", "Clube de leitura", "Caixa de música", "2 tags com spoiler"], vm.Tags.Select(t => t.Text));
        Assert.True(vm.Tags[^1].IsHidden);
        Assert.Equal(new SheetPill("Traduzidas pela IA, valem para todos os jogos", SheetTone.Ai), vm.TagsPill);
        Assert.Equal("· 7 no VNDB", vm.CharactersCount);
        Assert.Equal(6, vm.CharacterTiles.Count);
        Assert.True(vm.CanShowAll);
        var kenta = vm.CharacterTiles[0];
        Assert.Equal(("Kenta Aizawa", "相沢 健太", "Protagonista", "Aprovado", "Cabelo curto, Esportista"), (kenta.Name, kenta.Original, kenta.Role!.Text, kenta.Status!.Text, kenta.Line));
        Assert.Equal(SheetTone.Approved, kenta.Role.Tone);
        Assert.Equal(["Protagonista", "Principal", "Principal", "Secundário", "Secundária", "Secundária"], vm.CharacterTiles.Select(t => t.Role!.Text));
        Assert.Equal(["Aprovado", "Revisado", "Traduzido", "Traduzido pela IA", "Traduzido pela IA", "Traduzido pela IA"], vm.CharacterTiles.Select(t => t.Status!.Text));
        Assert.Equal(SheetTone.Pending, vm.CharacterTiles[1].Role!.Tone);
    }

    [Fact]
    public async Task Ver_todos_mostra_o_setimo_cartao_que_esta_escondido_por_spoiler()
    {
        var vm = await ReadyAsync();

        vm.ToggleAllCharactersCommand.Execute(null);

        Assert.Equal(7, vm.CharacterTiles.Count);
        Assert.Equal("Ver menos", vm.ShowAllText);
        var hidden = vm.CharacterTiles[^1];
        Assert.True(hidden.ShowsHidden);
        Assert.Equal(("Nome escondido", "Spoiler de nível 2", 2), (hidden.Name, hidden.Caption, hidden.HiddenLevel));
        Assert.False(hidden.HasPills);
        vm.ToggleAllCharactersCommand.Execute(null);
        Assert.Equal(6, vm.CharacterTiles.Count);
    }

    [Fact]
    public async Task O_spoiler_vem_escondido_e_sobe_um_nivel_por_vez()
    {
        var vm = await ReadyAsync();
        Assert.Equal("Escondido", vm.SelectedSpoiler.Label);

        vm.SelectedSpoiler = vm.SpoilerOptions[1];

        Assert.Equal("Mostrando nível 1", vm.SpoilerLevelText);
        Assert.Equal(["Drama escolar", "Mistério", "Slice of life", "Chuva", "Clube de leitura", "Caixa de música", "Final surpresa", "1 tag com spoiler"], vm.Tags.Select(t => t.Text));
        vm.SelectedSpoiler = vm.SpoilerOptions[2];

        Assert.DoesNotContain("[spoiler escondido]", vm.SynopsisText, StringComparison.Ordinal);
        Assert.Equal(8, vm.Tags.Count(t => !t.IsHidden));
        vm.ToggleAllCharactersCommand.Execute(null);
        Assert.DoesNotContain(vm.CharacterTiles, t => t.ShowsHidden);
        Assert.Contains(vm.CharacterTiles, t => t.Name == "Ghost");
        vm.SelectedSpoiler = null!;
        Assert.Equal(2, vm.SpoilerLevel);
    }

    [Fact]
    public async Task Mostrar_num_cartao_escondido_sobe_o_nivel_ate_o_que_ele_pede()
    {
        var vm = await ReadyAsync();
        vm.ToggleAllCharactersCommand.Execute(null);
        var hidden = vm.CharacterTiles[^1];

        vm.RevealCommand.Execute(vm.CharacterTiles[0]);
        Assert.Equal(0, vm.SpoilerLevel);
        vm.RevealCommand.Execute(null);
        Assert.Equal(0, vm.SpoilerLevel);
        vm.RevealCommand.Execute(hidden);

        Assert.Equal(2, vm.SpoilerLevel);
        Assert.DoesNotContain(vm.CharacterTiles, t => t.ShowsHidden);
        vm.CycleSpoilerCommand.Execute(null);
        Assert.Equal(0, vm.SpoilerLevel);
    }

    /// <summary>A mesma ficha com uma tag erótica e o retrato do primeiro personagem marcado como sexual, no modo adulto da Biblioteca.</summary>
    private async Task<GameSheetViewModel> AdultAsync(AdultMode mode, AdultReveal? reveals = null)
    {
        var game = GameSheetSamples.Game() with { Tags = [.. GameSheetSamples.Game().Tags, new VndbTag("g9", "Nudity", "ero", 2.5, 0, false)] };
        var characters = GameSheetSamples.Characters();
        characters[0] = characters[0] with { Image = new VndbImage("https://t.vndb.org/ch/1.jpg", null, 100, 140, 2, 0) };
        await SheetFetcher.FetchAsync(new FakeVndbCatalog([game], characters), Store, GameSheetSamples.Id, new FixedClock(GameSheetSamples.Now), Ct);
        new LibraryFile(_config.Path).Save(new GameLibrary([new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto", null, GameSheetSamples.Now) { VndbId = GameSheetSamples.Id }], new LibraryPrivacy(Adult: mode)));
        var vm = Create();
        if (reveals is not null)
        {
            vm.Reveals = reveals;
        }

        vm.Open(GameSheetSamples.Id, "Amaoto");
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task No_mostrar_a_ficha_traz_a_tag_erotica_e_o_retrato_sem_borrao()
    {
        var blurred = await AdultAsync(AdultMode.Blur);
        var vm = await AdultAsync(AdultMode.Show);

        Assert.Equal(blurred.Tags.Count(t => !t.IsHidden) + 1, vm.Tags.Count(t => !t.IsHidden));
        Assert.DoesNotContain(vm.Tags, t => t.IsHidden && t.Text.Contains("adulto", StringComparison.Ordinal));
        Assert.Equal(TileKind.Portrait, vm.CharacterTiles[0].Kind);
        Assert.False(vm.CharacterTiles[0].ShowsImageBlurred);
    }

    [Fact]
    public async Task No_desfocar_o_retrato_adulto_vem_borrado_e_as_tags_ficam_escondidas_com_o_mostrar()
    {
        var vm = await AdultAsync(AdultMode.Blur);

        var hiddenTags = vm.Tags.Single(t => t.IsHidden && t.Text.Contains("adulto", StringComparison.Ordinal));
        Assert.True(hiddenTags.CanReveal);
        Assert.Equal(TileKind.ImageBlurred, vm.CharacterTiles[0].Kind);
        Assert.True(vm.CharacterTiles[0].ShowsPortrait);
        Assert.True(vm.CharacterTiles[0].ShowsImageBlurred);
        Assert.NotNull(vm.CharacterTiles[0].Image);
    }

    [Fact]
    public async Task Revelar_na_ficha_libera_o_jogo_inteiro_e_vale_tambem_para_o_registro_da_Biblioteca()
    {
        var reveals = new AdultReveal();
        var vm = await AdultAsync(AdultMode.Blur, reveals);

        vm.RevealAdultCommand.Execute(null);

        Assert.True(reveals.IsRevealed(GameSheetSamples.Id));
        Assert.DoesNotContain(vm.Tags, t => t.IsHidden && t.Text.Contains("adulto", StringComparison.Ordinal));
        Assert.Equal(TileKind.Portrait, vm.CharacterTiles[0].Kind);
        Assert.True(vm.CharacterTiles[0].Revealed);
        Assert.False(vm.CharacterTiles[0].ShowsImageBlurred);
    }

    [Fact]
    public async Task No_esconder_o_retrato_adulto_volta_ao_quadro_tracejado_e_o_cartao_do_personagem_fica()
    {
        var vm = await AdultAsync(AdultMode.Hide);

        Assert.Equal(TileKind.ImageHidden, vm.CharacterTiles[0].Kind);
        Assert.False(vm.CharacterTiles[0].ShowsPortrait);
        Assert.Equal("Imagem de conteúdo adulto escondida", vm.CharacterTiles[0].Caption);
        Assert.Equal(6, vm.CharacterTiles.Count);
        Assert.DoesNotContain(vm.Tags, t => t is { IsHidden: true, CanReveal: true });
    }

    [Fact]
    public async Task O_nivel_inicial_vem_da_privacidade_da_Biblioteca_e_as_tags_suas_aparecem()
    {
        await GameSheetSamples.SeedAsync(_config.Path, true, Ct);
        var game = new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto", null, GameSheetSamples.Now) { VndbId = GameSheetSamples.Id };
        new LibraryFile(_config.Path).Save(new GameLibrary([game], new LibraryPrivacy(ShowSpoilers: true)));
        new UserTagsFile(_config.Path).Update(tags => tags.Add("g1", "favorito"));
        var vm = Create();

        vm.Open(GameSheetSamples.Id, "Amaoto");
        await vm.Loading;

        Assert.Equal(2, vm.SpoilerLevel);
        Assert.Contains(vm.Tags, t => t is { Text: "favorito", IsMine: true });
        Assert.DoesNotContain(vm.Tags, t => t.IsHidden);
    }

    [Fact]
    public async Task Reler_a_ficha_mantem_o_nivel_que_a_pessoa_escolheu_e_abrir_outro_jogo_volta_ao_da_Biblioteca()
    {
        var vm = await ReadyAsync();
        vm.SelectedSpoiler = vm.SpoilerOptions[1];

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.SpoilerLevel);
        vm.Open(GameSheetSamples.Id, "Amaoto");
        await vm.Loading;
        Assert.Equal(0, vm.SpoilerLevel);
    }

    [Fact]
    public async Task Ver_o_original_alterna_o_texto_do_VNDB_e_some_quando_nao_ha_traducao()
    {
        var vm = await ReadyAsync();

        Assert.True(vm.CanToggleOriginal);
        Assert.Equal("Ver o original", vm.SynopsisToggleText);
        vm.ToggleOriginalCommand.Execute(null);

        Assert.StartsWith("A town where it rains all year.", vm.SynopsisText, StringComparison.Ordinal);
        Assert.Equal("Ver a tradução", vm.SynopsisToggleText);

        using var other = new TempFolder();
        var raw = await ReadyAsync(translate: false, other.Path);
        Assert.False(raw.CanToggleOriginal);
        Assert.Equal(new SheetPill("Sem tradução", SheetTone.Pending), raw.SynopsisPill);
        Assert.StartsWith("A town where it rains", raw.SynopsisText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_texto_em_portugues_conta_os_blocos_por_etapa_e_o_botao_diz_se_e_de_novo()
    {
        var vm = await ReadyAsync();

        Assert.Equal(new SheetStatusCounts(0, 4, 1, 2, 1), vm.BlockCounts);
        Assert.Equal(["1 aprovado", "2 revisados", "1 traduzido", "4 pela IA"], vm.CountPills.Select(p => p.Text));
        Assert.Equal("Traduzir de novo com a IA", vm.TranslateLabel);

        using var other = new TempFolder();
        var raw = await ReadyAsync(translate: false, other.Path);
        Assert.Equal(["8 sem tradução"], raw.CountPills.Select(p => p.Text));
        Assert.Equal("Traduzir com a IA", raw.TranslateLabel);
    }

    [Fact]
    public async Task Traduzir_uma_ficha_sem_traducao_traduz_tudo_e_avisa()
    {
        var vm = await ReadyAsync(translate: false);

        await vm.TranslateCommand.ExecuteAsync(null);

        Assert.False(vm.HasPending);
        Assert.Equal(new SheetStatusCounts(0, 8, 0, 0, 0), vm.BlockCounts);
        Assert.Single(_notices, n => n.Kind == ToastKind.Success && n.Text.Contains("textos traduzidos pela IA", StringComparison.Ordinal));
        Assert.False(vm.IsBusy);
        Assert.Equal(string.Empty, vm.BusyText);
    }

    [Fact]
    public async Task Traduzir_de_novo_refaz_so_o_que_a_IA_traduziu_e_avisa_quando_nao_ha_nada()
    {
        var vm = await ReadyAsync();
        var before = Store.LoadCharacter("c4")!.Description!;
        Assert.Equal(TranslationStatus.Machine, before.Status);

        await vm.TranslateCommand.ExecuteAsync(null);
        Assert.Equal(TranslationStatus.Reviewed, Store.LoadCharacter("c2")!.Description!.Status);
        Assert.Contains(_notices, n => n.Kind == ToastKind.Success);

        SheetEditor.Review(Store, GameSheetSamples.Id);
        _notices.Clear();
        await vm.ReloadCommand.ExecuteAsync(null);
        await vm.TranslateCommand.ExecuteAsync(null);

        Assert.Single(_notices, n => n.Kind == ToastKind.Info && n.Text.Contains("Não havia o que traduzir", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Traduzir_sem_chave_da_IA_avisa_para_configurar_os_Ajustes_e_nao_trava()
    {
        var vm = await ReadyAsync(translate: false);
        vm.TranslatorFactory = () => throw new InvalidOperationException("Falta a chave da IA.");

        await vm.TranslateCommand.ExecuteAsync(null);
        vm.TranslatorFactory = null;
        await vm.TranslateCommand.ExecuteAsync(null);

        Assert.Equal(2, _notices.Count(n => n.Kind == ToastKind.Warning && n.Text.Contains("Ajustes", StringComparison.Ordinal)));
        Assert.True(vm.HasPending);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Item_que_a_IA_esqueceu_fica_pendente_e_o_aviso_diz_quantos()
    {
        var vm = await ReadyAsync(translate: false);
        vm.TranslatorFactory = () => new VNForge.AI.Sheet.FakeSheetTranslator(item => item.Key == "synopsis" ? null : item.IsProse ? item.Source : "x");

        await vm.TranslateCommand.ExecuteAsync(null);

        Assert.Single(_notices, n => n.Kind == ToastKind.Warning && n.Text.Contains("1 ficou sem tradução", StringComparison.Ordinal));
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadGame(GameSheetSamples.Id)!.Synopsis!.Status);
    }

    [Fact]
    public async Task As_acoes_esperam_enquanto_uma_roda()
    {
        var vm = await ReadyAsync(translate: false);
        var gate = new TaskCompletionSource();
        vm.TranslatorFactory = () => new VNForge.AI.Sheet.FakeSheetTranslator(item =>
        {
            gate.Task.Wait(Ct);
            return "x";
        });

        var running = vm.TranslateCommand.ExecuteAsync(null);
        await Task.Delay(50, Ct);

        Assert.True(vm.IsBusy);
        Assert.True(vm.ShowBusy);
        Assert.False(vm.TranslateCommand.CanExecute(null));
        Assert.False(vm.FetchCommand.CanExecute(null));
        gate.SetResult();
        await running;
        Assert.False(vm.IsBusy);
    }

    // --- Revisão ---

    private static async Task<SheetReviewViewModel> ReviewAsync(GameSheetViewModel vm)
    {
        vm.StartReviewCommand.Execute(null);
        await Task.CompletedTask;
        return vm.Review;
    }

    [Fact]
    public async Task A_revisao_abre_no_primeiro_bloco_e_mostra_o_progresso()
    {
        var vm = await ReadyAsync();

        var review = await ReviewAsync(vm);

        Assert.True(vm.IsReviewing);
        Assert.False(vm.IsOverview);
        Assert.Equal("Sinopse · bloco 1 de 8 · revisado igual a uma fala", review.Subtitle);
        Assert.Equal("3 de 8", review.ProgressText);
        Assert.Equal(3d / 8, review.ProgressFraction, 5);
        Assert.Equal(["Tudo", "Só IA", "Só pendentes"], review.Filters.Select(f => f.Label));
        Assert.Contains("Amaoto no Hakoniwa", review.KeepText, StringComparison.Ordinal);
        Assert.Single(review.Pairs);
        Assert.Equal("Sinopse", review.Pairs[0].Title);
        Assert.True(review.Pairs[0].CanEdit);
        Assert.False(review.Pairs[0].HasHint);
        Assert.True(review.CanAct);
    }

    [Fact]
    public async Task O_filtro_so_IA_lista_os_blocos_da_IA_e_marcar_revisado_tira_o_bloco_da_lista()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);

        review.SelectedFilter = review.Filters[1];

        Assert.Equal(["Gêneros e tags", "Haruto Mine", "Misaki Tōdō", "Tsubaki Inoue"], review.BlockTitles);
        Assert.Equal("Gêneros e tags · bloco 1 de 4 · revisado igual a uma fala", review.Subtitle);

        await review.MarkReviewedCommand.ExecuteAsync(null);

        Assert.Equal(TranslationStatus.Reviewed, Store.LoadTerms().Find(SheetTermKind.Tag, "g1")!.Text.Status);
        Assert.Equal(["Haruto Mine", "Misaki Tōdō", "Tsubaki Inoue"], review.BlockTitles);
        Assert.Equal("Haruto Mine · bloco 1 de 3 · revisado igual a uma fala", review.Subtitle);
        Assert.Equal("4 de 8", review.ProgressText);
    }

    [Fact]
    public async Task Aprovar_e_proxima_aprova_o_bloco_e_vai_para_o_seguinte_e_no_ultimo_avisa_o_fim()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.SelectedFilter = review.Filters[0];
        review.NextCommand.Execute(null);

        Assert.Equal("Gêneros e tags", review.Current!.Title);
        await review.ApproveNextCommand.ExecuteAsync(null);

        Assert.Equal(TranslationStatus.Approved, Store.LoadTerms().Find(SheetTermKind.Tag, "g1")!.Text.Status);
        Assert.Equal("Kenta Aizawa", review.Current!.Title);

        for (var i = 0; i < 10; i++)
        {
            review.NextCommand.Execute(null);
        }

        Assert.Equal("Tsubaki Inoue", review.Current!.Title);
        review.PreviousCommand.Execute(null);
        review.NextCommand.Execute(null);
        await review.ApproveNextCommand.ExecuteAsync(null);
        Assert.Single(_notices, n => n.Text.StartsWith("Fim da lista", StringComparison.Ordinal));
    }

    [Fact]
    public async Task O_texto_escrito_a_mao_e_gravado_e_revisado_junto_com_marcar_revisado()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.SelectedFilter = review.Filters[1];
        while (review.Current!.Title != "Haruto Mine")
        {
            review.NextCommand.Execute(null);
        }

        Assert.Equal(["Descrição de Haruto Mine", "Traços de Haruto Mine"], review.Pairs.Select(p => p.Title));
        Assert.True(review.Pairs[0].ShowsActions);
        Assert.False(review.Pairs[1].ShowsActions);
        review.Pairs[0].Draft = "Veterano do clube, bem calmo.";
        Assert.True(review.Pairs[0].IsDirty);

        await review.MarkReviewedCommand.ExecuteAsync(null);

        var description = Store.LoadCharacter("c6")!.Description!;
        Assert.Equal(("Veterano do clube, bem calmo.", TranslationStatus.Reviewed), (description.Target, description.Status));
        Assert.Equal(TranslationStatus.Reviewed, Store.LoadTerms().Find(SheetTermKind.Trait, "i13")!.Text.Status);
    }

    [Fact]
    public async Task A_lista_com_o_numero_errado_de_nomes_mostra_o_erro_no_par_e_nao_revisa_nada()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.SelectedFilter = review.Filters[1];
        while (review.Current!.Title != "Misaki Tōdō")
        {
            review.NextCommand.Execute(null);
        }

        review.Pairs[1].Draft = "Só um nome";
        await review.MarkReviewedCommand.ExecuteAsync(null);

        Assert.True(review.Pairs[1].HasError);
        Assert.Contains("2 nomes", review.Pairs[1].Error, StringComparison.Ordinal);
        Assert.Equal(TranslationStatus.Machine, Store.LoadCharacter("c4")!.Description!.Status);
    }

    [Fact]
    public async Task O_texto_com_spoiler_so_se_edita_no_nivel_maximo()
    {
        var vm = await ReadyAsync();
        Store.Update(s => s.SaveGame(s.LoadGame(GameSheetSamples.Id)! with { Synopsis = new SheetText("A rainy town. [spoiler]He dies.[/spoiler]", "Cidade chuvosa. [spoiler]Ele morre.[/spoiler]", TranslationStatus.Machine) }));
        await vm.ReloadCommand.ExecuteAsync(null);
        var review = await ReviewAsync(vm);
        Assert.False(review.Pairs[0].CanEdit);
        Assert.True(review.Pairs[0].HasHint);
        Assert.Equal("A rainy town. [spoiler escondido]", review.Pairs[0].Original);
        Assert.False(review.CanAct);
        Assert.False(review.MarkReviewedCommand.CanExecute(null));

        review.CycleSpoilerCommand.Execute(null);
        review.CycleSpoilerCommand.Execute(null);

        Assert.Equal("Mostrando nível 2", review.SpoilerLevelText);
        Assert.True(review.Pairs[0].CanEdit);
        Assert.Equal("A rainy town. [spoiler]He dies.[/spoiler]", review.Pairs[0].Original);
        Assert.Equal("Cidade chuvosa. [spoiler]Ele morre.[/spoiler]", review.Pairs[0].Draft);
        Assert.True(review.CanAct);
    }

    [Fact]
    public async Task Traduzir_de_novo_um_bloco_refaz_inclusive_o_revisado()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        while (review.Current!.Title != "Kenta Aizawa")
        {
            review.NextCommand.Execute(null);
        }

        vm.TranslatorFactory = () => new VNForge.AI.Sheet.FakeSheetTranslator(item => "Novo: " + item.Source);
        await review.RetranslateCommand.ExecuteAsync(null);

        Assert.Equal("Apagar a revisão e traduzir", review.RetranslateText);
        Assert.Single(_notices, n => n.Kind == ToastKind.Warning && n.Text.Contains("apaga a revisão", StringComparison.Ordinal));
        Assert.NotEqual(TranslationStatus.Machine, Store.LoadCharacter("c1")!.Description!.Status);
        await review.RetranslateCommand.ExecuteAsync(null);

        Assert.Equal("Traduzir de novo", review.RetranslateText);
        var description = Store.LoadCharacter("c1")!.Description!;
        Assert.Equal(("Novo: The protagonist.", TranslationStatus.Machine), (description.Target, description.Status));
        Assert.Equal(TranslationStatus.Machine, Store.LoadTerms().Find(SheetTermKind.Trait, "i1")!.Text.Status);
        Assert.Equal("Kenta Aizawa", review.Current!.Title);
    }

    [Fact]
    public async Task Traduzir_de_novo_sem_a_IA_configurada_avisa_e_nao_muda_nada()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.NextCommand.Execute(null);
        vm.TranslatorFactory = null;

        await review.RetranslateCommand.ExecuteAsync(null);

        Assert.Single(_notices, n => n.Kind == ToastKind.Warning);
        Assert.Equal(TranslationStatus.Machine, Store.LoadTerms().Find(SheetTermKind.Tag, "g1")!.Text.Status);
    }

    [Fact]
    public async Task Navegar_ou_reler_com_texto_que_nao_pode_ser_gravado_nao_perde_o_rascunho_e_o_que_pode_e_gravado_antes()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.SelectedFilter = review.Filters[1];
        while (review.Current!.Title != "Haruto Mine")
        {
            review.NextCommand.Execute(null);
        }

        review.Pairs[0].Draft = "Veterano do clube.";
        vm.CycleSpoilerCommand.Execute(null);

        Assert.Equal("Veterano do clube.", Store.LoadCharacter("c6")!.Description!.Target);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];

        while (review.Current!.Title != "Misaki Tōdō")
        {
            review.NextCommand.Execute(null);
        }

        review.Pairs[1].Draft = "Só um nome";
        review.NextCommand.Execute(null);
        review.PreviousCommand.Execute(null);
        review.SelectedFilter = review.Filters[0];
        vm.ExitReviewCommand.Execute(null);

        Assert.Equal("Misaki Tōdō", review.Current!.Title);
        Assert.Equal("Só um nome", review.Pairs[1].Draft);
        Assert.True(review.Pairs[1].HasError);
        Assert.Same(review.Filters[1], review.SelectedFilter);
        Assert.True(vm.IsReviewing);
    }

    [Fact]
    public async Task Tempo_esgotado_ao_buscar_a_ficha_vira_aviso_e_nao_escapa_do_comando()
    {
        var vm = await ReadyAsync(translate: false);
        vm.CatalogFactory = () => throw new TaskCanceledException("tempo esgotado");

        await vm.FetchCommand.ExecuteAsync(null);

        Assert.Contains(_notices, n => n.Kind == ToastKind.Error && n.Text.Contains("tempo esgotado", StringComparison.Ordinal));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Os_tracos_com_spoiler_aparecem_como_etiquetas_pelo_nivel_e_os_escondidos_sao_contados()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        while (review.Current!.Title != "Aoi Sakura")
        {
            review.NextCommand.Execute(null);
        }

        Assert.Equal(["Cabelo longo", "Quieta", "Estudante", "Rata de biblioteca", "2 traços escondidos"], review.SpoilerTraits.Select(t => t.Text));
        Assert.True(review.SpoilerTraits[^1].IsHidden);

        vm.SelectedSpoiler = vm.SpoilerOptions[2];

        Assert.Equal(["Cabelo longo", "Quieta", "Estudante", "Rata de biblioteca", "Irmã de Kenta · spoiler 2", "Mente · spoiler 1"], review.SpoilerTraits.Select(t => t.Text));
        Assert.Equal([false, false, false, false, true, true], review.SpoilerTraits.Select(t => t.IsSpoiler));
        Assert.Equal("Mostrando nível 2", review.SpoilerLevelText);
    }

    [Fact]
    public async Task Sair_da_revisao_grava_o_que_foi_escrito_e_um_filtro_vazio_diz_o_que_fazer()
    {
        var vm = await ReadyAsync();
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.NextCommand.Execute(null);
        review.NextCommand.Execute(null);
        review.Pairs[0].Draft = "Descrição de Kenta escrita à mão.";

        vm.ExitReviewCommand.Execute(null);
        await vm.Loading;

        Assert.False(vm.IsReviewing);
        Assert.Equal("Descrição de Kenta escrita à mão.", Store.LoadCharacter("c1")!.Description!.Target);

        SheetEditor.Approve(Store, GameSheetSamples.Id);
        SheetEditor.Review(Store, GameSheetSamples.Id);
        SheetEditor.Approve(Store, GameSheetSamples.Id);
        await vm.ReloadCommand.ExecuteAsync(null);
        vm.StartReviewCommand.Execute(null);
        review.SelectedFilter = review.Filters[1];

        Assert.True(review.IsEmpty);
        Assert.Contains("Nenhum bloco neste filtro", review.EmptyText, StringComparison.Ordinal);
        Assert.False(review.HasBlock);
        Assert.Equal("revisado igual a uma fala", review.Subtitle);
    }

    [Fact]
    public async Task Ficha_sem_blocos_nao_abre_a_revisao_e_marcar_um_bloco_sem_traducao_avisa()
    {
        var vm = await ReadyAsync(translate: false);

        Assert.True(vm.StartReviewCommand.CanExecute(null));
        var review = await ReviewAsync(vm);
        vm.SelectedSpoiler = vm.SpoilerOptions[2];
        review.NextCommand.Execute(null);
        await review.MarkReviewedCommand.ExecuteAsync(null);

        Assert.Single(_notices, n => n.Kind == ToastKind.Warning && n.Text.Contains("ainda não tem tradução", StringComparison.Ordinal));
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadTerms().Find(SheetTermKind.Tag, "g1")!.Text.Status);
    }
}
