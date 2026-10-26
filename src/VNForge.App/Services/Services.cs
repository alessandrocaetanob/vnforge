using System.Globalization;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>Abre o seletor de pasta do sistema. Atrás de uma interface para os ViewModels rodarem nos testes sem janela.</summary>
public interface IFolderPicker
{
    /// <summary>Devolve o caminho escolhido, ou <c>null</c> se o usuário cancelou.</summary>
    Task<string?> PickFolderAsync(string title);
}

/// <summary>Datas relativas até 7 dias ("há 2 min", "Ontem"); depois, <c>17/10/2026</c>.</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var local = when.ToOffset(now.Offset);
        var elapsed = now - local;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "agora";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"há {(int)elapsed.TotalMinutes} min";
        }

        var days = now.Date.Subtract(local.Date).Days;
        return days switch
        {
            0 => $"há {(int)elapsed.TotalHours} h",
            1 => "Ontem",
            <= 7 => $"há {days} dias",
            _ => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>Rótulos em português para ids que o Core guarda em inglês ou em código.</summary>
public static class ProjectLabels
{
    // Formato pt-BR fixo (48.211): não depende do ICU da máquina nem da cultura do usuário.
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Engine(string id, int? version) => id switch
    {
        "yuris" => version is { } v ? $"YU-RIS · YPF v{v}" : "YU-RIS",
        _ => id,
    };

    /// <summary><c>claude-opus-5-5</c> vira <c>Claude Opus 5.5</c>; id fora do padrão volta como veio.</summary>
    public static string Model(string id)
    {
        var parts = id.Split('-');
        if (parts.Length < 4 || parts[0] != "claude" || !parts[2..].All(p => p.All(char.IsAsciiDigit)))
        {
            return id;
        }

        var family = char.ToUpperInvariant(parts[1][0]) + parts[1][1..];
        return $"Claude {family} {string.Join('.', parts[2..])}";
    }

    /// <summary>"menos de 1 min", "cerca de 6 min", "cerca de 1 h 30 min": o tempo de uma estimativa, sem falsa precisão.</summary>
    public static string Duration(TimeSpan time)
    {
        var minutes = (int)Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1)
        {
            return "menos de 1 min";
        }

        if (minutes < 60)
        {
            return $"cerca de {minutes} min";
        }

        var rounded = (int)Math.Round(minutes / 5.0, MidpointRounding.AwayFromZero) * 5;
        var hours = rounded / 60;
        var rest = rounded % 60;
        return rest == 0 ? $"cerca de {hours} h" : $"cerca de {hours} h {rest} min";
    }

    public static string Encoding(string strategy) => strategy switch
    {
        "fold" => "Remover acentos (fold)",
        "remap" => "Remapear glifos (remap)",
        "cp1252" => "CP1252 (patch opcional da fonte)",
        "sjis" => "Shift-JIS puro",
        _ => strategy,
    };

    /// <summary>O nome curto da estratégia de acentos para a barra de status, o do cartão em linguagem comum: "sem acentos", "ajuste no jogo".</summary>
    public static string EncodingShort(string strategy) => strategy switch
    {
        "cp1252" => "ajuste no jogo",
        "sjis" => "forma antiga",
        "fold" => "sem acentos",
        "remap" => "fonte modificada",
        _ => strategy,
    };

    public static string Status(TranslationStatus status) => status switch
    {
        TranslationStatus.Untranslated => "Em japonês",
        TranslationStatus.Machine => "IA",
        TranslationStatus.Translated => "Sua",
        TranslationStatus.Reviewed or TranslationStatus.Approved => "Pronta",
        _ => status.ToString(),
    };

    public static string Gender(CharacterGender gender) => gender switch
    {
        CharacterGender.Female => "Feminino",
        CharacterGender.Male => "Masculino",
        CharacterGender.Other => "Outro",
        _ => "Não definido",
    };

    public static string Number(int value) => value.ToString("N0", PtBr);

    public static string Scripts(int count) => $"{Number(count)} {(count == 1 ? "script" : "scripts")}";

    /// <summary>"1 arquivo de roteiro", "1.254 arquivos de roteiro": o mesmo termo do passo da pasta e da estimativa de gasto.</summary>
    public static string ScriptFiles(int count) => $"{Number(count)} {(count == 1 ? "arquivo" : "arquivos")} de roteiro";
}
