using System.Globalization;
using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// Tira as <see cref="TextUnit"/> de um script YSTB já lido. Segue o VNTextPatch (<c>YurisScenarioScript.cs:121-229</c>) e as
/// notas do doc 06; o que cada tipo de unidade usa:
/// <list type="bullet">
/// <item><description><c>WORD</c> (bytes crus): <c>WORD</c> em instruções seguidas formam uma mensagem (<see cref="TextUnitKind.Narration"/>).
/// Se a mensagem tem a forma <c>nome「fala」</c> ou <c>nome（fala）</c>, o nome vai para <see cref="TextUnit.Speaker"/> e o tipo
/// é <see cref="TextUnitKind.Dialogue"/>; uma fala inteira entre <c>「」</c> sem nome também é <see cref="TextUnitKind.Dialogue"/>,
/// com <see cref="TextUnit.Speaker"/> nulo. Qualquer outra instrução, inclusive <c>RETURNCODE</c> e <c>_</c>, termina a mensagem.</description></item>
/// <item><description><c>GOSUB</c> de <c>ES.SEL.SET</c> (<see cref="TextUnitKind.Choice"/>) e <c>ES.CHAR.NAME</c>
/// (<see cref="TextUnitKind.Name"/>), sem diferenciar maiúsculas: cada argumento depois do nome da rotina que for string literal e não
/// vazio, até o primeiro que não for.</description></item>
/// <item><description>Qualquer outro argumento de expressão que seja só uma string literal com japonês (<see cref="TextUnitKind.Other"/>),
/// exceto em <c>WORD</c>, <c>_</c>, <c>RETURNCODE</c>, <c>CG</c>, <c>GOSUB</c>, nos comandos de fluxo <c>IF</c>, <c>ELSE</c> e <c>LOOP</c> e em <c>FONT</c>
/// (o argumento é o nome de uma fonte, não texto).</description></item>
/// </list>
/// Expressões que não são só uma string literal (concatenações, variáveis) não são extraídas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Id.</b> <see cref="ScriptFile.UnitId"/> com a posição <c>índice da instrução * 256 + índice do argumento</c>, da primeira
/// instrução com texto da unidade: o índice não muda quando a heurística junta <c>WORD</c> de outro jeito, e o argumento desempata
/// as escolhas de um mesmo <c>GOSUB</c>. O maior script aceito pelo leitor (32 MiB) tem menos de 4,2 milhões de instruções, então
/// a conta cabe em um <see cref="int"/>.
/// </para>
/// <para>
/// <b>Locator</b> (versão 1): <c>{ "version": 1, "command": "WORD", "parts": [ { "instruction": 12, "argument": 0, "size": 14,
/// "offset": 120 } ], "speaker": { "name": "美咲", "open": "「", "close": "」" } }</c>. <c>parts</c> lista, na ordem do arquivo, os
/// argumentos que formam o <see cref="TextUnit.Source"/> (um por <c>WORD</c> não vazio) com o <c>size</c> e o <c>offset</c> originais.
/// <c>speaker</c> só existe quando há falante; o original é <c>name + open + source + close</c>. Em <c>GOSUB</c> há ainda
/// <c>"routine"</c> com o nome da rotina. O texto de cada parte é o do <c>ControlMarkers.ToMarkers</c> sobre os bytes (em
/// strings de expressão, depois de tirar as aspas e desfazer <c>\\</c>, <c>\n</c> e <c>\t</c>).
/// </para>
/// </remarks>
public static class YstbTextExtractor
{
    /// <summary>Versão do formato do locator gravado nas unidades.</summary>
    public const int LocatorVersion = 1;

    /// <summary>Nome da rotina do E-ris que monta uma escolha.</summary>
    public const string ChoiceRoutine = "ES.SEL.SET";

    /// <summary>Nome da rotina do E-ris que define o nome de um personagem.</summary>
    public const string NameRoutine = "ES.CHAR.NAME";

    private const int ArgumentsPerInstruction = 256;

    /// <summary>
    /// Comandos que o <see cref="TextUnitKind.Other"/> não olha, se o YSCM os tem. <c>IF</c>, <c>ELSE</c> e <c>LOOP</c> têm argumentos
    /// cujo <c>size</c> e <c>offset</c> não apontam para recursos; <c>FONT</c> recebe o nome de uma fonte do Windows, que traduzido
    /// pediria uma fonte que não existe (#288).
    /// </summary>
    private static readonly string[] SkippedCommands = ["IF", "ELSE", "LOOP", "FONT"];

    /// <summary>Tira as unidades de texto de <paramref name="script"/>, na ordem do arquivo.</summary>
    /// <param name="file">O mesmo script, para a chave dos ids.</param>
    /// <param name="script">Script lido do <paramref name="file"/>.</param>
    /// <param name="commands">YSCM do mesmo jogo, de onde vêm os opcodes.</param>
    /// <exception cref="VNForge.Core.InvalidFileException">O YSCM não tem algum dos comandos <c>WORD</c>, <c>_</c>, <c>RETURNCODE</c>, <c>GOSUB</c> e <c>CG</c>.</exception>
    public static ScriptExtraction Extract(ScriptFile file, YstbFile script, YscmFile commands)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(commands);

        return new Run(file, script, commands).Execute();
    }

    private readonly record struct Part(int Instruction, int Argument, uint Size, uint Offset);

    private sealed class Run
    {
        private readonly ScriptFile _file;
        private readonly YstbFile _script;
        private readonly YuRisOpcodes _opcodes;
        private readonly HashSet<byte> _skippedOpcodes;
        private readonly List<TextUnit> _units = [];
        private readonly List<string> _warnings = [];
        private readonly List<YstbInstruction> _words = [];

        public Run(ScriptFile file, YstbFile script, YscmFile commands)
        {
            _file = file;
            _script = script;
            _opcodes = commands.GetOpcodes();
            _skippedOpcodes = [];
            foreach (var name in SkippedCommands)
            {
                if (commands.TryGetOpcode(name, out var opcode))
                {
                    _skippedOpcodes.Add(opcode);
                }
            }
        }

        public ScriptExtraction Execute()
        {
            foreach (var instruction in _script.Instructions)
            {
                if (instruction.Opcode == _opcodes.Word)
                {
                    CollectWord(instruction);
                    continue;
                }

                FlushMessage();
                if (instruction.Opcode == _opcodes.Gosub)
                {
                    ExtractGosub(instruction);
                }
                else if (!IsSkipped(instruction.Opcode))
                {
                    ExtractOther(instruction);
                }
            }

            FlushMessage();
            return new ScriptExtraction(_units) { Warnings = _warnings };
        }

        private bool IsSkipped(byte opcode) =>
            opcode == _opcodes.Expression || opcode == _opcodes.ReturnCode || opcode == _opcodes.Cg || _skippedOpcodes.Contains(opcode);

        private void CollectWord(YstbInstruction instruction)
        {
            if (instruction.Arguments.Count != 1)
            {
                FlushMessage();
                _warnings.Add($"A instrução {Number(instruction.Index)} (WORD) tem {Number(instruction.Arguments.Count)} argumentos em vez de 1; ela foi pulada.");
                return;
            }

            _words.Add(instruction);
        }

        private void FlushMessage()
        {
            if (_words.Count == 0)
            {
                return;
            }

            var parts = new List<Part>();
            var markers = new StringBuilder();
            foreach (var instruction in _words)
            {
                var argument = instruction.Arguments[0];
                if (argument.Size == 0)
                {
                    continue;
                }

                markers.Append(ControlMarkers.ToMarkers(_script.GetResource(argument).Span));
                parts.Add(new Part(instruction.Index, 0, argument.Size, argument.Offset));
            }

            _words.Clear();
            if (parts.Count == 0)
            {
                return;
            }

            var text = markers.ToString();
            TextUnit unit;
            if (SpeakerSplitter.TrySplit(text, out var split))
            {
                unit = NewUnit(parts[0], TextUnitKind.Dialogue, split.Name, split.Body, BuildLocator("WORD", null, parts, split));
            }
            else
            {
                var kind = SpeakerSplitter.IsQuoted(text) ? TextUnitKind.Dialogue : TextUnitKind.Narration;
                unit = NewUnit(parts[0], kind, null, text, BuildLocator("WORD", null, parts, null));
            }

            _units.Add(unit);
        }

        private void ExtractGosub(YstbInstruction instruction)
        {
            if (instruction.Arguments.Count < 2
                || ReadLiteral(instruction, 0) is not { } routine
                || !TryGetRoutineKind(routine, out var kind, out var routineName))
            {
                return;
            }

            for (var i = 1; i < instruction.Arguments.Count; i++)
            {
                if (ReadLiteral(instruction, i) is not { Length: > 0 } text)
                {
                    break;
                }

                var part = new Part(instruction.Index, i, instruction.Arguments[i].Size, instruction.Arguments[i].Offset);
                _units.Add(NewUnit(part, kind, null, ControlMarkers.ToMarkers(text), BuildLocator("GOSUB", routineName, [part], null)));
            }
        }

        private void ExtractOther(YstbInstruction instruction)
        {
            for (var i = 0; i < instruction.Arguments.Count; i++)
            {
                if (ReadLiteral(instruction, i) is not { Length: > 0 } text)
                {
                    continue;
                }

                var markers = ControlMarkers.ToMarkers(text);
                if (ContainsJapanese(markers))
                {
                    var part = new Part(instruction.Index, i, instruction.Arguments[i].Size, instruction.Arguments[i].Offset);
                    _units.Add(NewUnit(part, TextUnitKind.Other, null, markers, BuildLocator(instruction.CommandName, null, [part], null)));
                }
            }
        }

        /// <summary>Lê o argumento como string literal; nulo se não for uma (e, com escape inválido, avisa).</summary>
        private byte[]? ReadLiteral(YstbInstruction instruction, int argumentIndex)
        {
            var argument = instruction.Arguments[argumentIndex];
            if (argument.Size == 0)
            {
                return null;
            }

            switch (ExpressionStrings.Read(_script.GetResource(argument).Span, out var text))
            {
                case ExpressionStringStatus.Literal:
                    return text;
                case ExpressionStringStatus.InvalidEscape:
                    _warnings.Add(
                        $"A instrução {Number(instruction.Index)} ({instruction.CommandName}), argumento {Number(argumentIndex)}, tem uma string com escape que o YU-RIS não define (só \\\\, \\n e \\t); ela não foi extraída.");
                    return null;
                default:
                    return null;
            }
        }

        private static bool TryGetRoutineKind(byte[] routine, out TextUnitKind kind, out string name)
        {
            if (Ascii.EqualsIgnoreCase(routine, "ES.SEL.SET"u8))
            {
                (kind, name) = (TextUnitKind.Choice, ChoiceRoutine);
                return true;
            }

            if (Ascii.EqualsIgnoreCase(routine, "ES.CHAR.NAME"u8))
            {
                (kind, name) = (TextUnitKind.Name, NameRoutine);
                return true;
            }

            (kind, name) = (default, string.Empty);
            return false;
        }

        private TextUnit NewUnit(Part first, TextUnitKind kind, string? speaker, string source, JsonElement locator) =>
            new(_file.UnitId(checked((first.Instruction * ArgumentsPerInstruction) + first.Argument)), kind, speaker, source, null, TranslationStatus.Untranslated)
            {
                Locator = locator,
            };

        private static JsonElement BuildLocator(string command, string? routine, List<Part> parts, SpeakerSplit? split)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", LocatorVersion);
                writer.WriteString("command", command);
                if (routine is not null)
                {
                    writer.WriteString("routine", routine);
                }

                writer.WriteStartArray("parts");
                foreach (var part in parts)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("instruction", part.Instruction);
                    writer.WriteNumber("argument", part.Argument);
                    writer.WriteNumber("size", part.Size);
                    writer.WriteNumber("offset", part.Offset);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                if (split is { } s)
                {
                    writer.WriteStartObject("speaker");
                    writer.WriteString("name", s.Name);
                    writer.WriteString("open", s.Open);
                    writer.WriteString("close", s.Close);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            using var document = JsonDocument.Parse(stream.ToArray());
            return document.RootElement.Clone();
        }

        /// <summary>Hiragana, katakana (inclusive meia largura), ideogramas CJK e a extensão A.</summary>
        private static bool ContainsJapanese(string text)
        {
            return text.Any(c => c is (>= '぀' and <= 'ヿ') or (>= '㐀' and <= '䶿') or (>= '一' and <= '鿿') or (>= 'ｦ' and <= 'ﾟ'));
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
