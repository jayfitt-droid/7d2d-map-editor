using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MapEditor.App;

/// <summary>
/// A message box in the app's Fluent theme (light or dark, with the accent colour), in place of the Windows one, which
/// ignores it. It takes the same arguments as <see cref="MessageBox.Show(Window, string, string, MessageBoxButton, MessageBoxImage)"/>
/// and answers the same way: Enter picks the default (OK or Yes), Esc or the close button cancels (Cancel, else No,
/// else OK), and Ctrl+C copies the message.
/// </summary>
public partial class ThemedMessageBox : Window
{
    private MessageBoxResult _result;
    private readonly MessageBoxResult _cancelResult;

    private ThemedMessageBox(string text, string caption, MessageBoxButton buttons, MessageBoxImage image, IReadOnlyList<string>? labels)
    {
        InitializeComponent();
        Title = caption;
        Message.Text = text;

        (string Data, string Brush)? glyph = image switch
        {
            MessageBoxImage.Error => ("M12,2 a10,10 0 1,0 0.01,0 Z M8.5,8.5 L15.5,15.5 M15.5,8.5 L8.5,15.5", "SystemFillColorCriticalBrush"),
            MessageBoxImage.Warning => ("M12,3 L22,20 H2 Z M12,9 V14 M12,17 V17.2", "SystemFillColorCautionBrush"),
            MessageBoxImage.Question => ("M12,2 a10,10 0 1,0 0.01,0 Z M9.2,9.2 A2.9,2.9 0 1,1 13.4,11.8 C12.4,12.4 12,13 12,14 M12,17 V17.2", "AccentFillColorDefaultBrush"),
            MessageBoxImage.Information => ("M12,2 a10,10 0 1,0 0.01,0 Z M12,11 V17 M12,7.5 V7.7", "AccentFillColorDefaultBrush"),
            _ => null,
        };
        if (glyph is { } g)
        {
            var geometry = Geometry.Parse(g.Data);
            geometry.Freeze();
            Glyph.Data = geometry;
            Glyph.SetResourceReference(Shape.StrokeProperty, g.Brush);
            Glyph.Visibility = Visibility.Visible;
        }

        var choices = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            _ => [MessageBoxResult.OK],
        };
        _cancelResult = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : choices.Contains(MessageBoxResult.No) ? MessageBoxResult.No
            : MessageBoxResult.OK;
        _result = _cancelResult;
        for (int i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            var button = new Button
            {
                Content = labels is not null && i < labels.Count ? labels[i] : choice.ToString(),
                MinWidth = 96,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = choice == _cancelResult && choices.Length > 1,
            };
            if (i == 0) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            button.Click += (_, _) =>
            {
                _result = choice;
                Close();
            };
            Buttons.Children.Add(button);
        }
        Loaded += (_, _) => ((Button)Buttons.Children[0]).Focus();

        (image switch
        {
            MessageBoxImage.Error => SystemSounds.Hand,
            MessageBoxImage.Warning => SystemSounds.Exclamation,
            MessageBoxImage.Question => SystemSounds.Question,
            MessageBoxImage.Information => SystemSounds.Asterisk,
            _ => null,
        })?.Play();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try
            {
                Clipboard.SetText($"{Title}\n\n{Message.Text}");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard is busy; nothing to copy into.
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Buttons.Children.Count == 1)
        {
            // A lone OK closes on Esc too, as in Windows.
            Close();
            e.Handled = true;
        }
    }

    /// <param name="labels">Words for the buttons in order (for example Save, Don't save, Cancel); the answers stay the same.</param>
    public static MessageBoxResult Show(Window? owner, string text, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None, IReadOnlyList<string>? labels = null)
    {
        var box = new ThemedMessageBox(text, caption, buttons, image, labels);
        if (owner is { IsLoaded: true })
            box.Owner = owner;
        else
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        box.ShowDialog();
        return box._result;
    }
}
