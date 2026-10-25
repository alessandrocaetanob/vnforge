namespace VNForge.App.Services;

/// <summary>
/// Lê a cotação do dólar e o limite de gasto que a pessoa digita, com as mesmas regras e mensagens nos Ajustes e no assistente de novo projeto.
/// Vazio não é erro: cotação vazia volta ao valor de exemplo e limite vazio é "sem limite".
/// </summary>
public static class BudgetInput
{
    public const string RateInvalid = "Informe quanto vale 1 dólar em reais, por exemplo 5,40.";

    public const string LimitInvalid = "Use um valor em reais maior que zero, como 200,00, ou deixe vazio para não limitar.";

    /// <summary>A cotação digitada; <paramref name="rate"/> fica nulo quando o texto está vazio.</summary>
    public static bool TryReadRate(string? text, out decimal? rate, out string? error)
    {
        rate = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!MoneyParser.TryParse(text, out var parsed) || parsed <= 0m || parsed > AppSettings.MaxBrlPerUsd)
        {
            error = RateInvalid;
            return false;
        }

        rate = parsed;
        return true;
    }

    /// <summary>Vazio é "sem limite"; um valor precisa ser maior que zero.</summary>
    public static bool TryReadLimit(string? text, out decimal? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (MoneyParser.TryParse(text, out var parsed) && parsed > 0m)
        {
            value = parsed;
            return true;
        }

        error = LimitInvalid;
        return false;
    }
}
