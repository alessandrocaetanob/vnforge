using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectStatusReporterTests
{
    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Linha(string key, int n, string? falante, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    private static ProjectFolder ProjetoComTextos(string raiz)
    {
        var projeto = NovoProjeto(raiz);
        projeto.SaveText("yst00001",
        [
            Linha("yst00001", 1, "美咲", TranslationStatus.Untranslated),
            Linha("yst00001", 2, "美咲", TranslationStatus.Machine),
            Linha("yst00001", 3, null, TranslationStatus.Approved),
            Linha("yst00001", 4, "健", TranslationStatus.Translated),
        ]);
        projeto.SaveText("yst00002",
        [
            Linha("yst00002", 1, "健", TranslationStatus.Reviewed),
            Linha("yst00002", 2, "美咲", TranslationStatus.Machine),
            Linha("yst00002", 3, null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00003", []);
        return projeto;
    }

    [Fact]
    public void Totais_batem_com_a_contagem_feita_direto_nos_arquivos_json()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);

        var status = ProjectStatusReporter.Create(projeto);

        var porStatus = new Dictionary<string, int>();
        var porArquivo = new Dictionary<string, int>();
        var porFalante = new Dictionary<string, int>();
        foreach (var path in Directory.EnumerateFiles(projeto.TextDirectory, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var key = Path.GetFileNameWithoutExtension(path);
            porArquivo[key] = 0;
            foreach (var unit in doc.RootElement.GetProperty("units").EnumerateArray())
            {
                porArquivo[key]++;
                var s = unit.GetProperty("status").GetString()!;
                porStatus[s] = porStatus.GetValueOrDefault(s) + 1;
                var f = unit.GetProperty("speaker").GetString() ?? "";
                porFalante[f] = porFalante.GetValueOrDefault(f) + 1;
            }
        }

        Assert.Equal(porArquivo.Values.Sum(), status.Total.Total);
        Assert.Equal(porStatus.GetValueOrDefault("untranslated"), status.Total.Untranslated);
        Assert.Equal(porStatus.GetValueOrDefault("machine"), status.Total.Machine);
        Assert.Equal(porStatus.GetValueOrDefault("translated"), status.Total.Translated);
        Assert.Equal(porStatus.GetValueOrDefault("reviewed"), status.Total.Reviewed);
        Assert.Equal(porStatus.GetValueOrDefault("approved"), status.Total.Approved);
        Assert.Equal(porArquivo.Count, status.Files.Count);
        foreach (var file in status.Files)
        {
            Assert.Equal(porArquivo[file.Key], file.Counts.Total);
        }

        Assert.Equal(porFalante.Count, status.Characters.Count);
        foreach (var character in status.Characters)
        {
            Assert.Equal(porFalante[character.Speaker ?? ""], character.Counts.Total);
        }
    }

    [Fact]
    public void Arquivos_saem_pela_chave_e_cada_um_tem_a_propria_contagem()
    {
        using var temp = new TempFolder();
        var status = ProjectStatusReporter.Create(ProjetoComTextos(temp.Path));

        Assert.Equal(["yst00001", "yst00002", "yst00003"], status.Files.Select(f => f.Key));
        var primeiro = status.Files[0].Counts;
        Assert.Equal(new StatusCounts(1, 1, 1, 0, 1), primeiro);
        Assert.Equal(new StatusCounts(1, 1, 0, 1, 0), status.Files[1].Counts);
        Assert.Equal(0, status.Files[2].Counts.Total);
    }

    [Fact]
    public void Personagens_ordenam_por_linhas_e_sem_falante_vem_por_ultimo()
    {
        using var temp = new TempFolder();
        var status = ProjectStatusReporter.Create(ProjetoComTextos(temp.Path));

        Assert.Equal(["美咲", "健", null], status.Characters.Select(c => c.Speaker));
        Assert.Equal(new StatusCounts(1, 2, 0, 0, 0), status.Characters[0].Counts);
        Assert.Equal(2, status.Characters[2].Counts.Total);
    }

    [Fact]
    public void Falante_vazio_conta_como_sem_falante()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("a", [Linha("a", 1, "", TranslationStatus.Untranslated), Linha("a", 2, null, TranslationStatus.Untranslated)]);

        var status = ProjectStatusReporter.Create(projeto);

        var unico = Assert.Single(status.Characters);
        Assert.Null(unico.Speaker);
        Assert.Equal(2, unico.Counts.Total);
    }

    [Fact]
    public void Projeto_sem_textos_da_tudo_zerado()
    {
        using var temp = new TempFolder();
        var status = ProjectStatusReporter.Create(NovoProjeto(temp.Path));

        Assert.Equal(0, status.Total.Total);
        Assert.Empty(status.Files);
        Assert.Empty(status.Characters);
        Assert.Equal(0, status.Total.Percent);
    }

    [Fact]
    public void Percent_conta_as_linhas_com_qualquer_status_diferente_de_untranslated()
    {
        var counts = new StatusCounts(1, 1, 1, 0, 1);

        Assert.Equal(4, counts.Total);
        Assert.Equal(3, counts.Done);
        Assert.Equal(75, counts.Percent);
    }
}
