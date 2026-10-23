using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystl;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Dados do jogo que o YU-RIS carrega uma vez: a tabela de comandos do <c>ysc.ybn</c> (de onde vêm os opcodes) e, quando existe,
/// a lista de scripts do <c>yst_list.ybn</c>. Imutável depois de criado; <see cref="YuRisEngine"/> cria a partir de uma pasta
/// descompactada (<see cref="YuRisEngine.Open"/>).
/// </summary>
public sealed class YuRisContext : EngineContext
{
    /// <summary>Cria o contexto.</summary>
    /// <param name="commands">O <c>ysc.ybn</c> do jogo.</param>
    /// <param name="scriptList">O <c>yst_list.ybn</c>, ou nulo se o jogo não tem ou não foi lido.</param>
    public YuRisContext(YscmFile commands, YstlFile? scriptList = null)
        : base(YuRisEngine.EngineId)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Commands = commands;
        ScriptList = scriptList;
    }

    /// <summary>Comandos do <c>ysc.ybn</c>, na ordem do id.</summary>
    public YscmFile Commands { get; }

    /// <summary>Lista de scripts do <c>yst_list.ybn</c>, ou nulo.</summary>
    public YstlFile? ScriptList { get; }
}
