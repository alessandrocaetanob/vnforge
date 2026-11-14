namespace VNForge.Patching.Tests;

public class ProjectDiskTests
{
    // R2: o rclone montado numa pasta (C:\mnt\x) tem raiz NTFS; só o volume onde o caminho está de fato diz que o disco não é local.
    [Fact]
    public void R2_volume_montado_numa_pasta_conta_como_nao_local_mesmo_com_raiz_local()
    {
        var raiz = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "volume-local"));
        var montagem = Path.Combine(raiz, "mnt", "x") + Path.DirectorySeparatorChar;
        var disco = new PhysicalProjectDisk(
            caminho => caminho.StartsWith(montagem, StringComparison.Ordinal) ? montagem : raiz,
            volume => volume == raiz);

        Assert.False(disco.IsLocal(Path.Combine(montagem, "projeto", "vnforge.json")));
        Assert.True(disco.IsLocal(Path.Combine(raiz, "projetos", "vnforge.json")));
    }
}
