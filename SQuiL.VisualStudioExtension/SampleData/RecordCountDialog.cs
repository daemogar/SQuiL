using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;

namespace SQuiL.VisualStudioExtension.SampleData;

/// <summary>
/// Themed modal prompt for the row count to generate.  Mirrors VS Code's
/// <c>vscode.window.showInputBox</c> with the same validation rules: positive
/// integer, max 100.  Built programmatically rather than XAML to keep the
/// extension single-file-per-feature and avoid the XAML codegen step.
///
/// Inherits from <see cref="DialogWindow"/> so it picks up the active SSMS
/// theme (light / dark / blue) without manual colour wiring.
/// </summary>
internal sealed class RecordCountDialog : DialogWindow
{
    private const int MinCount = 1;
    private const int MaxCount = 100;

    private readonly TextBox _countBox;
    private readonly TextBlock _errorLabel;
    private readonly Button _okButton;

    /// <summary>Set when the user clicks OK with a valid value.</summary>
    public int? SelectedCount { get; private set; }

    public RecordCountDialog(string variableName, bool isModify, int defaultCount = 3)
    {
        Title                 = $"{(isModify ? "Modify" : "Insert")} sample data for {variableName}";
        Width                 = 360;
        Height                = 170;
        ResizeMode            = ResizeMode.NoResize;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        HasMinimizeButton     = false;
        HasMaximizeButton     = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text   = "How many records?",
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        _countBox = new TextBox
        {
            Text                = defaultCount.ToString(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _countBox.TextChanged += (_, _) => Validate();
        _countBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _okButton.IsEnabled)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        };
        Grid.SetRow(_countBox, 1);
        root.Children.Add(_countBox);

        _errorLabel = new TextBlock
        {
            Margin     = new Thickness(0, 6, 0, 0),
            Foreground = System.Windows.Media.Brushes.IndianRed,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_errorLabel, 2);
        root.Children.Add(_errorLabel);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 12, 0, 0),
        };

        _okButton = new Button
        {
            Content   = "OK",
            Width     = 80,
            IsDefault = true,
            Margin    = new Thickness(0, 0, 8, 0),
        };
        _okButton.Click += (_, _) => Commit();

        var cancelButton = new Button
        {
            Content   = "Cancel",
            Width     = 80,
            IsCancel  = true,
        };

        buttons.Children.Add(_okButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;

        Loaded += (_, _) =>
        {
            _countBox.SelectAll();
            _countBox.Focus();
            Validate();
        };
    }

    private void Validate()
    {
        if (!int.TryParse(_countBox.Text, out int n) || n < MinCount)
        {
            ShowError("Enter a positive integer.");
            return;
        }
        if (n > MaxCount)
        {
            ShowError($"Maximum {MaxCount} records.");
            return;
        }
        ClearError();
    }

    private void ShowError(string text)
    {
        _errorLabel.Text       = text;
        _errorLabel.Visibility = Visibility.Visible;
        _okButton.IsEnabled    = false;
    }

    private void ClearError()
    {
        _errorLabel.Visibility = Visibility.Collapsed;
        _okButton.IsEnabled    = true;
    }

    private void Commit()
    {
        if (!int.TryParse(_countBox.Text, out int n)) return;
        SelectedCount = n;
        DialogResult  = true;
        Close();
    }
}
