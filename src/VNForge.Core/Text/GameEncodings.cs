using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Ponto único de registro do <see cref="CodePagesEncodingProvider"/> e acesso às
/// codificações do jogo. O registro acontece na primeira leitura de qualquer membro.
/// </summary>
public static class GameEncodings
{
    private const int ShiftJisCodePage = 932;
    private const int Windows1252CodePage = 1252;

    private static readonly Encoding ShiftJisEncoding;
    private static readonly Encoding Windows1252Encoding;

    static GameEncodings()
    {
        // O construtor estático roda uma única vez e o runtime o protege contra concorrência.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ShiftJisEncoding = Encoding.GetEncoding(ShiftJisCodePage);
        Windows1252Encoding = Encoding.GetEncoding(Windows1252CodePage);
    }

    /// <summary>Shift-JIS (página 932), a codificação do texto e dos nomes do YU-RIS.</summary>
    public static Encoding ShiftJis => ShiftJisEncoding;

    /// <summary>Windows-1252, usada pela estratégia <c>cp1252</c> para o português.</summary>
    public static Encoding Windows1252 => Windows1252Encoding;
}
