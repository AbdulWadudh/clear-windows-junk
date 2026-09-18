// Clear-WindowsJunk - the checkbox picker used for "close which apps", "force which"
// and "relaunch which". Same widget three times, same look as the main window.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace ClearWindowsJunk
{
    public class PickItem
    {
        public string Label { get; set; }
        public string Detail { get; set; }
        public bool Checked { get; set; }
        public object Tag;
    }

    public class PickWindow : Window
    {
        const string Xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource WindowBg}' BorderBrush='{DynamicResource WindowBorder}' BorderThickness='1' TextOptions.TextFormattingMode='Ideal'
        TextOptions.TextRenderingMode='ClearType'>
  <Border.Resources>
    <Style TargetType='TextBlock'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='13'/>
    </Style>
    <Style TargetType='CheckBox'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
    </Style>
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
      <Setter Property='Padding' Value='16,6'/>
      <Setter Property='Margin' Value='6,0,0,0'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
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
  </Border.Resources>

  <Grid Margin='18'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <TextBlock x:Name='Title' Grid.Row='0' FontSize='16' FontWeight='SemiBold' TextWrapping='Wrap'/>
    <TextBlock x:Name='Hint' Grid.Row='1' Foreground='{DynamicResource Warn}' FontSize='11'
               TextWrapping='Wrap' Margin='0,4,0,10'/>

    <Border Grid.Row='2' Background='{DynamicResource Surface}' CornerRadius='12' BorderBrush='{DynamicResource Line}' BorderThickness='1'>
      <ScrollViewer VerticalScrollBarVisibility='Auto' Padding='6'>
        <ItemsControl x:Name='List'>
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Border Padding='10,7'>
                <Grid>
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='Auto'/>
                    <ColumnDefinition Width='*'/>
                  </Grid.ColumnDefinitions>
                  <CheckBox Grid.Column='0' IsChecked='{Binding Checked, Mode=TwoWay}'
                            VerticalAlignment='Center' Margin='0,0,10,0'/>
                  <StackPanel Grid.Column='1'>
                    <TextBlock Text='{Binding Label}'/>
                    <TextBlock Text='{Binding Detail}' Foreground='{DynamicResource TextMuted}' FontSize='11'
                               TextTrimming='CharacterEllipsis'/>
                  </StackPanel>
                </Grid>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Border>

    <StackPanel Grid.Row='3' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,14,0,0'>
      <Button x:Name='AllBtn'  Content='All' Style='{StaticResource Btn}'/>
      <Button x:Name='NoneBtn' Content='None' Style='{StaticResource Btn}'/>
      <Button x:Name='SkipBtn' Content='Skip' Style='{StaticResource Btn}'/>
      <Button x:Name='OkBtn'   Content='Continue' Style='{StaticResource Go}'/>
    </StackPanel>
  </Grid>
</Border>";

        readonly ObservableCollection<PickItem> _items;
        bool _ok;

        PickWindow(string title, List<PickItem> items, string hint, bool destructive)
        {
            var root = (Border)XamlReader.Parse(Xaml);
            Content = root;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            MaxHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;

            _items = new ObservableCollection<PickItem>(items);
            ((TextBlock)root.FindName("Title")).Text = title;
            var h = (TextBlock)root.FindName("Hint");
            h.Text = hint ?? "";
            h.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
            ((ItemsControl)root.FindName("List")).ItemsSource = _items;

            ((Button)root.FindName("AllBtn")).Click += (s, e) => Set(true);
            ((Button)root.FindName("NoneBtn")).Click += (s, e) => Set(false);
            ((Button)root.FindName("SkipBtn")).Click += (s, e) => { _ok = false; Close(); };
            var ok = (Button)root.FindName("OkBtn");
            if (destructive)
            {
                // local values beat style setters; a Style swap loses the painted template
                ok.Background = Theme.Get("Danger");
                ok.BorderBrush = Theme.Get("Danger");
                ok.Foreground = Brushes.White;
            }
            ok.Click += (s, e) => { _ok = true; Close(); };
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
        }

        void Set(bool on)
        {
            var copy = _items.ToList();
            _items.Clear();
            foreach (var i in copy) { i.Checked = on; _items.Add(i); }
        }

        public static List<PickItem> Pick(Window owner, string title, List<PickItem> items,
                                          string hint = null, bool destructive = false)
        {
            if (items == null || items.Count == 0) return new List<PickItem>();
            var w = new PickWindow(title, items, hint, destructive) { Owner = owner };
            w.ShowDialog();
            if (!w._ok) return new List<PickItem>();
            return w._items.Where(i => i.Checked).ToList();
        }
    }
}
