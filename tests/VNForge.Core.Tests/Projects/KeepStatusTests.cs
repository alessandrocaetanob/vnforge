using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O estado "Não traduzir" (<see cref="TranslationStatus.Keep"/>, #373): a linha fica como no original de propósito e conta como pronta.</summary>
[Trait("Risco", "R2")]
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
}
