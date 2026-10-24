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

    [Fact]
    public void Traducao_nova_grava_translated_com_origem_manual()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated), Linha(2, TranslationStatus.Untranslated));

        var salva = editor.SetTranslation("yst00001", "yst00001:000001", "Bom dia.\\p");

        Assert.Equal(TranslationStatus.Translated, salva.Status);
        Assert.Equal(new TranslationOrigin("manual", null, Agora), salva.Origin);
        var lida = Gravada(projeto, 1);
        Assert.Equal("Bom dia.\\p", lida.Translation);
        Assert.Equal(TranslationStatus.Translated, lida.Status);
        Assert.Equal(new TranslationOrigin("manual", null, Agora), lida.Origin);
        Assert.Equal("nota 1", lida.Notes);
        Assert.Equal(TranslationStatus.Untranslated, Gravada(projeto, 2).Status);
    }

    [Theory]
    [InlineData(TranslationStatus.Machine)]
    [InlineData(TranslationStatus.Reviewed)]
    [InlineData(TranslationStatus.Approved)]
    public void Editar_o_texto_desfaz_a_revisao_e_troca_a_origem(TranslationStatus anterior)
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, anterior, "Oi."));

        editor.SetTranslation("yst00001", "yst00001:000001", "Olá.");

        var lida = Gravada(projeto, 1);
        Assert.Equal(TranslationStatus.Translated, lida.Status);
        Assert.Equal("manual", lida.Origin?.Provider);
        Assert.Equal(Agora, lida.Origin?.At);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t ")]
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
    public void Texto_igual_ao_gravado_nao_regrava_o_arquivo()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Approved, "Oi."));
        var caminho = projeto.TextPath("yst00001");
        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var devolvida = editor.SetTranslation("yst00001", "yst00001:000001", "Oi.");

        Assert.Equal(TranslationStatus.Approved, devolvida.Status);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
        Assert.Equal(TranslationStatus.Approved, Gravada(projeto, 1).Status);
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
    public void Notas_sao_gravadas_sem_mexer_na_traducao()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Reviewed, "Oi."));

        var salva = editor.SetNotes("yst00001", "yst00001:000001", "Confirmar com o autor.");

        Assert.Equal("Confirmar com o autor.", salva.Notes);
        var lida = Gravada(projeto, 1);
        Assert.Equal("Confirmar com o autor.", lida.Notes);
        Assert.Equal(TranslationStatus.Reviewed, lida.Status);
        Assert.Equal("Oi.", lida.Translation);
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
    public void Aprovar_grava_approved_e_mantem_a_origem()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Machine, "Oi."));
        var origem = Gravada(projeto, 1).Origin;

        var salva = editor.Approve("yst00001", "yst00001:000001");

        Assert.Equal(TranslationStatus.Approved, salva.Status);
        var lida = Gravada(projeto, 1);
        Assert.Equal(TranslationStatus.Approved, lida.Status);
        Assert.Equal(origem, lida.Origin);
        Assert.Equal("Oi.", lida.Translation);
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
    public void Aprovar_rascunho_ignorado_pelo_build_usa_o_texto_que_esta_na_linha()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated, "Rascunho antigo."));

        editor.Approve("yst00001", "yst00001:000001");

        Assert.Equal(TranslationStatus.Approved, Gravada(projeto, 1).Status);
    }

    [Fact]
    public void Id_desconhecido_lanca_KeyNotFoundException_em_portugues()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        var erro = Assert.Throws<KeyNotFoundException>(() => editor.SetTranslation("yst00001", "yst00001:000099", "Oi."));

        Assert.Contains("yst00001:000099", erro.Message);
        Assert.Contains("não existe", erro.Message);
        Assert.Throws<KeyNotFoundException>(() => editor.SetNotes("yst00001", "yst00001:000099", "x"));
        Assert.Throws<KeyNotFoundException>(() => editor.Approve("yst00001", "yst00001:000099"));
    }

    [Fact]
    public void Script_inexistente_lanca_TextFileException()
    {
        using var pasta = new TempFolder();
        var (_, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        Assert.Throws<TextFileException>(() => editor.SetTranslation("yst99999", "yst99999:000001", "Oi."));
    }

    [Fact]
    public void Edicao_aparece_no_ProjectStatusReporter()
    {
        using var pasta = new TempFolder();
        var (projeto, editor) = Preparar(pasta.Path, Linha(1, TranslationStatus.Untranslated), Linha(2, TranslationStatus.Untranslated));

        editor.SetTranslation("yst00001", "yst00001:000001", "Oi.");
        editor.Approve("yst00001", "yst00001:000001");
        editor.SetTranslation("yst00001", "yst00001:000002", "Tchau.");

        var status = ProjectStatusReporter.Create(ProjectFolder.Open(projeto.Root));
        Assert.Equal(1, status.Total.Approved);
        Assert.Equal(1, status.Total.Translated);
        Assert.Equal(0, status.Total.Untranslated);
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
}
