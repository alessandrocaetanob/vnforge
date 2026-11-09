using System.IO.Compression;
using System.Net;
using System.Text;
using VNForge.App.Services;
using VNForge.Core.Jmdict;

namespace VNForge.App.Tests;

/// <summary>Um JMdict inventado no teste (com o DTD e as entidades do arquivo real) e a pasta do dicionário offline montada em cima dele. Nenhum texto do dicionário de verdade entra no repositório.</summary>
internal static class SyntheticDictionary
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE JMdict [
        <!ENTITY n "noun (common) (futsuumeishi)">
        <!ENTITY vs "noun or participle which takes the aux. verb suru">
        <!ENTITY vi "intransitive verb">
        <!ENTITY v5k-s "Godan verb - Iku/Yuku special class">
        <!ENTITY v5u "Godan verb with 'u' ending">
        <!ENTITY vt "transitive verb">
        <!ENTITY cop "copula">
        ]>
        <!-- JMdict created: 2026-11-09 -->
        <JMdict>
        <entry><ent_seq>1</ent_seq><k_ele><keb>遅刻</keb><ke_pri>news1</ke_pri></k_ele><r_ele><reb>ちこく</reb></r_ele>
        <sense><pos>&n;</pos><pos>&vs;</pos><gloss>lateness</gloss><gloss>tardiness</gloss></sense>
        <sense><pos>&vs;</pos><gloss>being late (for work, school)</gloss></sense></entry>
        <entry><ent_seq>2</ent_seq><k_ele><keb>行く</keb><ke_pri>ichi1</ke_pri></k_ele><k_ele><keb>往く</keb></k_ele><r_ele><reb>いく</reb></r_ele><r_ele><reb>ゆく</reb></r_ele>
        <sense><pos>&v5k-s;</pos><pos>&vi;</pos><gloss>to go</gloss><gloss>to move (towards)</gloss></sense>
        <sense><gloss>to proceed</gloss></sense>
        <sense><gloss>to head towards</gloss></sense>
        <sense><gloss>to pass through</gloss></sense>
        <sense><gloss>to get along</gloss></sense>
        <sense><gloss>to die</gloss></sense>
        <sense><gloss>to walk</gloss></sense>
        <sense><gloss>to travel</gloss></sense>
        <sense><gloss>to leave</gloss></sense>
        <sense><gloss>to flow</gloss></sense></entry>
        <entry><ent_seq>3</ent_seq><k_ele><keb>行う</keb></k_ele><r_ele><reb>おこなう</reb></r_ele><sense><pos>&v5u;</pos><pos>&vt;</pos><gloss>to perform</gloss></sense></entry>
        <entry><ent_seq>4</ent_seq><k_ele><keb>二人</keb><ke_pri>news1</ke_pri></k_ele><r_ele><reb>ふたり</reb></r_ele><sense><pos>&n;</pos><gloss>two people</gloss></sense></entry>
        <entry><ent_seq>5</ent_seq><k_ele><keb>五分</keb></k_ele><r_ele><reb>ごふん</reb></r_ele><sense><pos>&n;</pos><gloss>five minutes</gloss></sense></entry>
        <entry><ent_seq>6</ent_seq><r_ele><reb>だ</reb></r_ele><sense><pos>&cop;</pos><gloss>is</gloss></sense></entry>
        </JMdict>
        """;

    /// <summary>Grava o índice do dicionário inventado na pasta e devolve o dicionário do app já carregado.</summary>
    public static async Task<OfflineDictionary> InstalledAsync(string folder, Func<HttpClient>? http = null, Action<Action>? postToUi = null)
    {
        var store = new JmdictStore(folder);
        Directory.CreateDirectory(folder);
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(Xml)))
        using (var output = File.Create(store.IndexPath))
        {
            JmdictImporter.Import(input, output);
        }

        var dictionary = new OfflineDictionary(store, http, postToUi: postToUi);
        await dictionary.Loaded;
        return dictionary;
    }

    public static OfflineDictionary NotInstalled(string folder, Func<HttpClient>? http = null, TimeSpan? idleTimeout = null, Action<Action>? postToUi = null) =>
        new(new JmdictStore(folder), http, postToUi: postToUi, idleTimeout: idleTimeout);

    public static byte[] Gzip()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(Xml));
        }

        return output.ToArray();
    }

    /// <summary>Um servidor que responde o cabeçalho e nunca entrega o corpo: o download só termina se for cancelado ou estourar o prazo.</summary>
    public sealed class HangingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
    }

    /// <summary>Um servidor cuja conexão estoura o tempo, como o <see cref="HttpClient.Timeout"/> faz (TaskCanceledException sem cancelamento da pessoa).</summary>
    public sealed class TimeoutServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("O tempo limite da solicitação foi atingido.", new TimeoutException());
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    /// <summary>Um servidor que responde com o arquivo inventado (ou com o erro pedido) e conta as chamadas.</summary>
    public sealed class Server(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Gzip()) });
        }
    }
}
