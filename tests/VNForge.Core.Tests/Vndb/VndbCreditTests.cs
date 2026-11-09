using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

public class VndbCreditTests
{
    [Fact]
    public void O_credito_completo_cita_as_duas_licencas_e_a_pagina_do_vndb()
    {
        Assert.Contains("Open Database License", VndbCredit.Text);
        Assert.Contains("Database Contents License", VndbCredit.Text);
        Assert.Contains("vndb.org", VndbCredit.Text);
        Assert.EndsWith(VndbCredit.LicenseUrl + ".", VndbCredit.Text);
        Assert.Equal("https://vndb.org/d17#4", VndbCredit.LicenseUrl);
    }

    [Fact]
    public void O_credito_curto_cita_o_vndb_e_a_licenca_do_banco()
    {
        Assert.Contains("VNDB", VndbCredit.ShortText);
        Assert.Contains("Open Database License", VndbCredit.ShortText);
    }

    [Fact]
    public void Mudanca_sem_valor_e_manter_e_um_valor_vira_gravar()
    {
        VndbChange<int> none = default;
        VndbChange<int> set = 60;

        Assert.Equal(VndbChangeKind.Keep, none.Kind);
        Assert.Equal(VndbChangeKind.Set, set.Kind);
        Assert.Equal(60, set.Value);
        Assert.Equal(VndbChangeKind.Clear, VndbChange<int>.Clear.Kind);
    }

    [Fact]
    public void Update_novo_nao_mexe_em_nada()
    {
        var update = new VndbListUpdate();

        Assert.Equal(VndbChangeKind.Keep, update.Vote.Kind);
        Assert.Equal(VndbChangeKind.Keep, update.Notes.Kind);
        Assert.Equal(VndbChangeKind.Keep, update.Started.Kind);
        Assert.Equal(VndbChangeKind.Keep, update.Finished.Kind);
        Assert.Empty(update.LabelsSet);
        Assert.Empty(update.LabelsUnset);
    }
}
