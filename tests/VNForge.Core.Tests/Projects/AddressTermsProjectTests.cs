using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>Os tratamentos entre personagens (#470) de ponta a ponta: o build, o QA do projeto, o Painel e o contexto das linhas do Editor.</summary>
public sealed class AddressTermsProjectTests : IDisposable
{
    private const string Script = "story04";
    private const string Chan = "ミサキちゃん、おはよう";

    private static int writes;

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();

    public AddressTermsProjectTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _temp.Dispose();

    private static readonly AddressTerm[] Kenta = [new("Kenta", "Misaki", "ミサキちゃん", "Misaki-chan")];

    // A primeira fala do script a001 vira uma fala de Kenta que chama Misaki; a tradução é a que o teste passar.
    private void FalaDeKenta(string traducao)
    {
        var units = new List<TextUnit>(_engine.Scripts[0].Extract(
            new ScriptFile("a001.fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake"))), new FakeContext()).Units);
        units[0] = units[0] with { Source = Chan, Translation = traducao, Status = TranslationStatus.Translated, ChosenSpeaker = "Kenta" };
        _project.SaveText("a001", units);
    }

    [Fact]
    public void O_build_com_fala_fora_do_combinado_nao_trava_sem_allowWarnings()
    {
        _project.SaveAddressTerms(Kenta);
        FalaDeKenta("Bom dia");

        var qa = new QaEngine([new AddressTermRule()]);
        var report = qa.Run(QaContext.FromProject(_project));
        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: qa, allowWarnings: false);

        Assert.Equal(1, report.StyleAdviceCount);
        Assert.Empty(report.BlockingIssues);
        Assert.NotNull(result);
    }

    [Fact]
    public void O_build_com_o_arquivo_de_tratamentos_ilegivel_segue_e_avisa()
    {
        File.WriteAllText(_project.AddressTermsPath, "{ isto não é json");
        FalaDeKenta("Bom dia");

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new AddressTermRule()]));

        Assert.Contains(result.Warnings, w => w.Contains("address-terms.json", StringComparison.Ordinal) && w.Contains("ignorado", StringComparison.Ordinal));
        Assert.Equal("{ isto não é json", File.ReadAllText(_project.AddressTermsPath));
    }

    [Fact]
    public void O_contexto_do_projeto_le_os_tratamentos_do_arquivo()
    {
        _project.SaveAddressTerms(Kenta);

        var context = QaContext.FromProject(_project);

        Assert.Equal(Kenta, context.AddressTerms);
        Assert.False(context.AddressBook.IsEmpty);
    }

    [Fact]
    public void O_painel_conta_o_aviso_de_tratamento_como_estilo_e_nao_como_problema()
    {
        _project.SaveAddressTerms(Kenta);
        FalaDeKenta("Bom dia");

        var qa = ProjectDashboard.Read(_project).Qa;

        Assert.NotNull(qa);
        Assert.Equal(1, qa.StyleAdvice);
        Assert.Equal(0, qa.Total);
    }

    [Fact]
    public void O_painel_ignora_o_arquivo_de_tratamentos_ilegivel_e_ainda_confere_o_resto()
    {
        File.WriteAllText(_project.AddressTermsPath, "{ isto não é json");
        FalaDeKenta("Bom dia");

        var qa = ProjectDashboard.Read(_project).Qa;

        Assert.NotNull(qa);
        Assert.Equal(0, qa.StyleAdvice);
    }

    // O contexto confere a data de gravação e o tamanho; dois arquivos gravados no mesmo instante e do mesmo tamanho não contariam como mudança.
    private static void Distinguir(string arquivo) =>
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref writes)));

    private static ProjectFolder ProjetoDeLinhas(string raiz)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "VN",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText(Script, [new TextUnit($"{Script}:000001", TextUnitKind.Dialogue, "Kenta", Chan, "Misaki-chan, bom dia", TranslationStatus.Translated)]);
        return projeto;
    }

    private static bool Avisa(LineEntry linha) => linha.Issues.Any(i => i.RuleId == AddressTermRule.RuleId);

    [Fact]
    public void As_linhas_do_Editor_releem_o_arquivo_guardam_o_ultimo_bom_e_esvaziam_quando_ele_some()
    {
        using var pasta = new TempFolder();
        var projeto = ProjetoDeLinhas(pasta.Path);
        var linhas = ProjectLines.Read(projeto);
        var entrada = linhas.LinesOf(Script).Single();
        Assert.False(Avisa(entrada));

        // Aparece o arquivo: a mesma fala, sem o tratamento combinado, passa a avisar.
        projeto.SaveAddressTerms(Kenta);
        Distinguir(projeto.AddressTermsPath);
        var semTratamento = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Misaki, bom dia" });
        Assert.True(Avisa(semTratamento));

        // O arquivo fica ilegível: vale o último contexto bom.
        File.WriteAllText(projeto.AddressTermsPath, "{ quebrado");
        Distinguir(projeto.AddressTermsPath);
        var aindaAvisa = linhas.WithUnit(semTratamento, semTratamento.Unit with { Translation = "Misaki, boa noite" });
        Assert.True(Avisa(aindaAvisa));

        // O arquivo some: a lista esvazia e o aviso vai embora.
        File.Delete(projeto.AddressTermsPath);
        var semArquivo = linhas.WithUnit(aindaAvisa, aindaAvisa.Unit with { Translation = "Misaki, boa tarde" });
        Assert.False(Avisa(semArquivo));
    }
}
