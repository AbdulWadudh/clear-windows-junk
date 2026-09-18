// Clear-WindowsJunk - themed confirm / notice dialogs.
// MessageBox.Show draws stock Win32 chrome that ignores the app's theme entirely,
// so destructive prompts get this instead. Same tokens and fonts as the main window.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace ClearWindowsJunk
{
    public class Dialog : Window
    {
        internal const string Xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource WindowBg}' BorderBrush='{DynamicResource WindowBorder}'
        BorderThickness='1' TextOptions.TextFormattingMode='Ideal'
        TextOptions.TextRenderingMode='ClearType'>
  <Border.Resources>
" + Layout.SharedStyles + @"
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
      <Setter Property='Padding' Value='18,8'/>
      <Setter Property='Margin' Value='8,0,0,0'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='9'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Hover}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Go' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Background' Value='{DynamicResource Accent}'/>
      <Setter Property='BorderBrush' Value='{DynamicResource AccentHi}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='9'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'
                    Padding='{TemplateBinding Padding}'>
              <Grid>
                <!-- a white wash keeps the fill solid on hover instead of hollowing it out -->
                <Border x:Name='wash' CornerRadius='8' Background='White' Opacity='0'/>
                <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='wash' Property='Opacity' Value='0.16'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='wash' Property='Opacity' Value='0.28'/>
              </Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'>
                <Setter TargetName='b' Property='BorderThickness' Value='2'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Background' Value='{DynamicResource Danger}'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Danger}'/>
    </Style>
  </Border.Resources>

  <Grid Margin='24,20,24,18'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,12'>
      <Border x:Name='Dot' Width='9' Height='9' CornerRadius='5' VerticalAlignment='Center'
              Background='{DynamicResource Accent}' Margin='0,0,10,0'/>
      <TextBlock x:Name='Title' FontSize='16' FontWeight='SemiBold'
                 FontFamily='{DynamicResource FontDisplay}'/>
    </StackPanel>

    <TextBlock x:Name='Message' Grid.Row='1' TextWrapping='Wrap' FontSize='13'/>

    <Border x:Name='DetailBox' Grid.Row='2' Margin='0,14,0,0' Visibility='Collapsed'
            CornerRadius='9' Padding='13,10' Background='{DynamicResource Surface}'
            BorderBrush='{DynamicResource Line}' BorderThickness='1'>
      <TextBlock x:Name='Detail' TextWrapping='Wrap' FontSize='11.9'
                 Foreground='{DynamicResource TextMuted}'/>
    </Border>

    <StackPanel Grid.Row='3' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,20,0,0'>
      <Button x:Name='NoBtn'  Content='Cancel' Style='{StaticResource Btn}'/>
      <Button x:Name='YesBtn' Content='Continue' Style='{StaticResource Go}'/>
    </StackPanel>
  </Grid>
</Border>";

        bool _ok;

        Dialog(Window owner, string title, string message, string detail,
               string okText, bool destructive, bool noticeOnly)
        {
            var root = (Border)XamlReader.Parse(Xaml);
            Content = root;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 470;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            WindowStartupLocation = owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;

            ((TextBlock)root.FindName("Title")).Text = title;
            Highlight((TextBlock)root.FindName("Message"), message);

            if (!string.IsNullOrEmpty(detail))
            {
                ((TextBlock)root.FindName("Detail")).Text = detail;
                ((Border)root.FindName("DetailBox")).Visibility = Visibility.Visible;
            }

            var yes = (Button)root.FindName("YesBtn");
            var no = (Button)root.FindName("NoBtn");
            yes.Content = okText;
            if (destructive)
            {
                // local values, not a style swap: a swapped Style silently lost the
                // template's background here, leaving an unpainted primary button
                yes.Background = Theme.Get("Danger");
                yes.BorderBrush = Theme.Get("Danger");
                yes.Foreground = Brushes.White;
                yes.FontWeight = FontWeights.SemiBold;
                ((Border)root.FindName("Dot")).Background = Theme.Get("Danger");
            }
            if (noticeOnly) { no.Visibility = Visibility.Collapsed; yes.Content = "OK"; }

            yes.Click += delegate { _ok = true; Close(); };
            no.Click += delegate { _ok = false; Close(); };
            yes.IsDefault = true;
            no.IsCancel = true;
            MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            Loaded += delegate { yes.Focus(); };
        }

        // Sizes and counts are the part you actually read before clicking a destructive
        // button, so they get full-strength text while the prose sits back a shade.
        static readonly Regex Figures = new Regex(
            @"\d[\d.,]*\s?(?:TB|GB|MB|KB|B)\b|\b\d[\d.,]*\b", RegexOptions.Compiled);

        static void Highlight(TextBlock tb, string text)
        {
            tb.Inlines.Clear();
            tb.Foreground = Theme.Get("TextMuted");
            int at = 0;
            foreach (Match m in Figures.Matches(text ?? ""))
            {
                if (m.Index > at) tb.Inlines.Add(new Run(text.Substring(at, m.Index - at)));
                tb.Inlines.Add(new Run(m.Value)
                {
                    Foreground = Theme.Get("Text"),
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily(Theme.CurrentPair.Mono)
                });
                at = m.Index + m.Length;
            }
            if (text != null && at < text.Length) tb.Inlines.Add(new Run(text.Substring(at)));
        }

        public static bool Confirm(Window owner, string title, string message,
                                   string detail = null, string okText = "Continue",
                                   bool destructive = false)
        {
            var d = new Dialog(owner, title, message, detail, okText, destructive, false);
            d.ShowDialog();
            return d._ok;
        }

        public static void Notice(Window owner, string title, string message, string detail = null)
        {
            new Dialog(owner, title, message, detail, "OK", false, true).ShowDialog();
        }
    }
}
