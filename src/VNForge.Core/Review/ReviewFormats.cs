namespace VNForge.Core.Review;

/// <summary>Os formatos de revisão conhecidos. Um formato novo entra na lista <see cref="All"/>.</summary>
public static class ReviewFormats
{
    public static IReviewFormat Csv { get; } = new CsvReviewFormat();

    public static IReadOnlyList<IReviewFormat> All { get; } = [Csv];

    /// <summary>Acha o formato pelo nome (sem diferenciar maiúsculas); <c>null</c> se não existe.</summary>
    public static IReviewFormat? Find(string name) =>
        All.FirstOrDefault(f => string.Equals(f.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Acha o formato pela extensão do arquivo; <c>null</c> se nenhum a usa.</summary>
    public static IReviewFormat? FromPath(string path) =>
        All.FirstOrDefault(f => string.Equals(f.Extension, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));
}
