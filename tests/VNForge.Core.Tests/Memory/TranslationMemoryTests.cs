using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Memory;

/// <summary>A memória de tradução entre projetos (#537): os pares aprovados de um projeto aparecem como sugestão em outro. Todo texto aqui é inventado no teste.</summary>
[Trait("Risco", "R2")]
public sealed class TranslationMemoryTests : IDisposable
{
    private static readonly DateTimeOffset Dia1 = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Dia2 = Dia1.AddDays(18);

    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private string Config => Path.Combine(temp.Path, "config");

    private TranslationMemory Memoria() => new(Config);

    private static TextUnit Linha(int n, string original, string? traducao, TranslationStatus status = TranslationStatus.Approved) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private ProjectFolder NovoProjeto(string pasta, string nome, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(Path.Combine(temp.Path, pasta), new ProjectConfig(
            nome,
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00001", linhas);
        return projeto;
    }

    private void EscreverArquivo(string json)
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, TranslationMemory.FileName), json);
    }

    [Fact]
    public void Entrada_nula_e_projeto_sem_chave_tambem_sao_recusados()
    {
        EscreverArquivo("""{"version":1,"entries":[null]}""");
        Assert.Throws<MemoryFileException>(() => Memoria().Find(Frase));

        EscreverArquivo("""{"version":1,"entries":[{"source":"a","target":"b","projectKey":" ","projectName":"A","unitId":"u:1"}]}""");
        Assert.Throws<MemoryFileException>(() => Memoria().Find(Frase));
    }
}
