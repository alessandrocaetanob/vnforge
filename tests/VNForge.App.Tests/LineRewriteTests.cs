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

[Trait("Risco", "R2")]
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
