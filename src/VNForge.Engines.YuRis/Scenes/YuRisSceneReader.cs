using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Scenes;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Yslb;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Scenes;

/// <summary>
/// Lê as ligações entre scripts do YU-RIS (docs/06, "Saltos entre scripts"). Só vale o destino que é um nome de rótulo literal que o
/// <c>ysl.ybn</c> declara em um único script do jogo:
/// <list type="bullet">
/// <item><description><c>GO</c> com o rótulo no primeiro argumento é um <see cref="SceneEdgeKind.Jump"/>.</description></item>
/// <item><description><c>GOSUB</c> com o rótulo no primeiro argumento é um <see cref="SceneEdgeKind.Call"/>.</description></item>
/// <item><description>
/// <c>GOSUB ES.SEL.GO</c> (rotina do E-ris) recebe um rótulo por escolha, na ordem do <c>ES.SEL.SET</c> que vem em seguida: cada
/// rótulo é uma aresta <see cref="SceneEdgeKind.Choice"/>, com o id do texto da escolha quando a extração tem esse texto.
/// </description></item>
/// </list>
/// Destino calculado (variável, concatenação), rótulo que não existe ou que existe em dois scripts, e script que não é do jogo não geram aresta.
/// Um script que o leitor não consegue abrir é pulado.
/// </summary>
internal static class YuRisSceneReader
{
    private const string JumpCommand = "GO";
    private const string ChoiceTargetsRoutine = "ES.SEL.GO";
    private const int ArgumentsPerInstruction = 256;

    /// <summary>Quantos rótulos o <c>ES.SEL.GO</c> do E-ris copia (o <c>LOOP 8</c> da rotina); os demais argumentos são ignorados.</summary>
    private const int MaxChoiceTargets = 8;

    public static IReadOnlyList<SceneEdge> Read(YuRisContext context, IReadOnlyList<UnpackedScript> scripts)
    {
        if (context.Labels is not { } labels
            || !context.Commands.TryGetOpcode(YuRisOpcodes.GosubName, out var gosub))
        {
            return [];
        }

        var hasJump = context.Commands.TryGetOpcode(JumpCommand, out var jump);
        var sceneKeys = scripts.Select(s => new ScriptFile(s.Name, ReadOnlyMemory<byte>.Empty).Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = new List<SceneEdge>();
        foreach (var script in scripts)
        {
            YstbFile ystb;
            ScriptFile file;
            try
            {
                file = new ScriptFile(script.Name, File.ReadAllBytes(script.Path));
                ystb = YstbReader.Read(file.Content, file.Name, context.Commands);
            }
            catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            new ScriptScan(file, ystb, labels, sceneKeys, gosub, hasJump ? jump : null, edges).Scan();
        }

        return edges;
    }

    private sealed class ScriptScan(
        ScriptFile file,
        YstbFile ystb,
        YslbFile labels,
        HashSet<string> sceneKeys,
        byte gosub,
        byte? jump,
        List<SceneEdge> edges)
    {
        public void Scan()
        {
            for (var i = 0; i < ystb.Instructions.Count; i++)
            {
                var instruction = ystb.Instructions[i];
                if (instruction.Opcode == jump)
                {
                    AddEdge(instruction, SceneEdgeKind.Jump);
                }
                else if (instruction.Opcode == gosub)
                {
                    ScanGosub(i, instruction);
                }
            }
        }

        private void ScanGosub(int index, YstbInstruction instruction)
        {
            if (Literal(instruction, 0) is not { Length: > 0 } routine)
            {
                return;
            }

            AddEdge(instruction, SceneEdgeKind.Call);
            if (string.Equals(routine, ChoiceTargetsRoutine, StringComparison.OrdinalIgnoreCase))
            {
                ScanChoices(index, instruction);
            }
        }

        private void ScanChoices(int index, YstbInstruction instruction)
        {
            var textUnits = TextUnitsOfNextChoiceSet(index);
            for (var i = 1; i < instruction.Arguments.Count && i <= MaxChoiceTargets; i++)
            {
                if (Literal(instruction, i) is not { Length: > 0 } label || Resolve(label) is not { } target)
                {
                    continue;
                }

                var unitId = textUnits is { } set && i <= set.Count ? file.UnitId((set.Instruction * ArgumentsPerInstruction) + i) : null;
                edges.Add(new SceneEdge(file.Key, target, SceneEdgeKind.Choice, label) { ChoiceUnitId = unitId });
            }
        }

        /// <summary>
        /// O próximo <c>ES.SEL.SET</c> depois do <c>ES.SEL.GO</c> e antes de outro <c>ES.SEL.GO</c>, com quantos textos a extração
        /// tira dele: os literais não vazios do começo, como em <c>YstbTextExtractor</c>.
        /// </summary>
        private (int Instruction, int Count)? TextUnitsOfNextChoiceSet(int goIndex)
        {
            for (var i = goIndex + 1; i < ystb.Instructions.Count; i++)
            {
                var candidate = ystb.Instructions[i];
                if (candidate.Opcode != gosub || Literal(candidate, 0) is not { } routine)
                {
                    continue;
                }

                if (string.Equals(routine, ChoiceTargetsRoutine, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (string.Equals(routine, YstbTextExtractor.ChoiceRoutine, StringComparison.OrdinalIgnoreCase))
                {
                    var count = 0;
                    while (count + 1 < candidate.Arguments.Count && Literal(candidate, count + 1) is { Length: > 0 })
                    {
                        count++;
                    }

                    return (i, count);
                }
            }

            return null;
        }

        private void AddEdge(YstbInstruction instruction, SceneEdgeKind kind)
        {
            if (Literal(instruction, 0) is { Length: > 0 } label && Resolve(label) is { } target)
            {
                edges.Add(new SceneEdge(file.Key, target, kind, label));
            }
        }

        private string? Resolve(string label)
        {
            if (labels.Find(label) is not { } found)
            {
                return null;
            }

            var key = Path.GetFileNameWithoutExtension(found.ScriptFileName);
            return sceneKeys.Contains(key) ? key : null;
        }

        private string? Literal(YstbInstruction instruction, int argumentIndex)
        {
            if (argumentIndex >= instruction.Arguments.Count)
            {
                return null;
            }

            var argument = instruction.Arguments[argumentIndex];
            if (argument.Size == 0)
            {
                return string.Empty;
            }

            if (argument.Offset + (long)argument.Size > ystb.Resources.Length
                || ExpressionStrings.Read(ystb.GetResource(argument).Span, out var text) != ExpressionStringStatus.Literal)
            {
                return null;
            }

            return GameEncodings.ShiftJis.GetString(text);
        }
    }
}
