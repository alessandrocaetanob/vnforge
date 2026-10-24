using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Canto da janela onde os avisos curtos aparecem: um <c>ItemsControl</c> sobre <c>ToastService.Toasts</c>. Fica sobre o conteúdo e só captura o
/// mouse nos próprios avisos.
/// </summary>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "O tipo existe só para os estilos selecionarem o canto dos avisos (c|ToastHost).")]
public sealed class ToastHost : ItemsControl;
