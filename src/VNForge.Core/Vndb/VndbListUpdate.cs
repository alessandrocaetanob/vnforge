namespace VNForge.Core.Vndb;

/// <summary>O que fazer com um campo da lista no <c>PATCH /ulist</c>: deixar como está, gravar um valor ou apagar o que havia.</summary>
public enum VndbChangeKind
{
    Keep,
    Set,
    Clear,
}

/// <summary>
/// Uma mudança em um campo da lista. O padrão (<c>default</c>) é <see cref="VndbChangeKind.Keep"/>: o campo não vai no pedido e o VNDB não o mexe.
/// Um valor converte direto em <see cref="VndbChangeKind.Set"/>; <see cref="Clear"/> manda <c>null</c> e apaga o campo.
/// </summary>
public readonly record struct VndbChange<T>(VndbChangeKind Kind, T? Value)
{
    public static VndbChange<T> Clear => new(VndbChangeKind.Clear, default);

    public static VndbChange<T> Set(T value) => new(VndbChangeKind.Set, value);

    public static implicit operator VndbChange<T>(T value) => Set(value);
}

/// <summary>
/// As mudanças de uma entrada da lista. <see cref="LabelsSet"/> e <see cref="LabelsUnset"/> acrescentam e tiram rótulos sem mexer nos outros; o VNDB também aceita
/// trocar todos de uma vez, mas aqui isso não existe, para um erro não apagar os rótulos da pessoa. O VNDB adiciona o jogo à lista se ele ainda não estiver nela.
/// </summary>
public sealed record VndbListUpdate
{
    /// <summary>De 10 a 100.</summary>
    public VndbChange<int> Vote { get; init; }

    public VndbChange<string> Notes { get; init; }

    public VndbChange<DateOnly> Started { get; init; }

    public VndbChange<DateOnly> Finished { get; init; }

    public IReadOnlyList<int> LabelsSet { get; init; } = [];

    public IReadOnlyList<int> LabelsUnset { get; init; } = [];
}
