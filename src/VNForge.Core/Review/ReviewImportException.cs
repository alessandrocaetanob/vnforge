using System.Text;

namespace VNForge.Core.Review;

/// <summary>Um problema do arquivo de revisão. <see cref="Line"/> é 0 quando o problema é do arquivo todo.</summary>
public sealed record ReviewImportError(int Line, string Message);

/// <summary>O arquivo de revisão não pôde ser lido ou tem linhas inválidas. Nada foi alterado no projeto.</summary>
public sealed class ReviewImportException : VNForgeException
{
    private const int MaxListed = 20;

    public ReviewImportException(string message)
        : this([new ReviewImportError(0, message)])
    {
    }

    public ReviewImportException(IReadOnlyList<ReviewImportError> errors)
        : base(Describe(errors))
    {
        Errors = errors;
    }

    /// <summary>Todos os problemas encontrados, na ordem do arquivo.</summary>
    public IReadOnlyList<ReviewImportError> Errors { get; }

    private static string Describe(IReadOnlyList<ReviewImportError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors is [{ Line: 0 } only])
        {
            return only.Message;
        }

        var sb = new StringBuilder("Arquivo de revisão recusado, nada foi importado:");
        foreach (var error in errors.Take(MaxListed))
        {
            sb.Append("\n  ").Append(error.Line > 0 ? $"linha {error.Line}: " : string.Empty).Append(error.Message);
        }

        if (errors.Count > MaxListed)
        {
            sb.Append($"\n  ... e mais {errors.Count - MaxListed} erro(s).");
        }

        return sb.ToString();
    }
}
