using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public class LineEditorTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Linha(int n, TranslationStatus status, string? traducao = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
            Notes = $"nota {n}",
        };

    private static (ProjectFolder Projeto, LineEditor Editor) Preparar(string raiz, params TextUnit[] linhas)
    {
        var projeto = NovoProjeto(raiz);
        projeto.SaveText("yst00001", linhas);
        return (projeto, new LineEditor(projeto, new FakeTimeProvider(Agora)));
    }

    private static TextUnit Gravada(ProjectFolder projeto, int n) =>
        projeto.LoadText("yst00001").Single(u => u.Id == $"yst00001:{n:D6}");

    private static LineEditor EditorComOpcao(string raiz, bool pontuacaoDoTeclado, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default)
        {
            Style = StyleConfig.Default with { KeyboardPunctuation = pontuacaoDoTeclado },
        });
        projeto.SaveText("yst00001", linhas);
        return new LineEditor(projeto, new FakeTimeProvider(Agora));
    }

    private static TextUnit ComDuvida(int n) =>
        Linha(n, TranslationStatus.Machine, "Bom dia.") with { Doubt = new TranslationDoubt(DoubtKind.HiddenSubject, "Quem cumprimenta?") };

    [Fact]
    public void Restore_recusa_quando_a_linha_mudou_depois_da_gravacao()
    {
        using var pasta = new TempFolder();
        var antes = Linha(1, TranslationStatus.Translated, "Bom dia.");
        var (projeto, editor) = Preparar(pasta.Path, antes);
        var gravada = editor.Approve("yst00001", antes.Id);
        var outra = editor.SetTranslation("yst00001", antes.Id, "Outro texto.");

        var erro = Assert.Throws<VNForgeException>(() => editor.Restore("yst00001", antes, gravada));

        Assert.Equal("A linha mudou depois deste aviso; nada foi desfeito.", erro.Message);
        var lida = Gravada(projeto, 1);
        Assert.Equal((outra.Translation, outra.Status, outra.Origin), (lida.Translation, lida.Status, lida.Origin));
    }

    [Fact]
    public void Aprovar_sem_traducao_e_recusado_e_nada_e_gravado()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        var erro = Assert.Throws<InvalidOperationException>(() => editor.Approve("yst00001", "yst00001:000001"));

        Assert.Contains("tradução", erro.Message);
        Assert.Equal(TranslationStatus.Untranslated, Gravada(projeto, 1).Status);
    }

    [Fact]
    public void Argumentos_invalidos_sao_recusados()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        var relogio = new FakeTimeProvider(Agora);
        Assert.Throws<ArgumentNullException>(() => new LineEditor(null!, relogio));
        Assert.Throws<ArgumentNullException>(() => new LineEditor(projeto, null!));
        var editor = new LineEditor(projeto, relogio);
        Assert.Throws<ArgumentNullException>(() => editor.SetNotes("yst00001", "yst00001:000001", null!));
    }

    [Fact]
    public void SetChosenSpeaker_vazio_e_narracao_e_nulo_volta_ao_falante_do_jogo()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Translated, "Oi"));

        editor.SetChosenSpeaker("yst00001", "yst00001:000001", string.Empty);
        Assert.Equal(string.Empty, Gravada(projeto, 1).ChosenSpeaker);

        editor.SetChosenSpeaker("yst00001", "yst00001:000001", null);
        Assert.Null(Gravada(projeto, 1).ChosenSpeaker);
    }
}
