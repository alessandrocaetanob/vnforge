using System.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma pasta de projeto de tradução: <c>vnforge.json</c> mais <c>text/</c>, <c>.cache/</c>, <c>build/</c> e <c>backup/</c>.</summary>
public sealed class ProjectFolder
{
    public const string ConfigFileName = "vnforge.json";

    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const string GitIgnoreContent = "# Gerado pelo VNForge: arquivos do jogo e backups dos originais nunca vão para o git\n.cache/\nbuild/\nbackup/\n";

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
        try
        {
            folder.WriteConfig(json, overwrite: false);
            folder.WriteGitIgnoreIfMissing();
        }
        catch (IOException) when (File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"Já existe um {ConfigFileName} em '{folder.Root}'; o projeto não foi sobrescrito.");
        }

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

    public ProjectConfig LoadConfig()
    {
        string json;
        try
        {
            json = Utf8Estrito.GetString(File.ReadAllBytes(ConfigPath));
        }
        catch (DecoderFallbackException)
        {
            throw new ProjectConfigException("$", "o arquivo não está em UTF-8 válido.");
        }

        return ProjectConfigSerializer.Parse(json.TrimStart((char)0xFEFF));
    }

    public void SaveConfig(ProjectConfig config) =>
        WriteConfig(ProjectConfigSerializer.Serialize(config), overwrite: true);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(TextDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(BuildDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    private void WriteGitIgnoreIfMissing()
    {
        var path = Path.Combine(Root, ".gitignore");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, GitIgnoreContent, Utf8SemBom);
        }
    }

    private void WriteConfig(string json, bool overwrite)
    {
        var temporary = Path.Combine(Root, $"{ConfigFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, json, Utf8SemBom);
            File.Move(temporary, ConfigPath, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
