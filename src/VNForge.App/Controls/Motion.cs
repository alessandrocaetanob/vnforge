using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VNForge.App.Services;
using VNForge.Core.Text;

namespace VNForge.App.Controls;

/// <summary>
/// Movimento do app em um lugar só. <see cref="Follow"/> põe a classe <c>motion</c> na janela enquanto o Windows permite animações; os estilos de
/// <c>Themes/Controls/Motion.axaml</c> só animam sob essa classe. <see cref="PulseOnApprovalProperty"/> faz o pulso verde da linha aprovada.
/// </summary>
public static class Motion
{
    public const string MotionClass = "motion";
    public const string PulseClass = "pulse";

    public static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(700);

    public static readonly AttachedProperty<TranslationStatus> PulseOnApprovalProperty =
        AvaloniaProperty.RegisterAttached<Control, TranslationStatus>("PulseOnApproval", typeof(Motion));

    private static readonly ConditionalWeakTable<Control, object> LastContext = new();

    static Motion()
    {
        PulseOnApprovalProperty.Changed.AddClassHandler<Control>(OnStatusChanged);
    }

    public static TranslationStatus GetPulseOnApproval(Control control) => control.GetValue(PulseOnApprovalProperty);

    public static void SetPulseOnApproval(Control control, TranslationStatus value) => control.SetValue(PulseOnApprovalProperty, value);

    /// <summary>Mantém a classe <c>motion</c> da janela igual a <see cref="MotionPreferences.AnimationsEnabled"/>, até a janela fechar.</summary>
    public static void Follow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        void Apply() => window.Classes.Set(MotionClass, MotionPreferences.AnimationsEnabled);
        void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Apply);

        Apply();
        MotionPreferences.Changed += OnChanged;
        window.Closed += (_, _) => MotionPreferences.Changed -= OnChanged;
        window.Activated += (_, _) => Apply();
    }

    // O contêiner da lista é reaproveitado entre linhas: só vale como aprovação quando a mesma linha (mesmo DataContext) muda de status.
    private static void OnStatusChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        var context = control.DataContext;
        var sameLine = LastContext.TryGetValue(control, out var previous) && ReferenceEquals(previous, context);
        LastContext.Remove(control);
        if (context is not null)
        {
            LastContext.Add(control, context);
        }

        if (sameLine && MotionPreferences.AnimationsEnabled
            && e.OldValue is TranslationStatus before && before != TranslationStatus.Approved
            && e.NewValue is TranslationStatus.Approved)
        {
            control.Classes.Add(PulseClass);
            DispatcherTimer.RunOnce(() => control.Classes.Remove(PulseClass), PulseDuration);
        }
    }
}
