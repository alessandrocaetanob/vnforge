using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R6")]
public class ScriptGroupEditorTests
{
    private static string Key(int n) => $"yst{n:D5}";

    private static ProjectFolder ProjetoCom(string raiz, int scripts, params (int N, string Folder)[] pastas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        for (var n = 1; n <= scripts; n++)
        {
            projeto.SaveText(Key(n), [new TextUnit($"{Key(n)}:000001", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        if (pastas.Length > 0)
        {
            projeto.SaveScriptNames(ScriptNames.Empty.WithDetected(pastas.ToDictionary(p => Key(p.N), p => new DetectedScript($"cena{p.N}", p.Folder), StringComparer.Ordinal)));
        }

        return projeto;
    }

    [Fact]
    public void Chave_em_maiuscula_e_aceita_e_chave_desconhecida_recusa_sem_gravar_nada()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3);

        Assert.Equal([Key(1)], ScriptGroupEditor.Group(projeto, [Key(1).ToUpperInvariant()], "A").Keys);

        var antes = File.ReadAllText(projeto.ScriptNamesPath);
        var erro = Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [Key(2), "yst99999", "nada"], "B"));

        Assert.Contains("yst99999", erro.Message, StringComparison.Ordinal);
        Assert.Contains("nada", erro.Message, StringComparison.Ordinal);
        Assert.Equal(antes, File.ReadAllText(projeto.ScriptNamesPath));
    }
}
