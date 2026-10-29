using Avalonia.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>O menu da palavra sublinhada pelo corretor no campo da tradução (#511).</summary>
public static class SpellingMenu
{
    public const string IgnoreHeader = "Ignorar esta palavra no projeto";

    /// <summary>
    /// O menu da palavra sublinhada que cobre <paramref name="offset"/> do texto do campo; nulo quando não há palavra marcada ali.
    /// <paramref name="includeEnd"/> vale para o cursor do teclado; um clique do ponteiro passa falso.
    /// </summary>
    public static MenuFlyout? Create(LineDetailViewModel? detail, int offset, bool includeEnd = true)
    {
        if (detail?.MisspellingAt(offset, includeEnd) is not { } span)
        {
            return null;
        }

        var ignore = new MenuItem { Header = IgnoreHeader, Command = detail.IgnoreWordCommand, CommandParameter = span.Text };
        return new MenuFlyout { Items = { ignore } };
    }
}
