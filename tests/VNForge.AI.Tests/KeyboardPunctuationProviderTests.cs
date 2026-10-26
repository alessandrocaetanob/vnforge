using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public sealed class KeyboardPunctuationProviderTests
{
    private static ProjectConfig Config(string strategy, bool option) => new(
        "VN",
        new GameConfig("D:/Jogos/VN", "VN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig(strategy),
        AiConfig.Default)
    {
        Style = StyleConfig.Default with { KeyboardPunctuation = option },
    };

    private static TranslationBatch Batch() =>
        new([new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated), new TextUnit("a:2", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);

    private sealed class Canned(params TranslationResult[] results) : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>(results);
    }

    [Fact]
    public async Task Resposta_da_IA_com_pontuacao_japonesa_sai_com_a_do_teclado()
    {
        var inner = new Canned(new TranslationResult("a:1", "～Bom dia。（oi）"), new TranslationResult("a:2", "Nossa！？"));
        var provider = KeyboardPunctuationProvider.Wrap(inner, Config("cp1252", option: true));

        var results = await provider.TranslateAsync(Batch(), CancellationToken.None);

        Assert.Equal(["~Bom dia.(oi)", "Nossa!?"], results.Select(r => r.Translation));
    }

    [Fact]
    public async Task Com_a_opcao_desligada_o_provedor_nao_e_envolvido()
    {
        var inner = new Canned(new TranslationResult("a:1", "Oi。"));

        var provider = KeyboardPunctuationProvider.Wrap(inner, Config("cp1252", option: false));
        var results = await provider.TranslateAsync(Batch(), CancellationToken.None);

        Assert.Same(inner, provider);
        Assert.Equal("Oi。", results[0].Translation);
    }

    [Fact]
    public async Task Linha_recusada_passa_sem_mudanca()
    {
        var recusada = new TranslationResult("a:1", string.Empty, Refused: true);
        var provider = KeyboardPunctuationProvider.Wrap(new Canned(recusada), Config("cp1252", option: true));

        var results = await provider.TranslateAsync(Batch(), CancellationToken.None);

        Assert.Equal(recusada, results[0]);
    }

    [Fact]
    public void Estrategia_sem_encoder_pronto_falha_com_a_mensagem_de_sempre_em_vez_de_trocar_sem_conferir()
    {
        var ex = Assert.Throws<VNForgeException>(() => KeyboardPunctuationProvider.Wrap(new Canned(), Config("remap", option: true)));

        Assert.Contains("remap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Estrategia_sem_encoder_pronto_com_a_opcao_desligada_nao_falha()
    {
        var inner = new Canned(new TranslationResult("a:1", "Oi。"));

        var provider = KeyboardPunctuationProvider.Wrap(inner, Config("remap", option: false));

        Assert.Same(inner, provider);
        Assert.Equal("Oi。", (await provider.TranslateAsync(Batch(), CancellationToken.None))[0].Translation);
    }
}
