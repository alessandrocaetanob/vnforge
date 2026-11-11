using VNForge.App.ViewModels;
using VNForge.App.Services;
using VNForge.Core.Glossary;

namespace VNForge.App.Tests;

/// <summary>Estados de linha e de rótulo que as telas mostram e que não têm outro teste de ViewModel: o cartão de personagem da ficha e o papel do VNDB no inspetor.</summary>
public class ViewModelStatesTests
{
    private static CharacterTile Tile(TileKind kind, string? original = null, string caption = "", string line = "", SheetPill? role = null, SheetPill? status = null) =>
        new("c1", kind, "Kenta Aizawa", original, null, role, status, line, caption, 0);

    [Theory]
    [InlineData(TileKind.Portrait, true, false, false, false)]
    [InlineData(TileKind.ImageBlurred, true, true, false, false)]
    [InlineData(TileKind.NoImage, false, false, true, false)]
    [InlineData(TileKind.ImageHidden, false, false, false, true)]
    public void O_cartao_de_personagem_decide_o_retrato_pelo_tipo(TileKind kind, bool portrait, bool blurred, bool placeholder, bool imageHidden)
    {
        var tile = Tile(kind);

        Assert.Equal((portrait, blurred, placeholder, imageHidden), (tile.ShowsPortrait, tile.ShowsImageBlurred, tile.ShowsPlaceholder, tile.ShowsImageHidden));
    }

    [Fact]
    public void O_cartao_mostra_o_original_ou_o_aviso_e_a_linha_e_os_selos_so_quando_existem()
    {
        var plain = Tile(TileKind.NoImage, original: null, caption: "Sem imagem no VNDB");
        var full = Tile(TileKind.Portrait, original: "相沢 健太", line: "Cabelo curto", role: new SheetPill("Protagonista", SheetTone.Translated), status: new SheetPill("Aprovado", SheetTone.Translated));

        Assert.Equal((false, true, false, false, false), (plain.HasOriginal, plain.HasCaption, plain.HasLine, plain.HasPills, plain.HasStatus));
        Assert.Equal((true, false, true, true, true), (full.HasOriginal, full.HasCaption, full.HasLine, full.HasPills, full.HasStatus));
        Assert.Equal("Cartão de Kenta Aizawa", full.AutomationName);
        Assert.Equal("Personagem escondido por spoiler", Tile(TileKind.Hidden).AutomationName);
    }

    [Fact]
    public void Os_controles_da_ficha_se_leem_pelo_rotulo()
    {
        Assert.Equal("Leve", new SpoilerOption(1, "Leve").ToString());
        Assert.Equal("Só IA", new SheetFilterOption(SheetReviewFilter.Machine, "Só IA").ToString());
    }

    [Fact]
    public void A_Conferencia_pede_o_teste_no_jogo_e_so_aplica_um_ajuste_com_cartao()
    {
        var vm = new ConferenceViewModel();
        var requested = 0;
        vm.TestRequested += (_, _) => requested++;

        vm.TestCommand.Execute(null);

        Assert.Equal(1, requested);
        Assert.False(vm.ApplySettingCommand.CanExecute(null));
    }

    [Fact]
    public void O_tom_da_cena_que_cita_um_personagem_e_so_apoio()
    {
        Assert.True(new BibleToneChip("cômico leve", IsSupport: false).IsMain);
        Assert.False(new BibleToneChip("seco na fala de Misaki", IsSupport: true).IsMain);
    }

    [Theory]
    [InlineData("cp1252", "ajuste no jogo")]
    [InlineData("sjis", "forma antiga")]
    [InlineData("fold", "sem acentos")]
    [InlineData("remap", "fonte modificada")]
    [InlineData("outra", "outra")]
    public void A_estrategia_de_acentos_tem_o_nome_curto_da_barra_de_estado(string strategy, string expected) =>
        Assert.Equal(expected, ProjectLabels.EncodingShort(strategy));

    [Theory]
    [InlineData("main", CharacterGender.Male, "Protagonista")]
    [InlineData("primary", CharacterGender.Female, "Principal")]
    [InlineData("side", CharacterGender.Female, "Secundária")]
    [InlineData("side", CharacterGender.Male, "Secundário")]
    [InlineData("appears", CharacterGender.Male, "Aparece")]
    public void O_papel_do_VNDB_concorda_com_o_genero(string role, CharacterGender gender, string expected) =>
        Assert.Equal(expected, EditorContext.RoleLabel(role, gender));
}
