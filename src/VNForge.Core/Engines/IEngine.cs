namespace VNForge.Core.Engines;

/// <summary>
/// Uma engine de jogo suportada (YU-RIS, KiriKiri, Ren'Py...). Cada engine vive num projeto
/// <c>VNForge.Engines.X</c> que implementa esta interface; o Core só conhece o contrato.
/// </summary>
public interface IEngine
{
    /// <summary>Identificador estável e em minúsculas da engine, usado em arquivos de projeto e na CLI (ex.: <c>"yuris"</c>).</summary>
    string Id { get; }

    /// <summary>Nome para mostrar ao usuário (ex.: <c>"YU-RIS"</c>); por padrão o próprio <see cref="Id"/>.</summary>
    string DisplayName => Id;

    /// <summary>
    /// Examina a pasta de um jogo, só lendo, e diz se a engine a reconhece.
    /// Nunca grava nada na pasta e não lança exceção para uma pasta de outra engine.
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    EngineDetection Detect(string gameDir);

    /// <summary>Formatos de pacote de arquivos que a engine sabe abrir (ex.: YPF).</summary>
    IReadOnlyList<IArchiveFormat> Archives { get; }

    /// <summary>Formatos de script que a engine sabe ler e reconstruir (ex.: YSTB).</summary>
    IReadOnlyList<IScriptFormat> Scripts { get; }

    /// <summary>Alterações opcionais no jogo além dos scripts e pacotes (ex.: patch de codepage no executável).</summary>
    IReadOnlyList<IGamePatch> Patches { get; }
}
