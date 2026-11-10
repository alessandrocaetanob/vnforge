namespace VNForge.Core.Library;

/// <summary>O atalho de teclado que vale mesmo com outro programa em foco (o jogo), para a captura de tela. Atrás de uma interface para os testes trocarem por um falso; o de verdade está em <c>VNForge.Platform.Windows</c>.</summary>
public interface IGlobalHotkey
{
    /// <summary>
    /// Passa a ouvir a tecla. <paramref name="pressed"/> roda numa thread de fundo, a cada vez que ela é apertada. Devolve o que desfaz o registro, ou nulo quando outro
    /// programa já usa a tecla ou o sistema não tem o recurso.
    /// </summary>
    IDisposable? Register(Action pressed);
}
