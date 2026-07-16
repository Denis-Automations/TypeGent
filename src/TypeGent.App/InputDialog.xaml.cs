using System.Windows;

namespace TypeGent.App;

/// <summary>
/// A tiny modal text-prompt dialog used by "Save As…" (v2 Phase 12). Returns the entered
/// string via <see cref="Prompt"/>; returns <c>null</c> when the user cancels.
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show a modal prompt owned by the app's main window. Returns <c>null</c> on cancel.</summary>
    public static string? Prompt(string prompt, string title, string defaultValue = "")
    {
        var dlg = new InputDialog
        {
            Title = title,
            Owner = Application.Current.MainWindow,
        };
        dlg.PromptText.Text = prompt;
        dlg.InputBox.Text = defaultValue;
        dlg.InputBox.SelectAll();
        dlg.InputBox.Focus();

        return dlg.ShowDialog() == true ? dlg.InputBox.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
