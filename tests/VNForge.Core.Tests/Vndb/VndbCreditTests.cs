using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

[Trait("Risco", "R7")]
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
}
