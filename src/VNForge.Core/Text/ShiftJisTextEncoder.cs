namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>sjis</c>: converte direto para Shift-JIS (página 932), sem tratar acentos do português.
/// Caractere fora da página 932 não é substituído nem descartado: <see cref="CodePageTextEncoder.Encode"/> lança exceção.
/// </summary>
public sealed class ShiftJisTextEncoder() : CodePageTextEncoder(GameEncodings.ShiftJis)
{
    public override string Id => "sjis";

    protected override string DisplayName => "o Shift-JIS não representa";
}
