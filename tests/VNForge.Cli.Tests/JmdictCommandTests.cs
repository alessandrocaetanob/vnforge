using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf jmdict</c> (#538): baixa o JMdict para a pasta do usuário e consulta sem rede. O dicionário aqui é inventado no teste.</summary>
public sealed class JmdictCommandTests : IDisposable
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE JMdict [
        <!ENTITY n "noun (common) (futsuumeishi)">
        <!ENTITY v5k-s "Godan verb - Iku/Yuku special class">
        <!ENTITY vi "intransitive verb">
        ]>
        <!-- JMdict created: 2026-11-09 -->
        <JMdict>
        <entry><ent_seq>1</ent_seq><k_ele><keb>遅刻</keb></k_ele><r_ele><reb>ちこく</reb></r_ele><sense><pos>&n;</pos><gloss>lateness</gloss></sense></entry>
        <entry><ent_seq>2</ent_seq><k_ele><keb>行く</keb></k_ele><r_ele><reb>いく</reb></r_ele><sense><pos>&v5k-s;</pos><pos>&vi;</pos><gloss>to go</gloss></sense></entry>
        </JMdict>
        """;

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static byte[] Gzip()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(Xml));
        }

        return output.ToArray();
    }

    private sealed class Server(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Gzip()) });
        }
    }

    private Task<RunResult> Run(params string[] args) => _cli.RunAsync(args, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Consultar_antes_de_instalar_pede_para_instalar()
    {
        var result = await Run("jmdict", "lookup", "遅刻");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("vnf jmdict install", result.Output + result.Error);
    }

    [Fact]
    public async Task Instala_o_dicionario_e_depois_consulta_sem_rede()
    {
        var server = new Server();
        _cli.Cli.HttpClientFactory = () => new HttpClient(server);

        var install = await Run("jmdict", "install");
        var visitas = server.Requests;
        var lookup = await Run("jmdict", "lookup", "行った遅刻");
        var status = await Run("jmdict", "status");

        Assert.Equal(0, install.ExitCode);
        Assert.Contains("2 entradas", install.Output);
        Assert.Contains("EDRDG", install.Output);
        Assert.Equal(1, visitas);
        Assert.Equal(0, lookup.ExitCode);
        Assert.Contains("行く", lookup.Output);
        Assert.Contains("passada", lookup.Output);
        Assert.Contains("遅刻", lookup.Output);
        Assert.Equal(visitas, server.Requests);
        Assert.Contains("Em dia", status.Output);
    }

    [Fact]
    public async Task Json_da_instalacao_e_da_consulta_traz_as_entradas_a_palavra_a_conjugacao_e_os_sentidos()
    {
        _cli.Cli.HttpClientFactory = () => new HttpClient(new Server());
        var install = await Run("jmdict", "install", "--json");

        var result = await Run("jmdict", "lookup", "行こう", "--json");

        using var installed = JsonDocument.Parse(install.Output);
        Assert.Equal(2, installed.RootElement.GetProperty("data").GetProperty("entries").GetInt32());
        using var json = JsonDocument.Parse(result.Output);
        var word = json.RootElement.GetProperty("data").GetProperty("words")[0];
        Assert.Equal("行く", word.GetProperty("word").GetString());
        Assert.Equal("volitiva", word.GetProperty("inflection")[0].GetString());
        Assert.Equal("to go", word.GetProperty("entries")[0].GetProperty("senses")[0].GetString());
    }

    [Fact]
    public async Task Falha_do_servidor_vira_erro_com_mensagem_e_nao_deixa_dicionario()
    {
        _cli.Cli.HttpClientFactory = () => new HttpClient(new Server(HttpStatusCode.ServiceUnavailable));

        var install = await Run("jmdict", "install");
        var status = await Run("jmdict", "status");

        Assert.NotEqual(0, install.ExitCode);
        Assert.Contains("503", install.Output + install.Error);
        Assert.Contains("não está instalado", status.Output);
    }
}
