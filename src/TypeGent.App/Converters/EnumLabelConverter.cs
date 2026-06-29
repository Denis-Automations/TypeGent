using System;
using System.Globalization;
using System.Windows.Data;
using TypeGent.App.ViewModels;

namespace TypeGent.App.Converters;

/// <summary>
/// One-way converter turning the UI enums into the friendly labels from the design sketch
/// (e.g. <c>UsQwerty</c> → "US QWERTY", <c>CtrlShiftT</c> → "Ctrl+Shift+T"). Used as the
/// <c>ItemTemplate</c> for the layout and hotkey dropdowns so the bound value stays a strongly
/// typed enum while the user sees readable text.
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        KeyboardLayoutKind.UsQwerty => "US QWERTY",
        HotKeyKind.CtrlShiftT => "Ctrl+Shift+T",
        HotKeyKind.CtrlShiftSpace => "Ctrl+Shift+Space",
        HotKeyKind.CtrlAltT => "Ctrl+Alt+T",
        HotKeyKind.F8 => "F8",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
