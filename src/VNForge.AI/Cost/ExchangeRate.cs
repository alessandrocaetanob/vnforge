namespace VNForge.AI.Cost;

/// <summary>
/// Cotação do dólar em reais, informada pela pessoa: o app não consulta a internet para ela. <see cref="InformedOn"/> é o dia em que a
/// pessoa a informou e fica vazio enquanto o valor é só o <see cref="Placeholder"/> que mantém as telas com números.
/// </summary>
public sealed record ExchangeRate
{
    public ExchangeRate(decimal brlPerUsd, DateOnly? informedOn = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(brlPerUsd, 0m);
        BrlPerUsd = brlPerUsd;
        InformedOn = informedOn;
    }

    /// <summary>Valor de exemplo para quem ainda não informou a cotação; as telas pedem para informar a de hoje.</summary>
    public static ExchangeRate Placeholder { get; } = new(5.40m);

    /// <summary>Reais por dólar.</summary>
    public decimal BrlPerUsd { get; }

    /// <summary>O dia em que a pessoa informou a cotação; <see langword="null"/> quando ela nunca informou.</summary>
    public DateOnly? InformedOn { get; }

    public bool IsInformed => InformedOn is not null;

    /// <summary>Converte sem arredondar: quem mostra o valor arredonda.</summary>
    public decimal ToBrl(decimal dollars) => dollars * BrlPerUsd;
}
