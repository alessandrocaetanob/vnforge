namespace VNForge.Core.Jmdict;

/// <summary>
/// Onde o índice do JMdict mora: uma pasta do usuário (no app, <c>dictionary/</c> dentro da pasta de configuração). O JMdict nunca entra no repositório nem no pacote do VNForge;
/// ele é baixado numa ação explícita da pessoa e atualizado do mesmo jeito. A licença do EDRDG pede um procedimento de atualização regular, então o índice com mais de
/// <see cref="StaleAfter"/> aparece como "desatualizado" para o app oferecer baixar de novo.
/// </summary>
public sealed class JmdictStore(string directory)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    public string Directory { get; } = directory;

    public string IndexPath => Path.Combine(Directory, "jmdict.vnfdict");

    public bool IsInstalled => File.Exists(IndexPath);

    /// <summary>Quando o índice foi montado (a data do arquivo); nulo sem dicionário instalado.</summary>
    public DateTimeOffset? InstalledAt => IsInstalled ? new DateTimeOffset(File.GetLastWriteTimeUtc(IndexPath), TimeSpan.Zero) : null;

    public bool IsStale(DateTimeOffset now) => InstalledAt is { } at && now - at > StaleAfter;

    /// <summary>Abre o índice instalado; nulo quando o dicionário ainda não foi baixado.</summary>
    /// <exception cref="JmdictFileException">O arquivo existe mas está estragado: a pessoa baixa de novo.</exception>
    public JmdictIndex? Open() => IsInstalled ? JmdictIndex.Open(IndexPath) : null;
}
