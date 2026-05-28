using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartAudioSwitcher.UI;

public enum DialogVisualStyle
{
    Info,
    Warning,
    Error,
    Question
}

public partial class StyledMessageDialog : Window
{
    public StyledMessageDialog(string title, string message, DialogVisualStyle style)
    {
        InitializeComponent();
        LblTitle.Text = title;
        LblMessage.Text = message;
        SeverityMarker.Background = new SolidColorBrush(GetColor(style));

        if (style == DialogVisualStyle.Question)
        {
            BtnOk.Visibility = Visibility.Collapsed;
            PnlQuestion.Visibility = Visibility.Visible;
        }
    }

    private static Color GetColor(DialogVisualStyle style)
    {
        return style switch
        {
            DialogVisualStyle.Warning => Color.FromRgb(255, 170, 0),
            DialogVisualStyle.Error => Color.FromRgb(237, 28, 36),
            DialogVisualStyle.Question => Color.FromRgb(58, 191, 248),
            _ => Color.FromRgb(108, 99, 255)
        };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
