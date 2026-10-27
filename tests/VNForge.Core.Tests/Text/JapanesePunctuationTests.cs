using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class JapanesePunctuationTests
{
    private static readonly ITextEncoder Cp1252 = TextEncoderRegistry.Default.Create("cp1252");
    private static readonly ITextEncoder Sjis = TextEncoderRegistry.Default.Create("sjis");

    [Theory]
    [InlineData("Oi。", "Oi.")]
    [InlineData("Sim、não", "Sim,não")]
    [InlineData("～Manhã～", "~Manhã~")]
    [InlineData("〜Manhã〜", "~Manhã~")]
    [InlineData("（nota）", "(nota)")]
    [InlineData("「Oi」", "\"Oi\"")]
    [InlineData("『Oi』", "\"Oi\"")]
    [InlineData("Nossa！？", "Nossa!?")]
    [InlineData("【Aviso】", "[Aviso]")]
    [InlineData("ＡＢＣ１２３", "ABC123")]
    [InlineData("um　dois", "um dois")]
    [InlineData("Ｏｉ，　ｔｕ：ｓｉｍ；", "Oi, tu:sim;")]
    public void Cada_item_da_tabela_vira_a_pontuacao_do_teclado(string entrada, string esperado)
    {
        Assert.Equal(esperado, JapanesePunctuation.ToKeyboard(entrada, Cp1252));
        Assert.Equal(esperado, JapanesePunctuation.FixUnencodable(entrada, Cp1252));
    }

    [Fact]
    public void Tres_pontos_medios_viram_reticencias_quando_a_estrategia_grava_e_tres_pontos_na_cp1252()
    {
        Assert.Equal("Hum...", JapanesePunctuation.FixUnencodable("Hum・・・", Cp1252));
        Assert.Equal("Hum…", JapanesePunctuation.ToKeyboard("Hum・・・", Sjis));
    }

    [Fact]
    public void Pontos_medios_no_fim_do_texto_trocam_um_a_um_e_tres_viram_reticencias()
    {
        Assert.Equal("Hum·", JapanesePunctuation.FixUnencodable("Hum・", Cp1252));
        Assert.Equal("Hum··", JapanesePunctuation.FixUnencodable("Hum・・", Cp1252));
        Assert.Equal("Hum...", JapanesePunctuation.FixUnencodable("Hum・・・", Cp1252));
        Assert.Equal("Hum... ·", JapanesePunctuation.FixUnencodable("Hum・・・ ・", Cp1252));
        Assert.Equal("Hum......", JapanesePunctuation.FixUnencodable("Hum・・・…", Cp1252));
        Assert.Equal("Hum..", JapanesePunctuation.FixUnencodable("Hum‥", Cp1252));
    }

    [Theory]
    [InlineData("Hum・")]
    [InlineData("Hum・・")]
    [InlineData("Hum・・・")]
    [InlineData("Hum・・・…")]
    [InlineData("Hum…・・・")]
    [InlineData("・・・・")]
    public void Reticencias_e_pontos_medios_sao_idempotentes_em_toda_estrategia(string texto)
    {
        foreach (var encoder in new ITextEncoder[] { Cp1252, Sjis, new OnlyAsciiEncoder() })
        {
            var corrigido = JapanesePunctuation.FixUnencodable(texto, encoder);
            Assert.Equal(corrigido, JapanesePunctuation.FixUnencodable(corrigido, encoder));
            var trocado = JapanesePunctuation.ToKeyboard(texto, encoder);
            Assert.Equal(trocado, JapanesePunctuation.ToKeyboard(trocado, encoder));
            Assert.Empty(encoder.FindUnencodable(JapanesePunctuation.FixUnencodable(texto, encoder).Replace("★", string.Empty)));
        }
    }

    [Fact]
    public void Reticencias_so_trocam_quando_a_estrategia_nao_as_grava_e_a_cp1252_nao_as_grava()
    {
        var semReticencias = new OnlyAsciiEncoder();

        Assert.Equal("Hum...", JapanesePunctuation.FixUnencodable("Hum…", semReticencias));
        Assert.Equal("Hum...", JapanesePunctuation.FixUnencodable("Hum…", Cp1252));
        Assert.Equal("Hum…", JapanesePunctuation.ToKeyboard("Hum…", Sjis));
        Assert.Equal("Hum...", JapanesePunctuation.FixUnencodable("Hum・・・", semReticencias));
    }

    [Fact]
    public void O_corretor_so_troca_o_que_a_estrategia_nao_grava()
    {
        // No Shift-JIS o ponto final japonês é gravável: o corretor deixa; a opção do projeto troca assim mesmo.
        Assert.Equal("Oi。", JapanesePunctuation.FixUnencodable("Oi。", Sjis));
        Assert.Equal("Oi.", JapanesePunctuation.ToKeyboard("Oi。", Sjis));
    }

    [Fact]
    public void Caractere_sem_substituto_gravavel_fica_como_esta()
    {
        // O encoder recusa até o '.', então não há troca gravável: o texto fica e o erro continua visível.
        var recusaTudo = new OnlyLettersEncoder();

        Assert.Equal("Oi。", JapanesePunctuation.FixUnencodable("Oi。", recusaTudo));
        Assert.Equal("Oi。", JapanesePunctuation.ToKeyboard("Oi。", recusaTudo));
        Assert.Equal("Oi★", JapanesePunctuation.FixUnencodable("Oi★", Cp1252));
    }

    [Fact]
    public void Ponto_medio_sozinho_vira_espaco_quando_a_estrategia_nao_grava_o_ponto_medio_latino()
    {
        Assert.Equal("John·Smith", JapanesePunctuation.FixUnencodable("John・Smith", Cp1252));
        Assert.Equal("John Smith", JapanesePunctuation.ToKeyboard("John・Smith", Sjis));
    }

    [Fact]
    public void Marcadores_de_controle_nao_sao_tocados()
    {
        Assert.Equal(@"Oi.\n{VAR:～}~\p", JapanesePunctuation.FixUnencodable(@"Oi。\n{VAR:～}～\p", Cp1252));
    }

    [Fact]
    public void Marcador_invalido_deixa_o_texto_inteiro_como_esta()
    {
        Assert.Equal("Oi。\\", JapanesePunctuation.FixUnencodable("Oi。\\", Cp1252));
    }

    [Fact]
    public void Barra_e_chaves_de_largura_dupla_nao_viram_marcador()
    {
        Assert.Equal("＼｛x｝", JapanesePunctuation.ToKeyboard("＼｛x｝", Cp1252));
    }

    [Theory]
    [InlineData("Oi。～（x）「y」・・・ＡＢ１　！")]
    [InlineData("Hum…")]
    [InlineData("a・b")]
    [InlineData(@"a\n。{VAR:x}")]
    [InlineData("sem nada")]
    public void A_troca_e_idempotente(string texto)
    {
        foreach (var encoder in new ITextEncoder[] { Cp1252, Sjis, new OnlyAsciiEncoder() })
        {
            var fixedOnce = JapanesePunctuation.FixUnencodable(texto, encoder);
            Assert.Equal(fixedOnce, JapanesePunctuation.FixUnencodable(fixedOnce, encoder));
            var keyboardOnce = JapanesePunctuation.ToKeyboard(texto, encoder);
            Assert.Equal(keyboardOnce, JapanesePunctuation.ToKeyboard(keyboardOnce, encoder));
        }
    }

    [Fact]
    public void Texto_sem_pontuacao_japonesa_volta_igual()
    {
        Assert.Equal("Ação, tudo bem?", JapanesePunctuation.ToKeyboard("Ação, tudo bem?", Cp1252));
    }

    [Theory]
    [InlineData(0xFF5E, "til japonês", "~")]
    [InlineData(0x3002, "ponto final japonês", ".")]
    [InlineData(0xFF08, "parêntese de largura dupla", "(")]
    [InlineData(0xFF21, "letra de largura dupla", "A")]
    [InlineData(0xFF11, "dígito de largura dupla", "1")]
    [InlineData(0x3000, "espaço de largura dupla", " ")]
    public void Nome_e_substituto_dos_caracteres_comuns(int codePoint, string nome, string teclado)
    {
        Assert.Equal(nome, JapanesePunctuation.NameOf(codePoint));
        Assert.Equal(teclado, JapanesePunctuation.KeyboardFor(codePoint));
    }

    [Fact]
    public void Caractere_fora_da_tabela_nao_tem_nome()
    {
        Assert.Null(JapanesePunctuation.NameOf('★'));
        Assert.Null(JapanesePunctuation.KeyboardFor('★'));
    }

    /// <summary>Grava só ASCII: não conhece nem as reticências.</summary>
    private sealed class OnlyAsciiEncoder : ITextEncoder
    {
        public string Id => "ascii";

        public byte[] Encode(string text) => System.Text.Encoding.ASCII.GetBytes(text);

        public IReadOnlyList<int> FindUnencodable(string text) => [.. text.Select((c, i) => (c, i)).Where(x => x.c > 0x7F).Select(x => x.i)];
    }

    /// <summary>Só letras: recusa até a pontuação ASCII.</summary>
    private sealed class OnlyLettersEncoder : ITextEncoder
    {
        public string Id => "letras";

        public byte[] Encode(string text) => System.Text.Encoding.ASCII.GetBytes(text);

        public IReadOnlyList<int> FindUnencodable(string text) => [.. text.Select((c, i) => (c, i)).Where(x => !char.IsLetter(x.c) || x.c > 0x7F).Select(x => x.i)];
    }
}
