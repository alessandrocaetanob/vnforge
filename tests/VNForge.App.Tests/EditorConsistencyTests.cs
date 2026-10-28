using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O aviso de "mesmo original, traduções diferentes" no detalhe da linha do Editor: as outras variantes, as linhas e a diferença (#457).</summary>
public sealed class EditorConsistencyTests : IDisposable
{
    private const string Script = "yst00001";
    private const string Rule = "traducao-inconsistente";

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;
    private readonly ProjectFolder projeto;

    public EditorConsistencyTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        projeto = ProjectFolder.Open(raiz);
        // Três linhas do mesmo original: "Bom dia." (2), "Bom dia" (1, sem o ponto) e "Bom  dia." (1, espaço duplo); mais uma de outro original.
        projeto.SaveText(Script,
        [
            Fala(0, "おはよう", "Bom dia."),
            Fala(1, "おはよう", "Bom dia."),
            Fala(2, "おはよう", "Bom dia"),
            Fala(3, "おはよう", "Bom  dia."),
            Fala(4, "さようなら", "Tchau."),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string original, string traducao) =>
        new($"{Script}:{n:D6}", TextUnitKind.Dialogue, null, original, traducao, TranslationStatus.Translated);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private static bool Tem(LineViewModel linha) => linha.Entry.Issues.Any(i => i.RuleId == Rule);

    private IReadOnlyList<string?> Traducoes() => [.. projeto.LoadText(Script).Select(u => u.Translation)];

    [Fact]
    public async Task Linha_com_traducao_divergente_mostra_o_cartao_com_as_variantes_as_linhas_e_a_diferenca()
    {
        var vm = await Abrir();
        Assert.True(Tem(vm.Lines[2]));

        vm.SelectedLine = vm.Lines[2];

        var card = Assert.IsType<ConferenceConsistencyCardViewModel>(vm.Detail!.ConsistencyCard);
        Assert.True(vm.Detail.HasConsistencyCard);
        Assert.Equal("Mesmo original, 3 traduções", card.Title);
        Assert.Equal(["Bom dia.", "Bom dia", "Bom  dia."], card.Variants.Select(v => v.Translation));
        Assert.Equal(["Cena 1 · 1", "Cena 1 · 2"], card.Variants[0].VisibleLines.Select(l => l.Label));
        Assert.Contains(card.Variants[1].Segments, s => s is { Kind: TextDiffKind.Missing, Text: "." });
        Assert.Contains(card.Variants[2].Segments, s => s.Kind == TextDiffKind.Added && s.Visible == "·");
        Assert.DoesNotContain(vm.Detail.QaIssues, i => i.Title == ConferenceRuleTexts.Title(Rule));
    }

    [Fact]
    public async Task Linha_da_traducao_mais_usada_nao_mostra_o_cartao()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[0];

        Assert.Null(vm.Detail!.ConsistencyCard);
        Assert.False(vm.Detail.HasConsistencyCard);
    }

    [Fact]
    public async Task Linha_sem_outras_traducoes_nao_mostra_o_cartao()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[4];

        Assert.Null(vm.Detail!.ConsistencyCard);
    }

    [Fact]
    public async Task O_link_de_uma_linha_do_cartao_abre_essa_linha_no_editor()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var link = vm.Detail!.ConsistencyCard!.Variants[0].VisibleLines[1];

        Assert.True(vm.Detail.OpenConsistencyLineCommand.CanExecute(link.Request));
        await vm.Detail.OpenConsistencyLineCommand.ExecuteAsync(link.Request);

        Assert.Equal(link.Request.UnitId, vm.SelectedLine!.Id);
    }

    [Fact]
    public async Task Usar_em_todas_grava_a_variante_na_linha_aberta_e_nas_outras_depois_de_confirmar()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var chosen = vm.Detail!.ConsistencyCard!.Variants[0];

        vm.Detail.UseVariantCommand.Execute(chosen);

        // As outras linhas têm outra tradução: a pergunta de sempre do "Usar em todos" vem antes de gravar nelas.
        Assert.True(vm.Detail.IsUseForAllConfirmOpen);
        vm.Detail.ConfirmUseForAllCommand.Execute(null);

        Assert.All(Traducoes().Take(4), t => Assert.Equal("Bom dia.", t));
        Assert.Equal("Tchau.", Traducoes()[4]);
        relogio.Advance(EditorViewModel.RecheckDelay);
        await vm.Rechecking;
        Assert.All(vm.Lines, l => Assert.False(Tem(l)));
        Assert.Null(vm.Detail.ConsistencyCard);
    }

    [Fact]
    public async Task Usar_em_todas_pode_ser_desfeito_pelo_mesmo_aviso_do_usar_em_todos()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        LineSavedEventArgs? saved = null;
        vm.LineSaved += (_, e) => saved = e;
        vm.Detail!.UseVariantCommand.Execute(vm.Detail.ConsistencyCard!.Variants[0]);
        vm.Detail.ConfirmUseForAllCommand.Execute(null);

        Assert.True(saved!.IsBatch);
        Assert.True(vm.Undo(saved));

        Assert.Equal(["Bom dia.", "Bom dia.", "Bom dia", "Bom dia."], Traducoes().Take(4));
    }

    [Fact]
    public async Task Cancelar_a_pergunta_nao_muda_nenhuma_linha_nem_a_aberta()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        vm.Detail!.UseVariantCommand.Execute(vm.Detail.ConsistencyCard!.Variants[0]);

        vm.Detail.CancelUseForAllCommand.Execute(null);

        // Cancelar não muda nenhuma linha, nem a aberta.
        Assert.Equal("Bom dia", Traducoes()[2]);
        Assert.Equal("Bom  dia.", Traducoes()[3]);
    }

    [Fact]
    public async Task Manter_diferentes_grava_a_marca_tira_o_aviso_e_continua_depois_de_reabrir()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        var card = vm.Detail!.ConsistencyCard!;

        Assert.True(vm.Detail.KeepDifferentCommand.CanExecute(card));
        await vm.Detail.KeepDifferentCommand.ExecuteAsync(card);

        Assert.Null(vm.Detail.ConsistencyCard);
        await vm.Rechecking;
        Assert.All(vm.Lines, l => Assert.False(Tem(l)));
        Assert.All(projeto.LoadText(Script).Take(4), u => Assert.Contains(ConsistencyGroups.FlagFor(card.Group), u.Flags));
        Assert.Empty(new InconsistentTranslationRule().Check(QaContext.ForTexts(projeto.LoadAllText())));

        var reaberto = await Abrir();
        Assert.All(reaberto.Lines, l => Assert.False(Tem(l)));
    }

    [Fact]
    public async Task Manter_diferentes_volta_a_avisar_quando_uma_traducao_muda()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[2];
        await vm.Detail!.KeepDifferentCommand.ExecuteAsync(vm.Detail.ConsistencyCard!);
        await vm.Rechecking;

        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.Translation = "Bom dia!!";
        vm.FlushPendingEdits();
        relogio.Advance(EditorViewModel.RecheckDelay);
        await vm.Rechecking;

        Assert.True(Tem(vm.Lines[3]));
    }
}
