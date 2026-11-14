using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
public sealed class LineSuggesterTests : IDisposable
{
    private static readonly TextUnit Linha = new("yst00001:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated);

    private readonly TempFolder pasta = new();
    private readonly List<(string Key, string Model)> criados = [];
    private string? chave;

    public void Dispose() => pasta.Dispose();

    private LineSuggester Abrir() => LineSuggester.ForUser(pasta.Path, _ => chave, (key, model) =>
    {
        criados.Add((key, model));
        return new ProvedorDescartavel($"{key}/{model}");
    });

    [Fact]
    public async Task Chave_em_branco_vale_como_sem_chave()
    {
        chave = "   ";

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.True(sugestao.FromFakeProvider);
        Assert.Empty(criados);
    }

    [Fact]
    public async Task Ai_json_invalido_cai_para_o_modelo_padrao_em_vez_de_falhar()
    {
        chave = "k1";
        File.WriteAllText(new AiUserSettingsFile(pasta.Path).Path, "{ isto não é json");

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.Equal($"k1/{ClaudeModels.Opus}", sugestao.Text);
    }


    private sealed class ProvedorDescartavel(string texto) : ITranslationProvider, IDisposable
    {
        public bool Descartado { get; private set; }

        public void Dispose() => Descartado = true;

        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            IReadOnlyList<TranslationResult> resultado = [.. batch.Lines.Select(u => new TranslationResult(u.Id, texto))];
            return Task.FromResult(resultado);
        }
    }
}
