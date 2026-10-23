using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// O YSTB (<c>.ybn</c>) como <see cref="IScriptFormat"/>. A extração lê o script com o <see cref="YuRisContext"/> e devolve as unidades
/// de <see cref="YstbTextExtractor"/>. A reconstrução lê o script de novo e entrega as unidades ao
/// <see cref="YstbTextInjector"/>, que grava só os argumentos com tradução pelo <see cref="YstbWriter"/>.
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
    public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(encoder);
        var context = Require(ctx);
        file.ValidateUnits(units);

        try
        {
            var script = YstbReader.Read(file.Content, file.Name, context.Commands);
            return YstbTextInjector.Inject(file, script, units, encoder);
        }
        catch (VNForgeException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static YuRisContext Require(EngineContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx as YuRisContext
            ?? throw new ArgumentException($"O contexto é da engine '{ctx.EngineId}', não do YU-RIS.", nameof(ctx));
    }
}
