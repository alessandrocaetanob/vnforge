using System.Text;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// Os marcadores do falante que o script do sistema cadastra com <c>GOSUB es.CHAR.NAME.MARK.SET "「"</c> (no jogo de referência, o <c>yst00073</c>:
/// <c>「</c>, <c>『</c> e <c>（</c>). O jogo separa o nome da fala procurando esses textos na mensagem, então o delimitador que o injetor grava
/// (<see cref="SpeakerDelimiters"/>) precisa ser o mesmo que está cadastrado. Quando a codificação não grava o marcador japonês, este tipo troca
/// o argumento pelo substituto e deixa os outros como estão (<c>（</c> não tem substituto).
/// </summary>
internal static class SpeakerMarkers
{
    private const string GosubCommand = "GOSUB";

    private const string RoutineName = "es.CHAR.NAME.MARK.SET";

    private static readonly byte[] Routine = Encoding.ASCII.GetBytes(RoutineName);

    /// <summary>Verdadeiro se o script cadastra algum marcador que a codificação não grava e que tem substituto.</summary>
    public static bool NeedsPatch(YstbFile script, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(encoder);
        return Find(script, encoder).Any();
    }

    /// <summary>Acrescenta a <paramref name="replacements"/> os bytes novos de cada marcador a trocar, sem tocar nos outros argumentos.</summary>
    public static void Plan(YstbFile script, ITextEncoder encoder, Dictionary<int, ReadOnlyMemory<byte>> replacements)
    {
        foreach (var (argumentIndex, original, replacement) in Find(script, encoder))
        {
            var content = ControlMarkers.ToBytes(replacement, encoder);
            replacements[argumentIndex] = YstbTextInjector.BuildExpression($"{GosubCommand} {RoutineName} em {script.Source}", content, original);
        }
    }

    private static IEnumerable<(int ArgumentIndex, byte[] Original, string Replacement)> Find(YstbFile script, ITextEncoder encoder)
    {
        foreach (var instruction in script.Instructions)
        {
            if (instruction.CommandName != GosubCommand
                || instruction.Arguments.Count < 2
                || ReadLiteral(script, instruction.Arguments[0]) is not { } routine
                || !Ascii.EqualsIgnoreCase(routine, Routine))
            {
                continue;
            }

            for (var i = 1; i < instruction.Arguments.Count; i++)
            {
                var argument = instruction.Arguments[i];
                if (ReadLiteral(script, argument) is not { } text)
                {
                    break;
                }

                var marker = ControlMarkers.ToMarkers(text);
                var replacement = SpeakerDelimiters.Resolve(encoder, marker);
                if (!string.Equals(replacement, marker, StringComparison.Ordinal))
                {
                    yield return (instruction.FirstArgumentIndex + i, script.GetResource(argument).ToArray(), replacement);
                }
            }
        }
    }

    private static byte[]? ReadLiteral(YstbFile script, YstbArgument argument) =>
        argument.Size != 0 && ExpressionStrings.Read(script.GetResource(argument).Span, out var text) == ExpressionStringStatus.Literal ? text : null;
}
