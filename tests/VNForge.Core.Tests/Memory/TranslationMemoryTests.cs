using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Memory;

/// <summary>A memória de tradução entre projetos (#537): os pares aprovados de um projeto aparecem como sugestão em outro. Todo texto aqui é inventado no teste.</summary>
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

    [Fact]
    public void Uma_linha_aprovada_num_projeto_aparece_como_sugestao_em_outro()
    {
        var estrela = NovoProjeto("estrela", "Hoshizora no Kakera", Linha(1, Frase, "Aizawa, Sakura. Vocês dois, cinco minutos atrasados."));
        var memoria = Memoria();
        memoria.SyncProject(estrela, Dia1);
        var outro = NovoProjeto("outro", "Amaoto no Hakoniwa", Linha(7, Frase, "Aizawa, Sakura. Os dois, cinco minutos atrasados.", TranslationStatus.Machine));

        var achadas = new TranslationMemory(Config).Find(Frase, TranslationMemory.ProjectKeyOf(outro));

        var achada = Assert.Single(achadas);
        Assert.True(achada.IsExact);
        Assert.Equal(100, achada.Percent);
        Assert.Equal("Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", achada.Entry.Target);
        Assert.Equal("Hoshizora no Kakera", achada.Entry.ProjectName);
        Assert.Equal(Dia1, achada.Entry.ApprovedAt);
    }

    [Fact]
    public void Linha_que_nao_esta_aprovada_nao_entra()
    {
        var projeto = NovoProjeto(
            "p",
            "P",
            Linha(1, "あ１", "A1", TranslationStatus.Machine),
            Linha(2, "あ２", "A2", TranslationStatus.Reviewed),
            Linha(3, "あ３", "A3", TranslationStatus.Keep),
            Linha(4, "あ４", null),
            Linha(5, "あ５", "   "),
            Linha(6, "   ", "vazio"));
        var memoria = Memoria();

        var resultado = memoria.SyncProject(projeto, Dia1);

        Assert.Equal(new MemorySyncResult(0, 0, 0, 0), resultado);
        Assert.False(File.Exists(memoria.Path));
    }

    [Fact]
    public void O_projeto_de_origem_fica_de_fora_da_busca()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Traduzida aqui."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);

        Assert.Empty(memoria.Find(Frase, TranslationMemory.ProjectKeyOf(projeto)));
        Assert.Single(memoria.Find(Frase));
    }

    [Fact]
    public void A_correspondencia_aproximada_vem_com_percentual_abaixo_de_cem_e_depois_da_exata()
    {
        var a = NovoProjeto("a", "Natsu no Shirabe", Linha(1, "「相沢、二人そろって五分遅刻だ」", "Aizawa, os dois, cinco minutos atrasados."));
        var b = NovoProjeto("b", "Hoshizora no Kakera", Linha(1, Frase, "Aizawa, Sakura. Vocês dois, cinco minutos atrasados."));
        var memoria = Memoria();
        memoria.SyncProject(a, Dia2);
        memoria.SyncProject(b, Dia1);

        var achadas = memoria.Find(Frase);

        Assert.Equal(["Hoshizora no Kakera", "Natsu no Shirabe"], achadas.Select(m => m.Entry.ProjectName));
        Assert.Equal(100, achadas[0].Percent);
        Assert.InRange(achadas[1].Percent, 80, 99);
        Assert.False(achadas[1].IsExact);
    }

    [Fact]
    public void Texto_diferente_demais_nao_e_sugerido()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, "今日はいい天気ですね、散歩に行きましょう。", "Que dia bonito, vamos passear."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);

        Assert.Empty(memoria.Find(Frase));
        Assert.Empty(memoria.Find("今日はいい天気ですね。"));
    }

    [Fact]
    public void Marcadores_do_jogo_e_espacos_nao_contam_na_comparacao()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, "おはよう、先輩。\\p", "Bom dia, senpai."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);

        var achada = Assert.Single(memoria.Find("おはよう、先輩。  \\n \\p "));

        Assert.Equal(100, achada.Percent);
    }

    [Fact]
    public void O_mesmo_texto_aprovado_duas_vezes_no_projeto_aparece_uma_vez()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."), Linha(2, Frase, "Atrasados."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);

        Assert.Single(memoria.Find(Frase));
        Assert.Equal(new MemoryStats(2, 1), memoria.Stats());
    }

    [Fact]
    public void Traducoes_diferentes_do_mesmo_projeto_aparecem_todas_da_mais_recente_a_mais_antiga()
    {
        var a = NovoProjeto("a", "A", Linha(1, Frase, "Primeira."));
        var b = NovoProjeto("b", "B", Linha(1, Frase, "Segunda."));
        var memoria = Memoria();
        memoria.SyncProject(a, Dia1);
        memoria.SyncProject(b, Dia2);

        Assert.Equal(["Segunda.", "Primeira."], memoria.Find(Frase).Select(m => m.Entry.Target));
    }

    [Fact]
    public void O_limite_de_sugestoes_corta_a_lista()
    {
        var memoria = Memoria();
        for (var i = 0; i < 4; i++)
        {
            memoria.SyncProject(NovoProjeto($"p{i}", $"P{i}", Linha(1, Frase, $"Versão {i}.")), Dia1.AddDays(i));
        }

        Assert.Equal(2, memoria.Find(Frase, limit: 2).Count);
        Assert.Equal("Versão 3.", memoria.Find(Frase, limit: 2)[0].Entry.Target);
    }

    [Fact]
    public void Sincronizar_de_novo_nao_muda_a_data_nem_regrava_o_arquivo()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);
        var antes = File.GetLastWriteTimeUtc(memoria.Path);
        Thread.Sleep(20);

        var resultado = memoria.SyncProject(projeto, Dia2);

        Assert.False(resultado.Changed);
        Assert.Equal(1, resultado.ProjectTotal);
        Assert.Equal(antes, File.GetLastWriteTimeUtc(memoria.Path));
        Assert.Equal(Dia1, memoria.All().Single().ApprovedAt);
    }

    [Fact]
    public void Mudar_a_traducao_aprovada_troca_o_par_e_desaprovar_tira_da_memoria()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."), Linha(2, "あ", "A."));
        var memoria = Memoria();
        memoria.SyncProject(projeto, Dia1);
        projeto.SaveText("yst00001", [Linha(1, Frase, "Chegaram tarde."), Linha(2, "あ", "A.", TranslationStatus.Translated)]);

        var resultado = memoria.SyncProject(projeto, Dia2);

        Assert.Equal(new MemorySyncResult(0, 1, 1, 1), resultado);
        var par = Assert.Single(memoria.All());
        Assert.Equal("Chegaram tarde.", par.Target);
        Assert.Equal(Dia2, par.ApprovedAt);
    }

    [Fact]
    public void Sincronizar_um_projeto_nao_mexe_nos_pares_dos_outros()
    {
        var a = NovoProjeto("a", "A", Linha(1, "あ", "A."));
        var b = NovoProjeto("b", "B", Linha(1, "い", "I."));
        var memoria = Memoria();
        memoria.SyncProject(a, Dia1);
        memoria.SyncProject(b, Dia1);

        a.SaveText("yst00001", [Linha(1, "あ", "A.", TranslationStatus.Translated)]);
        memoria.SyncProject(a, Dia2);

        Assert.Equal(["い"], memoria.All().Select(e => e.Source));
    }

    [Fact]
    public void Record_acompanha_a_aprovacao_linha_a_linha()
    {
        var memoria = Memoria();
        var chave = TranslationMemory.KeyOf(Path.Combine(temp.Path, "p"));
        var aprovada = Linha(1, Frase, "Atrasados.");

        Assert.True(memoria.Record(chave, "P", aprovada, Dia1));
        Assert.False(memoria.Record(chave, "P", aprovada, Dia2));
        Assert.True(memoria.Record(chave, "P", aprovada with { Translation = "Chegaram tarde." }, Dia2));
        Assert.Equal("Chegaram tarde.", memoria.All().Single().Target);

        Assert.True(memoria.Record(chave, "P", aprovada with { Status = TranslationStatus.Translated }, Dia2));
        Assert.Empty(memoria.All());
        Assert.False(memoria.Record(chave, "P", aprovada with { Status = TranslationStatus.Translated }, Dia2));
    }

    [Fact]
    public void RemoveProject_tira_so_os_pares_dele()
    {
        var a = NovoProjeto("a", "A", Linha(1, "あ", "A."));
        var b = NovoProjeto("b", "B", Linha(1, "い", "I."));
        var memoria = Memoria();
        memoria.SyncProject(a, Dia1);
        memoria.SyncProject(b, Dia1);

        Assert.Equal(1, memoria.RemoveProject(TranslationMemory.ProjectKeyOf(a)));

        Assert.Equal(["い"], memoria.All().Select(e => e.Source));
        Assert.Equal(0, memoria.RemoveProject(TranslationMemory.ProjectKeyOf(a)));
    }

    [Fact]
    public void Arquivo_ilegivel_da_erro_em_portugues_e_nao_e_sobrescrito()
    {
        Directory.CreateDirectory(Config);
        var caminho = Path.Combine(Config, TranslationMemory.FileName);
        File.WriteAllText(caminho, "{ isto não é a memória");
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."));
        var memoria = Memoria();

        var erro = Assert.Throws<MemoryFileException>(() => memoria.SyncProject(projeto, Dia1));

        Assert.Contains(caminho, erro.Message);
        Assert.Equal("{ isto não é a memória", File.ReadAllText(caminho));
        Assert.Throws<MemoryFileException>(() => memoria.Find(Frase));
    }

    [Fact]
    public void O_arquivo_guarda_japones_sem_escape_e_le_de_volta_em_outra_instancia()
    {
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."));
        Memoria().SyncProject(projeto, Dia1);

        var texto = File.ReadAllText(Path.Combine(Config, TranslationMemory.FileName));

        Assert.Contains(Frase, texto);
        Assert.StartsWith("{\n  \"version\": 1,", texto);
        Assert.Single(new TranslationMemory(Config).Find(Frase));
    }

    [Fact]
    public void A_busca_enxerga_o_que_outra_instancia_gravou_depois_de_carregar()
    {
        var memoria = Memoria();
        Assert.Empty(memoria.Find(Frase));

        new TranslationMemory(Config).SyncProject(NovoProjeto("p", "P", Linha(1, Frase, "Atrasados.")), Dia1);

        Assert.Single(memoria.Find(Frase));
    }

    [Fact]
    public void Muitos_pares_continuam_achando_o_igual_e_o_parecido()
    {
        var linhas = Enumerable.Range(0, 20_000)
            .Select(i => Linha(i, $"今日は{i}番目の話をしよう、ねえ{i % 97}。", $"Hoje vamos falar da {i}ª."))
            .ToArray();
        var memoria = Memoria();
        memoria.SyncProject(NovoProjeto("grande", "Grande", linhas), Dia1);

        var achadas = memoria.Find($"今日は777番目の話をしよう、ねえ{777 % 97}。");

        Assert.NotEmpty(achadas);
        Assert.Equal(100, achadas[0].Percent);
        Assert.Equal("Hoje vamos falar da 777ª.", achadas[0].Entry.Target);
        Assert.All(achadas.Skip(1), m => Assert.InRange(m.Percent, 80, 99));
    }

    private void EscreverArquivo(string json)
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, TranslationMemory.FileName), json);
    }

    [Fact]
    public void Entrada_sem_campo_obrigatorio_e_erro_do_arquivo_em_todas_as_operacoes()
    {
        EscreverArquivo("""{"version":1,"entries":[{"target":"x","projectKey":"D:/a","projectName":"A","unitId":"u:1","approvedAt":"2026-09-14T12:00:00+00:00"}]}""");
        var projeto = NovoProjeto("p", "P", Linha(1, Frase, "Atrasados."));
        var memoria = Memoria();

        var erro = Assert.Throws<MemoryFileException>(() => memoria.Find(Frase));
        Assert.Contains("entrada 1", erro.Message);
        Assert.Throws<MemoryFileException>(() => memoria.Stats());
        Assert.Throws<MemoryFileException>(() => memoria.Projects());
        Assert.Throws<MemoryFileException>(() => memoria.SyncProject(projeto, Dia1));
        Assert.Throws<MemoryFileException>(() => memoria.Record("D:/p", "P", Linha(1, Frase, "Atrasados."), Dia1));
        Assert.Throws<MemoryFileException>(() => memoria.RemoveProject("D:/a"));
    }

    [Fact]
    public void Entrada_nula_e_projeto_sem_chave_tambem_sao_recusados()
    {
        EscreverArquivo("""{"version":1,"entries":[null]}""");
        Assert.Throws<MemoryFileException>(() => Memoria().Find(Frase));

        EscreverArquivo("""{"version":1,"entries":[{"source":"a","target":"b","projectKey":" ","projectName":"A","unitId":"u:1"}]}""");
        Assert.Throws<MemoryFileException>(() => Memoria().Find(Frase));
    }

    [Fact]
    public void O_mesmo_par_repetido_no_arquivo_fica_com_o_ultimo_e_o_sync_nao_quebra()
    {
        EscreverArquivo("""
            {"version":1,"entries":[
              {"source":"あ","target":"velho","projectKey":"D:/a","projectName":"A","unitId":"u:1","approvedAt":"2026-09-14T12:00:00+00:00"},
              {"source":"あ","target":"novo","projectKey":"d:/A","projectName":"A","unitId":"u:1","approvedAt":"2026-09-15T12:00:00+00:00"}]}
            """);
        var memoria = Memoria();

        Assert.Equal(["novo"], memoria.All().Select(e => e.Target));
        Assert.True(memoria.Record("D:/a", "A", Linha(1, "あ", "mais novo") with { Id = "u:1" }, Dia2));
        Assert.Equal(["mais novo"], memoria.All().Select(e => e.Target));
    }

    [Fact]
    public void Record_com_arquivo_ilegivel_falha_sem_apagar_o_arquivo()
    {
        EscreverArquivo("lixo");
        var memoria = Memoria();

        Assert.Throws<MemoryFileException>(() => memoria.Record("D:/p", "P", Linha(1, Frase, "Atrasados."), Dia1));

        Assert.Equal("lixo", File.ReadAllText(memoria.Path));
    }

    [Fact]
    public void Projeto_com_o_mesmo_nome_do_atual_nao_e_sugerido_mesmo_com_outra_pasta()
    {
        var movido = NovoProjeto("antigo", "Amaoto no Hakoniwa", Linha(1, Frase, "Atrasados."));
        var outro = NovoProjeto("estrela", "Hoshizora no Kakera", Linha(1, Frase, "Chegaram tarde."));
        var memoria = Memoria();
        memoria.SyncProject(movido, Dia1);
        memoria.SyncProject(outro, Dia1);

        var achadas = memoria.Find(Frase, Path.Combine(temp.Path, "novo-lugar"), excludeProjectName: "amaoto no hakoniwa");

        Assert.Equal(["Hoshizora no Kakera"], achadas.Select(m => m.Entry.ProjectName));
    }

    [Fact]
    public void Projects_lista_cada_projeto_com_os_pares_dele()
    {
        var memoria = Memoria();
        memoria.SyncProject(NovoProjeto("a", "Zeta", Linha(1, "あ", "A."), Linha(2, "い", "I.")), Dia1);
        memoria.SyncProject(NovoProjeto("b", "Alfa", Linha(1, "う", "U.")), Dia1);

        var lista = memoria.Projects();

        Assert.Equal([("Alfa", 1), ("Zeta", 2)], lista.Select(p => (p.Name, p.Entries)));
    }
}
