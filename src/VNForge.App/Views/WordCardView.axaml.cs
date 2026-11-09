using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>O cartão do dicionário offline do inspetor (#538). Só desenha; o estado e os comandos são do <see cref="LineDetailViewModel"/>.</summary>
public sealed partial class WordCardView : UserControl
{
    public WordCardView() => InitializeComponent();

    /// <summary>O rodapé e o aviso levam à licença do JMdict no site do EDRDG, no navegador padrão.</summary>
    private void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(LineDetailViewModel.JmdictLicenseUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Sem navegador registrado o link não abre; o endereço continua na dica do botão para a pessoa copiar.
        }
    }
}
