using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// O conteúdo de um item da lista de scripts (docs/design/components/ScriptItem): 36 px com o nome da cena, o percentual concluído e uma barra fina embaixo.
/// O id (<see cref="Key"/>) não é desenhado; quem usa o controle o põe na dica do mouse.
/// "Atual" é a seleção do <c>ListBoxItem</c> que o contém (<c>ListBox.scripts</c>). Com <see cref="IsRunning"/>, um ícone de sincronização
/// em <c>status-ai-fg</c> aparece antes do percentual; com 100%, um visto toma o lugar do texto e a barra fica em <c>seg-approved</c>.
/// </summary>
public sealed class ScriptItem : TemplatedControl
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<ScriptItem, string?>(nameof(Key));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<ScriptItem, string?>(nameof(Label));
    public static readonly StyledProperty<int> PercentProperty = AvaloniaProperty.Register<ScriptItem, int>(nameof(Percent));
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<ScriptItem, bool>(nameof(IsRunning));

    public static readonly DirectProperty<ScriptItem, string> PercentTextProperty =
        AvaloniaProperty.RegisterDirect<ScriptItem, string>(nameof(PercentText), o => o.PercentText);

    private string percentText = "0%";

    public ScriptItem() => Apply();

    /// <summary>A chave do script (<c>yst00013</c>).</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>O nome da cena ("Manhã de chuva" ou "Cena 13"); sem ele o item mostra só o percentual e a barra (a lista do Editor sempre passa um nome).</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Linhas Revisadas e Aprovadas sobre o total, de 0 a 100.</summary>
    public int Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>Há uma tarefa da IA traduzindo este script.</summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public string PercentText
    {
        get => percentText;
        private set => SetAndRaise(PercentTextProperty, ref percentText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PercentProperty || change.Property == IsRunningProperty || change.Property == LabelProperty || change.Property == KeyProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var percent = Math.Clamp(Percent, 0, 100);
        PercentText = $"{percent}%";
        PseudoClasses.Set(":running", IsRunning);
        PseudoClasses.Set(":complete", percent == 100);
        PseudoClasses.Set(":unlabeled", string.IsNullOrEmpty(Label));
        AutomationProperties.SetName(this, string.IsNullOrEmpty(Label) ? $"{Key}, {PercentText}" : $"{Key}, {Label}, {PercentText}");
    }
}
