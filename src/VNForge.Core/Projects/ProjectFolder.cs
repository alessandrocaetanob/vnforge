using System.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma pasta de projeto de tradução: <c>vnforge.json</c> mais <c>text/</c>, <c>.cache/</c>, <c>build/</c> e <c>backup/</c>.</summary>
public sealed class ProjectFolder
{
    public const string ConfigFileName = "vnforge.json";

    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    private ProjectFolder(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ConfigPath => Path.Combine(Root, ConfigFileName);

    public string TextDirectory => Path.Combine(Root, "text");

    public string CacheDirectory => Path.Combine(Root, ".cache");

    public string BuildDirectory => Path.Combine(Root, "build");

    public string BackupDirectory => Path.Combine(Root, "backup");

    public static ProjectFolder Create(string root, ProjectConfig config)
    {
        var folder = new ProjectFolder(root);
        if (File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"Já existe um {ConfigFileName} em '{folder.Root}'; o projeto não foi sobrescrito.");
        }

        var json = ProjectConfigSerializer.Serialize(config);
        folder.EnsureDirectories();
        folder.WriteConfig(json);
        return folder;
    }

    public static ProjectFolder Open(string root)
    {
        var folder = new ProjectFolder(root);
        if (!File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"{ConfigFileName} não encontrado em '{folder.Root}'; a pasta não é um projeto do VNForge.");
        }

        return folder;
    }

    public ProjectConfig LoadConfig() =>
        ProjectConfigSerializer.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));

    public void SaveConfig(ProjectConfig config) =>
        WriteConfig(ProjectConfigSerializer.Serialize(config));

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(TextDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(BuildDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    private void WriteConfig(string json)
    {
        var temporary = Path.Combine(Root, $"{ConfigFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, json, Utf8SemBom);
            File.Move(temporary, ConfigPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
