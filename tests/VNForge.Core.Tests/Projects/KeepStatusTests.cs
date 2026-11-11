using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O estado "Não traduzir" (<see cref="TranslationStatus.Keep"/>, #373): a linha fica como no original de propósito e conta como pronta.</summary>
public sealed class KeepStatusTests
{
    private const string FontName = "ＭＳ ゴシック";
    private static readonly DateTimeOffset Agora = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(int n, TranslationStatus status, string? traducao = null, string? origem = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.System, null, origem ?? $"原文{n}", traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private static ProjectFolder NovoProjeto(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00001", linhas);
        return projeto;
    }

    private static LineEditor EditorDe(ProjectFolder projeto) => new(projeto, new FakeTimeProvider(Agora));

    [Fact]
    public void Ida_e_volta_do_text_json_com_linha_keep_nao_muda_nada()
    {
        using var pasta = new TempFolder();
        var linhas = new[] { Linha(1, TranslationStatus.Keep, origem: FontName), Linha(2, TranslationStatus.Translated, "Olá") };
        var projeto = NovoProjeto(pasta.Path, linhas);

        var lidas = projeto.LoadText("yst00001");

        Assert.Equal(linhas.Select(TextUnitJson.Serialize), lidas.Select(TextUnitJson.Serialize));
        Assert.Equal(TextFileSerializer.Serialize(linhas), TextFileSerializer.Serialize(lidas));
    }

    [Fact]
    public void Filtro_Keep_mostra_so_as_linhas_nao_traduzir()
    {
        var keep = new LineEntry("a", 1, Linha(1, TranslationStatus.Keep), []);
        var pendente = new LineEntry("a", 2, Linha(2, TranslationStatus.Untranslated), []);

        var filtro = new LineFilter(LineStatusFilter.Keep);

        Assert.True(filtro.Matches(keep));
        Assert.False(filtro.Matches(pendente));
        Assert.False(new LineFilter(LineStatusFilter.Pending).Matches(keep));
    }

    [Fact]
    public void SetKeep_falso_devolve_a_linha_a_sem_traducao_e_nao_mexe_nas_outras()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Keep), Linha(2, TranslationStatus.Approved, "ok"));
        var editor = EditorDe(projeto);

        var volta = editor.SetKeep("yst00001", "yst00001:000001", keep: false);
        var aprovada = editor.SetKeep("yst00001", "yst00001:000002", keep: false);

        Assert.Equal(TranslationStatus.Untranslated, volta.Status);
        Assert.Equal(TranslationStatus.Approved, aprovada.Status);
        Assert.Equal("ok", aprovada.Translation);
    }

    [Fact]
    public void SetKeepMany_grava_um_arquivo_por_script_e_devolve_o_que_mudou()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Untranslated), Linha(2, TranslationStatus.Keep), Linha(3, TranslationStatus.Machine, "x"));

        var mudancas = EditorDe(projeto).SetKeepMany("yst00001", ["yst00001:000001", "yst00001:000002", "yst00001:000003"], keep: true);

        Assert.Equal(["yst00001:000001", "yst00001:000003"], mudancas.Select(c => c.Saved.Id));
        Assert.All(projeto.LoadText("yst00001"), u => Assert.Equal(TranslationStatus.Keep, u.Status));
    }

    [Fact]
    public void Traducao_vazia_numa_linha_keep_nao_tira_o_keep_e_texto_novo_traduz_a_linha()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Keep));
        var editor = EditorDe(projeto);

        Assert.Equal(TranslationStatus.Keep, editor.SetTranslation("yst00001", "yst00001:000001", "  ").Status);
        Assert.Equal(TranslationStatus.Translated, editor.SetTranslation("yst00001", "yst00001:000001", "MS Gothic").Status);
    }
}
