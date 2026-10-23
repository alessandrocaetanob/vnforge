using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// O YSTB (<c>.ybn</c>) como <see cref="IScriptFormat"/>. A extração lê o script com o <see cref="YuRisContext"/> e devolve as unidades
/// de <see cref="YstbTextExtractor"/>. A reconstrução ainda não está ligada: o escritor de YSTB e o injetor de texto da E2.7 (#38)
/// entram à parte, e <see cref="Rebuild"/> lança <see cref="NotSupportedException"/> até a E8.1 (<c>vnf build</c>) usá-los.
/// </summary>
public sealed class YstbScriptFormat : IScriptFormat
{
    public string Id => "ystb";

    public IReadOnlyList<string> FileExtensions { get; } = [".ybn"];

    /// <inheritdoc />
    public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
    {
        ArgumentNullException.ThrowIfNull(file);
        var context = Require(ctx);

        try
        {
            var script = YstbReader.Read(file.Content, file.Name, context.Commands);
            return YstbTextExtractor.Extract(file, script, context.Commands);
        }
        catch (VNForgeException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Sempre: a reconstrução dos scripts ainda não está ligada ao formato.</exception>
    public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(encoder);
        _ = Require(ctx);
        throw new NotSupportedException("A reconstrução de scripts YSTB ainda não está ligada ao IScriptFormat; ela chega com o vnf build (E8.1).");
    }

    private static YuRisContext Require(EngineContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx as YuRisContext
            ?? throw new ArgumentException($"O contexto é da engine '{ctx.EngineId}', não do YU-RIS.", nameof(ctx));
    }
}
