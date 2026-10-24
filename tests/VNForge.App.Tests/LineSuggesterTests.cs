using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

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
    public async Task Sem_chave_usa_o_provedor_falso_e_nao_cria_provedor_real()
    {
        chave = null;

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.True(sugestao.FromFakeProvider);
        Assert.Empty(criados);
    }

    [Fact]
    public async Task Chave_em_branco_vale_como_sem_chave()
    {
        chave = "   ";

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.True(sugestao.FromFakeProvider);
        Assert.Empty(criados);
    }

    [Fact]
    public async Task Com_chave_usa_o_provedor_real_com_o_modelo_padrao()
    {
        chave = "k1";

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.False(sugestao.FromFakeProvider);
        Assert.Equal($"k1/{ClaudeModels.Opus}", sugestao.Text);
    }

    [Fact]
    public async Task Reaproveita_o_provedor_enquanto_chave_e_modelo_nao_mudam()
    {
        chave = "k1";
        var sugestor = Abrir();

        await sugestor.SuggestAsync(Linha, CancellationToken.None);
        await sugestor.SuggestAsync(Linha, CancellationToken.None);

        Assert.Single(criados);
    }

    [Fact]
    public async Task Trocar_a_chave_cria_outro_provedor_e_descarta_o_antigo()
    {
        var sugestor = LineSuggester.ForUser(pasta.Path, _ => chave, (key, model) =>
        {
            var provedor = new ProvedorDescartavel(key);
            antigos.Add(provedor);
            return provedor;
        });
        chave = "k1";
        await sugestor.SuggestAsync(Linha, CancellationToken.None);

        chave = "k2";
        var sugestao = await sugestor.SuggestAsync(Linha, CancellationToken.None);

        Assert.Equal("k2", sugestao.Text);
        Assert.Equal(2, antigos.Count);
        Assert.True(antigos[0].Descartado);
        Assert.False(antigos[1].Descartado);
    }

    [Fact]
    public async Task Trocar_o_modelo_em_ai_json_cria_outro_provedor()
    {
        chave = "k1";
        var sugestor = Abrir();
        await sugestor.SuggestAsync(Linha, CancellationToken.None);

        new AiUserSettingsFile(pasta.Path).Save(new AiUserSettings(ClaudeModels.Haiku));
        var sugestao = await sugestor.SuggestAsync(Linha, CancellationToken.None);

        Assert.Equal($"k1/{ClaudeModels.Haiku}", sugestao.Text);
        Assert.Equal([("k1", ClaudeModels.Opus), ("k1", ClaudeModels.Haiku)], criados);
    }

    [Fact]
    public async Task Ai_json_invalido_cai_para_o_modelo_padrao_em_vez_de_falhar()
    {
        chave = "k1";
        File.WriteAllText(new AiUserSettingsFile(pasta.Path).Path, "{ isto não é json");

        var sugestao = await Abrir().SuggestAsync(Linha, CancellationToken.None);

        Assert.Equal($"k1/{ClaudeModels.Opus}", sugestao.Text);
    }

    [Fact]
    public void Provedor_claude_padrao_nao_precisa_de_rede_para_ser_montado()
    {
        Assert.IsType<ClaudeTranslationProvider>(LineSuggester.CreateClaude("sk-teste", ClaudeModels.Sonnet));
    }

    [Fact]
    public void Sem_funcao_para_abrir_o_provedor_e_recusado()
    {
        Assert.Throws<ArgumentNullException>(() => new LineSuggester(null!));
    }

    [Fact]
    public async Task Unidade_nula_e_recusada()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new LineSuggester(() => null).SuggestAsync(null!, CancellationToken.None));
    }

    private readonly List<ProvedorDescartavel> antigos = [];

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
