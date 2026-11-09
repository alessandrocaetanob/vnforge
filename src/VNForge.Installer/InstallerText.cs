using System.Globalization;

namespace VNForge.Installer;

/// <summary>Os números e as listas que as janelas escrevem. O instalador roda com <c>InvariantGlobalization</c>, então a vírgula decimal e os plurais ficam aqui, à mão.</summary>
public static class InstallerText
{
    /// <summary>Vazão que a estimativa do "Leva cerca de" supõe para copiar, aplicar e conferir: um disco comum, de propósito pessimista.</summary>
    private const double BytesPerSecond = 30d * 1024 * 1024;

    public static string Files(int count) => count == 1 ? "1 arquivo" : $"{count.ToString(CultureInfo.InvariantCulture)} arquivos";

    public static string Size(long bytes)
    {
        const double kilo = 1024;
        return bytes switch
        {
            < 1024 => $"{bytes.ToString(CultureInfo.InvariantCulture)} B",
            < 1024 * 1024 => $"{Round(bytes / kilo)} KB",
            < 1024L * 1024 * 1024 => $"{Round(bytes / kilo / kilo)} MB",
            _ => $"{Round(bytes / kilo / kilo / kilo)} GB",
        };

        static string Round(double value) => value >= 100
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
    }

    /// <summary>"0,2 s", "12 s", "1 min 5 s".</summary>
    public static string Elapsed(TimeSpan time)
    {
        var seconds = time.TotalSeconds;
        if (seconds < 10)
        {
            return $"{Math.Max(0.1, Math.Round(seconds, 1)).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')} s";
        }

        if (seconds < 60)
        {
            return $"{Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture)} s";
        }

        var whole = (int)Math.Round(seconds);
        return $"{(whole / 60).ToString(CultureInfo.InvariantCulture)} min {(whole % 60).ToString(CultureInfo.InvariantCulture)} s";
    }

    public static string Percent(double fraction) => $"{Math.Clamp((int)Math.Round(fraction * 100), 0, 100).ToString(CultureInfo.InvariantCulture)}%";

    /// <summary>"script.ypf, Game.exe e font.ypf"; passando de <paramref name="max"/>, "script.ypf, Game.exe, font.ypf e mais 3 arquivos".</summary>
    public static string NameList(IReadOnlyList<string> names, int max = 4)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count <= max)
        {
            return $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";
        }

        var rest = names.Count - (max - 1);
        return $"{string.Join(", ", names.Take(max - 1))} e mais {Files(rest)}";
    }

    /// <summary>"3f9a…c21d": os quatro primeiros e os quatro últimos do SHA-256.</summary>
    public static string ShortHash(string? sha) => sha is { Length: > 12 } ? $"{sha[..4]}…{sha[^4..]}" : sha ?? "(nenhum)";

    /// <summary>A frase de duração da janela "Instalando", a partir dos bytes que o instalador vai copiar, aplicar e conferir.</summary>
    public static string EstimateSentence(long totalBytes)
    {
        var seconds = totalBytes / BytesPerSecond;
        if (seconds < 5)
        {
            return "Leva poucos segundos.";
        }

        if (seconds < 90)
        {
            return $"Leva cerca de {((int)(Math.Round(seconds / 5) * 5)).ToString(CultureInfo.InvariantCulture)} segundos.";
        }

        var minutes = (int)Math.Ceiling(seconds / 60);
        return $"Leva cerca de {minutes.ToString(CultureInfo.InvariantCulture)} minutos.";
    }

    /// <summary>A versão que o executável traz em suas propriedades (<c>1.01</c>; vírgulas de "1, 0, 1, 0" viram pontos), ou nula se o arquivo não existe ou não a diz.</summary>
    public static string? FileVersionOf(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var raw = !string.IsNullOrWhiteSpace(info.FileVersion) ? info.FileVersion : info.ProductVersion;
            var version = raw?.Replace(", ", ".", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal).Trim();
            return string.IsNullOrEmpty(version) || version is "0.0.0.0" or "0.0.0" ? null : version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static string FileName(string relativePath) => relativePath[(relativePath.LastIndexOfAny(['/', '\\']) + 1)..];
}
