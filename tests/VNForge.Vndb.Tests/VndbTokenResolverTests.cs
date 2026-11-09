using VNForge.Core.Vndb;

namespace VNForge.Vndb.Tests;

public class VndbTokenResolverTests
{
    [Fact]
    public void Le_a_variavel_VNDB_TOKEN_e_tira_os_espacos()
    {
        string? asked = null;

        var token = VndbTokenResolver.Resolve(name =>
        {
            asked = name;
            return "  abcd-efgh  ";
        });

        Assert.Equal("VNDB_TOKEN", asked);
        Assert.Equal("abcd-efgh", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Variavel_ausente_ou_vazia_nao_e_token(string? value)
    {
        Assert.Null(VndbTokenResolver.Resolve(_ => value));
    }

    [Fact]
    public void A_variavel_de_ambiente_vence_o_cofre()
    {
        var store = new MemoryStore();
        store.Save("do-cofre");

        var resolved = VndbTokenResolver.Resolve(store, _ => " do-ambiente ");

        Assert.Equal("do-ambiente", resolved.Token);
        Assert.Equal(VndbTokenSource.Environment, resolved.Source);
    }

    [Fact]
    public void Sem_variavel_vale_o_cofre_e_sem_nenhum_dos_dois_nao_ha_token()
    {
        var store = new MemoryStore();
        Assert.Equal(VndbTokenSource.None, VndbTokenResolver.Resolve(store, _ => null).Source);

        store.Save("do-cofre");

        var resolved = VndbTokenResolver.Resolve(store, _ => "");
        Assert.Equal("do-cofre", resolved.Token);
        Assert.Equal(VndbTokenSource.Vault, resolved.Source);
    }

    [Fact]
    public void O_token_resolvido_nunca_aparece_no_texto_do_registro()
    {
        var resolved = new ResolvedVndbToken("segredo-do-token", VndbTokenSource.Vault);

        Assert.DoesNotContain("segredo-do-token", resolved.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_do_cofre_nao_contem_o_token_em_texto_puro()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O cofre DPAPI só existe no Windows.");
        using var dir = new TempDir();
        var store = new DpapiVndbTokenStore(dir.Path);
        store.Save("abcd-segredo-token");

        Assert.DoesNotContain("abcd-segredo-token", File.ReadAllText(store.Path, System.Text.Encoding.Latin1));
        Assert.Equal("abcd-segredo-token", store.Load());
        Assert.True(store.Delete());
        Assert.False(store.Delete());
        Assert.Null(store.Load());
    }

    [Fact]
    public void Fora_do_windows_o_cofre_manda_usar_a_variavel_de_ambiente()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Só vale fora do Windows.");
        using var dir = new TempDir();

        var erro = Assert.Throws<VndbException>(() => new DpapiVndbTokenStore(dir.Path).Save("x"));

        Assert.Contains("VNDB_TOKEN", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_do_Windows_copiado_para_outro_sistema_e_ignorado_com_aviso_e_a_variavel_vale()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        File.WriteAllBytes(Path.Combine(dir.Path, "vndb-token.dat"), [1, 2, 3, 4]);
        var store = new DpapiVndbTokenStore(dir.Path, windows: false);

        var semVariavel = VndbTokenResolver.Resolve(store, _ => null);
        var comVariavel = VndbTokenResolver.Resolve(store, _ => "do-ambiente");

        Assert.Null(store.Load());
        Assert.Equal(VndbTokenSource.None, semVariavel.Source);
        Assert.Contains("ignorado", semVariavel.Notice, StringComparison.Ordinal);
        Assert.Contains("VNDB_TOKEN", semVariavel.Notice, StringComparison.Ordinal);
        Assert.Equal("do-ambiente", comVariavel.Token);
        Assert.Equal(VndbTokenSource.Environment, comVariavel.Source);
        Assert.True(File.Exists(store.Path));
    }

    [Fact]
    public void Sem_arquivo_ou_no_Windows_nao_ha_aviso()
    {
        using var dir = new TempDir();

        Assert.Null(new DpapiVndbTokenStore(dir.Path, windows: false).Notice);
        Assert.Null(new DpapiVndbTokenStore(dir.Path, windows: true).Notice);
        Assert.Null(VndbTokenResolver.Resolve(new DpapiVndbTokenStore(dir.Path, windows: false), _ => null).Notice);
    }

    private sealed class MemoryStore : IVndbTokenStore
    {
        private string? _token;

        public string? Load() => _token;

        public void Save(string token) => _token = token;

        public bool Delete()
        {
            var had = _token is not null;
            _token = null;
            return had;
        }
    }
}
