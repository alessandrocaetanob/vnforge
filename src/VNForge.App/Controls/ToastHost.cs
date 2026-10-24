using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Canto da janela onde os avisos curtos aparecem: um <c>ItemsControl</c> sobre <c>ToastService.Toasts</c>. Fica sobre o conteúdo e só captura o
/// mouse nos próprios avisos.
/// </summary>
public sealed class ToastHost : ItemsControl;
