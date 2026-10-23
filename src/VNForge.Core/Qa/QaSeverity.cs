namespace VNForge.Core.Qa;

/// <summary>Gravidade de um problema do QA. A ordem importa: <see cref="Error"/> é maior que <see cref="Warning"/>.</summary>
public enum QaSeverity
{
    /// <summary>Aviso: o revisor pode corrigir ou marcar como "ok" na linha (<see cref="QaFlags"/>); o build para nele, a menos que rode com <c>--allow-warnings</c>.</summary>
    Warning = 0,

    /// <summary>Erro: bloqueia o build sempre, e "marcar como ok" não o silencia.</summary>
    Error = 1,
}
