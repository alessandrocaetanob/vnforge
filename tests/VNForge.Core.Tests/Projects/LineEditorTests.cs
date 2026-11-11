using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

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

    [Fact]
    public void A_opcao_e_lida_do_vnforge_json_quando_ele_muda_e_nao_a_cada_gravacao()
    {
        using var pasta = new TempFolder();
        var editor = EditorComOpcao(pasta.Path, pontuacaoDoTeclado: true, Linha(1, TranslationStatus.Machine, "x"));
        var projeto = ProjectFolder.Open(pasta.Path);
        Assert.Equal("a.", editor.SetTranslation("yst00001", "yst00001:000001", "a。").Translation);

        projeto.SaveConfig(projeto.LoadConfig() with { Style = StyleConfig.Default with { KeyboardPunctuation = false } });

        Assert.Equal("b。", editor.SetTranslation("yst00001", "yst00001:000001", "b。").Translation);
    }

    [Fact]
    public void Restore_devolve_a_linha_ao_estado_anterior()
    {
        using var pasta = new TempFolder();
        var antes = Linha(1, TranslationStatus.Translated, "Bom dia.");
        var (projeto, editor) = Preparar(pasta.Path, antes, Linha(2, TranslationStatus.Untranslated));
        var gravada = editor.Approve("yst00001", antes.Id);

        var restaurada = editor.Restore("yst00001", antes, gravada);

        Assert.Equal(antes.Status, restaurada.Status);
        var lida = Gravada(projeto, 1);
        Assert.Equal((antes.Translation, antes.Status, antes.Origin, antes.Notes), (lida.Translation, lida.Status, lida.Origin, lida.Notes));
        Assert.Equal(TranslationStatus.Untranslated, Gravada(projeto, 2).Status);
    }

    private static TextUnit ComDuvida(int n) =>
        Linha(n, TranslationStatus.Machine, "Bom dia.") with { Doubt = new TranslationDoubt(DoubtKind.HiddenSubject, "Quem cumprimenta?") };

    [Fact]
    public void Approve_de_linha_ja_aprovada_ainda_encerra_a_duvida()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, ComDuvida(1) with { Status = TranslationStatus.Approved });

        editor.Approve("yst00001", "yst00001:000001");

        Assert.Null(Gravada(projeto, 1).Doubt);
    }

    [Fact]
    public void SetTranslation_com_texto_novo_encerra_a_duvida_e_com_texto_igual_a_mantem()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, ComDuvida(1));

        editor.SetTranslation("yst00001", "yst00001:000001", "Bom dia.");
        Assert.NotNull(Gravada(projeto, 1).Doubt);

        editor.SetTranslation("yst00001", "yst00001:000001", "Olá.");
        Assert.Null(Gravada(projeto, 1).Doubt);
    }

    [Fact]
    public void Restore_devolve_a_duvida_que_a_linha_tinha()
    {
        using var pasta = new TempFolder();
        var antes = ComDuvida(1);
        var (projeto, editor) = Preparar(pasta.Path, antes);
        var gravada = editor.Approve("yst00001", antes.Id);

        editor.Restore("yst00001", antes, gravada);

        Assert.Equal(antes.Doubt, Gravada(projeto, 1).Doubt);
    }

    [Fact]
    public void Restore_de_linha_que_nao_existe_falha()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        Assert.Throws<KeyNotFoundException>(() =>
        {
            var inexistente = Linha(9, TranslationStatus.Translated, "x");
            editor.Restore("yst00001", inexistente, inexistente);
        });
    }

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
    public void Restore_mantem_flags_e_original_a_revisar_do_arquivo_atual()
    {
        using var pasta = new TempFolder();
        var antes = Linha(1, TranslationStatus.Translated, "Bom dia.");
        var (projeto, editor) = Preparar(pasta.Path, antes);
        var gravada = editor.Approve("yst00001", antes.Id);
        projeto.SaveText("yst00001", [gravada with { Flags = ["glossary"], PreviousSource = "旧原文" }]);

        editor.Restore("yst00001", antes, gravada);

        var lida = Gravada(projeto, 1);
        Assert.Equal(TranslationStatus.Translated, lida.Status);
        Assert.Equal(["glossary"], lida.Flags);
        Assert.Equal("旧原文", lida.PreviousSource);
    }

    [Theory]
    [InlineData(null)]
    public void Traducao_vazia_volta_para_untranslated_sem_origem(string? texto)
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Approved, "Oi."));

        var salva = editor.SetTranslation("yst00001", "yst00001:000001", texto);

        Assert.Null(salva.Translation);
        var lida = Gravada(projeto, 1);
        Assert.Null(lida.Translation);
        Assert.Equal(TranslationStatus.Untranslated, lida.Status);
        Assert.Null(lida.Origin);
    }

    [Fact]
    public void Traducao_vazia_numa_linha_sem_traducao_nao_regrava_o_arquivo()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));
        var caminho = projeto.TextPath("yst00001");
        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        editor.SetTranslation("yst00001", "yst00001:000001", "   ");

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
    }

    [Fact]
    public void Notas_iguais_nao_regravam_o_arquivo()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Translated, "Oi."));
        var caminho = projeto.TextPath("yst00001");
        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        editor.SetNotes("yst00001", "yst00001:000001", "nota 1");

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
    }

    [Fact]
    public void Aprovar_linha_ja_aprovada_nao_regrava_o_arquivo()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Approved, "Oi."));
        var caminho = projeto.TextPath("yst00001");
        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        editor.Approve("yst00001", "yst00001:000001");

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
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
    public void Script_inexistente_lanca_TextFileException()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        Assert.Throws<TextFileException>(() => editor.SetTranslation("yst99999", "yst99999:000001", "Oi."));
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

    [Fact]
    public void SetChosenSpeaker_igual_ao_do_jogo_ou_a_narracao_numa_linha_sem_falante_nao_grava_nada()
    {
        using var pasta = new TempFolder();
        var semFalante = Linha(2, TranslationStatus.Translated, "Oi") with { Speaker = null };
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Translated, "Oi"), semFalante);

        Assert.Null(editor.SetChosenSpeaker("yst00001", "yst00001:000001", "美咲").ChosenSpeaker);
        Assert.Null(editor.SetChosenSpeaker("yst00001", "yst00001:000002", string.Empty).ChosenSpeaker);

        Assert.All(projeto.LoadText("yst00001"), u => Assert.Null(u.ChosenSpeaker));
    }

    [Fact]
    public void SetChosenSpeaker_em_linha_inexistente_lanca()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Translated, "Oi"));

        Assert.Throws<KeyNotFoundException>(() => editor.SetChosenSpeaker("yst00001", "yst00001:000099", "Inui"));
    }
}
