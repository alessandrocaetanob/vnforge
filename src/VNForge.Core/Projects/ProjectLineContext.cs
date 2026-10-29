using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// O contexto do QA das linhas de um projeto (glossário, personagens, estilo, perfil de formatação e encoder, sem os textos), mantido em dia
/// com os arquivos: cada arquivo só é lido de novo quando a data de gravação ou o tamanho muda, então pedir o contexto a cada gravação de linha
/// custa um <c>stat</c> por arquivo. Um arquivo que não abre (gravado pela metade, fora de UTF-8) deixa aquela parte como estava.
/// </summary>
internal sealed class ProjectLineContext
{
    private readonly ProjectFolder project;
    private readonly Lock gate = new();
    private QaContext current;
    private FileStamp config;
    private FileStamp formatting;
    private FileStamp glossary;
    private FileStamp characters;
    private FileStamp addressTerms;

    /// <param name="project">A pasta do projeto.</param>
    /// <param name="initial">O contexto lido de <paramref name="project"/> (os textos são ignorados).</param>
    /// <param name="stamps">As datas dos arquivos tiradas <em>antes</em> da leitura de <paramref name="initial"/>, para uma gravação no meio não passar batida.</param>
    public ProjectLineContext(ProjectFolder project, QaContext initial, Stamps stamps)
    {
        this.project = project;
        current = initial with { Texts = new Dictionary<string, IReadOnlyList<TextUnit>>() };
        config = stamps.Config;
        formatting = stamps.Formatting;
        glossary = stamps.Glossary;
        characters = stamps.Characters;
        addressTerms = stamps.AddressTerms;
    }

    /// <summary>As datas de gravação dos arquivos de que o contexto depende.</summary>
    public static Stamps StampsOf(ProjectFolder project) => new(
        FileStamp.Of(project.ConfigPath),
        FileStamp.Of(project.FormattingPath),
        FileStamp.Of(project.GlossaryPath),
        FileStamp.Of(project.CharactersPath),
        FileStamp.Of(project.AddressTermsPath));

    /// <summary>O contexto de agora, com os textos vazios.</summary>
    public QaContext Current()
    {
        lock (gate)
        {
            if (Changed(project.ConfigPath, ref config) && Try(project.LoadConfig) is { } loaded)
            {
                current = current with { Style = loaded.Style, Encoder = Try(() => TextEncoderRegistry.Default.Create(loaded.Encoding.Strategy)) ?? current.Encoder };
            }

            if (Changed(project.FormattingPath, ref formatting) && Try(project.LoadFormatting) is { } profile)
            {
                current = current with { Formatting = profile };
            }

            if (Changed(project.GlossaryPath, ref glossary) && Try(project.LoadGlossary) is { } terms)
            {
                current = current with { Glossary = terms };
            }

            if (Changed(project.CharactersPath, ref characters) && Try(project.LoadCharacters) is { } people)
            {
                current = current with { Characters = people };
            }

            if (Changed(project.AddressTermsPath, ref addressTerms) && Try(project.LoadAddressTerms) is { } pairs)
            {
                current = current with { AddressTerms = pairs };
            }

            return current;
        }
    }

    private static bool Changed(string path, ref FileStamp known)
    {
        var now = FileStamp.Of(path);
        if (now == known)
        {
            return false;
        }

        known = now;
        return true;
    }

    private static T? Try<T>(Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
    {
        public static FileStamp Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : new FileStamp(default, -1);
        }
    }

    internal readonly record struct Stamps(FileStamp Config, FileStamp Formatting, FileStamp Glossary, FileStamp Characters, FileStamp AddressTerms);
}
