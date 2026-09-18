// Clear-WindowsJunk - XAML for the shell, kept as a string so the whole app compiles
// with csc.exe alone (no SDK, no build action for .xaml files).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClearWindowsJunk
{
    public static class Layout
    {
        // Application-level, not per window: a ToolTip is hosted in a Popup, which is its
        // own visual tree, so an implicit style sitting in a window's Border.Resources
        // never reaches it. Left alone it draws the stock pale Win32 box with black text,
        // which on the dark theme reads as a bug. Foreground and the font are set on the
        // style itself because they inherit down into the ContentPresenter's text.
        public const string AppStyles = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border CornerRadius='7' Padding='10,6'
                  Background='{DynamicResource SurfaceAlt}'
                  BorderBrush='{DynamicResource Line}' BorderThickness='1'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
";

        // Implicit styles (no x:Key) that every window needs. WPF resolves an implicit
        // style for an element inside a DataTemplate against the window's own resources,
        // never an ancestor element's, so a window that does not carry these renders
        // stock Win32 chrome - a native checkbox, a native scrollbar, and black text on
        // the dark surface. Each window splices this same string into its
        // Border.Resources: one copy of the templates instead of three that drift.
        public const string SharedStyles = @"
    <Style TargetType='TextBlock'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='TextOptions.TextFormattingMode' Value='Ideal'/>
      <Setter Property='TextOptions.TextRenderingMode' Value='ClearType'/>
    </Style>
    <!-- checkbox: rounded, fills with accent, no stock chrome -->
    <Style TargetType='CheckBox'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='CheckBox'>
            <!-- The outer grid is transparent and fills the whole control, so every
                 pixel the row covers is hit-testable. Padding insets only the visuals,
                 which keeps the row's breathing space clickable instead of dead. -->
            <Grid Background='Transparent'>
              <Grid Margin='{TemplateBinding Padding}'>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>
                <Border x:Name='box' Width='17' Height='17' CornerRadius='5'
                        Background='{DynamicResource Well}' BorderBrush='{DynamicResource TextFaint}'
                        BorderThickness='1.2' VerticalAlignment='Center'>
                  <Grid>
                    <Path x:Name='tick' Data='M 0,3.6 L 2.8,6.4 L 8,0.6' Stroke='White'
                          StrokeThickness='1.9' Opacity='0' Margin='3,4,0,0'
                          StrokeEndLineCap='Round' StrokeStartLineCap='Round'/>
                    <Border x:Name='dash' Width='8' Height='2' CornerRadius='1' Background='White'
                            Opacity='0' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                  </Grid>
                </Border>
                <ContentPresenter Grid.Column='1' Margin='9,0,0,0' VerticalAlignment='Center'
                                  HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
              </Grid>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource AccentHi}'/>
                <Setter TargetName='tick' Property='Opacity' Value='1'/>
              </Trigger>
              <!-- partial selection reads as blue, so it is never mistaken for 'all ticked' -->
              <Trigger Property='IsChecked' Value='{x:Null}'>
                <Setter TargetName='box' Property='Background' Value='{DynamicResource Mixed}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource MixedHi}'/>
                <Setter TargetName='dash' Property='Opacity' Value='1'/>
              </Trigger>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter Property='Opacity' Value='0.35'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='TextBox'>
      <Setter Property='Background' Value='{DynamicResource Well}'/>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
      <Setter Property='BorderThickness' Value='1'/>
      <Setter Property='Padding' Value='8,3'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border Background='{TemplateBinding Background}' CornerRadius='7'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
              <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- thin dark scrollbar -->
    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='5'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Grid Background='Transparent'>
              <Track x:Name='PART_Track' IsDirectionReversed='True'>
                <Track.Thumb>
                  <Thumb>
                    <Thumb.Template>
                      <ControlTemplate TargetType='Thumb'>
                        <Border CornerRadius='3' Background='{DynamicResource Line}' Margin='1,0'/>
                      </ControlTemplate>
                    </Thumb.Template>
                  </Thumb>
                </Track.Thumb>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0'/>
                </Track.IncreaseRepeatButton>
                <Track.DecreaseRepeatButton>
                  <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0'/>
                </Track.DecreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
";

        public const string MainXaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        BorderBrush='{DynamicResource WindowBorder}' BorderThickness='1'
        Background='{DynamicResource WindowBg}'
        TextOptions.TextRenderingMode='ClearType'>

  <Border.Resources>
    <BooleanToVisibilityConverter x:Key='B2V'/>



" + SharedStyles + @"

    <!-- section label: small, wide-tracked, muted -->
    <Style x:Key='Eyebrow' TargetType='TextBlock'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='FontSize' Value='11'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
    </Style>


    <Style x:Key='Chip' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
      <Setter Property='Padding' Value='12,5'/>
      <Setter Property='Margin' Value='0,0,6,0'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='14'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Subtle}'/>
                <Setter Property='Foreground' Value='{DynamicResource Text}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='Primary' TargetType='Button'>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='Padding' Value='22,9'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' CornerRadius='9' Padding='{TemplateBinding Padding}'>
              <Border.Background>
                <DynamicResource ResourceKey='AccentGrad'/>
              </Border.Background>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Opacity' Value='0.88'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.3'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='Seg' TargetType='RadioButton'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Margin' Value='0,0,7,0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RadioButton'>
            <Border x:Name='b' CornerRadius='14' Padding='14,6'
                    Background='{DynamicResource Well}' BorderThickness='1'
                    BorderBrush='{DynamicResource Line}'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
              </Trigger>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='Badge' TargetType='Button'>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' CornerRadius='10' Padding='9,2'
                    Background='{DynamicResource Subtle}'
                    BorderBrush='{DynamicResource Line}' BorderThickness='1'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Hover}'/>
                <Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Warn}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='ChevBtn' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource TextFaint}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Width='20' Height='20' CornerRadius='6' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Subtle}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='IconBtn' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Margin' Value='0,0,12,0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Width='26' Height='26' CornerRadius='7'
                    Background='{DynamicResource Subtle}' BorderThickness='1'
                    BorderBrush='{DynamicResource Line}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Foreground' Value='{DynamicResource Text}'/>
      <Setter Property='Padding' Value='18,9'/>
      <Setter Property='FontSize' Value='13'/>
    </Style>

    <Style x:Key='Caption' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='Transparent' Width='36' Height='30'
                    CornerRadius='8' Margin='3,0' BorderThickness='1' BorderBrush='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Subtle}'/>
                <Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Line}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Line}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style x:Key='CloseCap' TargetType='Button' BasedOn='{StaticResource Caption}'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='Transparent' Width='36' Height='30'
                    CornerRadius='8' Margin='3,0'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{DynamicResource Danger}'/>
                <Setter Property='Foreground' Value='White'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- nav rail item -->
    <Style x:Key='Nav' TargetType='RadioButton'>
      <Setter Property='Foreground' Value='{DynamicResource TextMuted}'/>
      <Setter Property='FontFamily' Value='{DynamicResource FontUi}'/>
      <Setter Property='FontSize' Value='12'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Margin' Value='8,2'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RadioButton'>
            <Grid>
              <Border x:Name='pill' CornerRadius='8' Background='Transparent' Padding='12,9'>
                <ContentPresenter VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='mark' Width='3' Height='16' CornerRadius='2' Background='{DynamicResource Accent}'
                      HorizontalAlignment='Left' VerticalAlignment='Center' Opacity='0' Margin='-8,0,0,0'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='pill' Property='Background' Value='{DynamicResource Subtle}'/>
                <Setter TargetName='mark' Property='Opacity' Value='1'/>
                <Setter Property='Foreground' Value='{DynamicResource Text}'/>
              </Trigger>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter Property='Foreground' Value='{DynamicResource Text}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>


    <DataTemplate x:Key='TargetRow'>
                  <StackPanel>
                    <StackPanel Orientation='Horizontal' Margin='12,18,10,7'
                                Visibility='{Binding ShowHeader, Converter={StaticResource B2V}}'>
                      <CheckBox IsChecked='{Binding GroupSelected, Mode=TwoWay}'
                                VerticalAlignment='Center' Margin='0,0,11,0'
                                ToolTip='Tick or clear every row in this group'/>
                      <TextBlock Text='{Binding HeaderText}' Style='{StaticResource Eyebrow}'
                                 VerticalAlignment='Center'/>
                    </StackPanel>
                    <Border x:Name='row' Padding='0' CornerRadius='9' Background='Transparent' Margin='2,0'>
                      <Grid>
                       <Grid.ColumnDefinitions>
                         <ColumnDefinition Width='Auto'/>
                         <ColumnDefinition Width='*'/>
                       </Grid.ColumnDefinitions>
                       <Button x:Name='expandBtn' Grid.Column='0' Tag='{Binding Name}'
                               Content='{Binding Chevron}' FontFamily='Segoe MDL2 Assets' FontSize='9'
                               Style='{StaticResource ChevBtn}' Margin='10,0,4,0'
                               Visibility='{Binding CanOpen, Converter={StaticResource B2V}}'
                               ToolTip='Show what is inside'/>
                       <CheckBox Grid.Column='1' IsChecked='{Binding Checked, Mode=TwoWay}' IsEnabled='{Binding Enabled}'
                                HorizontalContentAlignment='Stretch' VerticalContentAlignment='Center'
                                HorizontalAlignment='Stretch' VerticalAlignment='Stretch'
                                Padding='0,9,10,9' Margin='0'>
                        <Grid Margin='2,0,0,0'>
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='*'/>
                            <ColumnDefinition Width='Auto'/>
                          </Grid.ColumnDefinitions>
                          <StackPanel Grid.Column='0' VerticalAlignment='Center'>
                            <TextBlock Text='{Binding Name}' FontSize='13' Foreground='{DynamicResource Text}'/>
                            <TextBlock Text='{Binding LockNote}' Foreground='{DynamicResource TextMuted}' FontSize='11'
                                       TextTrimming='CharacterEllipsis'
                                       Visibility='{Binding HasNote, Converter={StaticResource B2V}}'/>
                          </StackPanel>
                          <StackPanel Grid.Column='1' Orientation='Horizontal' HorizontalAlignment='Right'
                                      VerticalAlignment='Center'>
                          <Button x:Name='openBtn' Tag='{Binding FirstPath}' Content='&#xE838;'
                                  FontFamily='Segoe MDL2 Assets' FontSize='12' Opacity='0'
                                  Style='{StaticResource IconBtn}' ToolTip='Open this folder'
                                  Visibility='{Binding CanOpen, Converter={StaticResource B2V}}'/>
                          <StackPanel HorizontalAlignment='Right' VerticalAlignment='Center'>
                            <TextBlock Text='{Binding SizeText}' FontSize='13' HorizontalAlignment='Right'
                                       FontFamily='{DynamicResource FontMono}'
                                       Foreground='{Binding SizeBrush}'
                                       Typography.NumeralAlignment='Tabular'/>
                            <TextBlock Text='{Binding FilesText}' Foreground='{DynamicResource TextFaint}' FontSize='11'
                                       HorizontalAlignment='Right'/>
                          </StackPanel>
                          </StackPanel>
                        </Grid>
                      </CheckBox>
                      </Grid>
                    </Border>
                  </StackPanel>
                  <DataTemplate.Triggers>
                    <Trigger SourceName='row' Property='IsMouseOver' Value='True'>
                      <Setter TargetName='row' Property='Background' Value='{DynamicResource Subtle}'/>
                      <Setter TargetName='openBtn' Property='Opacity' Value='1'/>
                    </Trigger>
                  </DataTemplate.Triggers>
                </DataTemplate>

    <DataTemplate x:Key='NodeRow'>
      <Border x:Name='nrow' Padding='0' CornerRadius='8' Background='Transparent' Margin='2,0'>
        <Grid Margin='{Binding Indent}'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='*'/>
          </Grid.ColumnDefinitions>
          <Button x:Name='nodeBtn' Grid.Column='0' Tag='{Binding FullPath}' Content='{Binding Chevron}'
                  FontFamily='Segoe MDL2 Assets' FontSize='9' Style='{StaticResource ChevBtn}'
                  Visibility='{Binding ChevronShown}' Margin='0,0,4,0'/>
          <!-- everything to the right of the chevron is the checkbox's content, so the
               whole row toggles - name, actions, size and the padding around them -->
          <CheckBox Grid.Column='1' IsChecked='{Binding Checked, Mode=TwoWay}'
                    Visibility='{Binding BoxShown}'
                    HorizontalContentAlignment='Stretch' VerticalContentAlignment='Center'
                    HorizontalAlignment='Stretch' VerticalAlignment='Stretch'
                    Padding='0,5,10,5' Margin='0'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column='0' Text='{Binding Name}' FontSize='12'
                         Foreground='{DynamicResource TextMuted}' VerticalAlignment='Center'
                         TextTrimming='CharacterEllipsis'/>
              <StackPanel Grid.Column='1' Orientation='Horizontal' HorizontalAlignment='Right'
                          VerticalAlignment='Center'>
                <Button x:Name='nodeOpen' Tag='{Binding FullPath}' Content='&#xE838;'
                        FontFamily='Segoe MDL2 Assets' FontSize='11' Opacity='0'
                        Style='{StaticResource IconBtn}' Margin='0,0,6,0' ToolTip='Open in Explorer'/>
                <Button x:Name='nodeDel' Tag='{Binding FullPath}' Content='&#xE74D;'
                        FontFamily='Segoe MDL2 Assets' FontSize='11' Opacity='0'
                        Style='{StaticResource IconBtn}' Margin='0,0,12,0' ToolTip='Delete just this entry'/>
                <StackPanel HorizontalAlignment='Right' VerticalAlignment='Center'>
                  <TextBlock Text='{Binding SizeText}' FontSize='12' HorizontalAlignment='Right'
                             FontFamily='{DynamicResource FontMono}' Foreground='{Binding Tint}'
                             Typography.NumeralAlignment='Tabular'/>
                  <TextBlock Text='{Binding FilesText}' FontSize='10' HorizontalAlignment='Right'
                             Foreground='{DynamicResource TextFaint}'/>
                </StackPanel>
              </StackPanel>
            </Grid>
          </CheckBox>
        </Grid>
      </Border>
      <DataTemplate.Triggers>
        <Trigger SourceName='nrow' Property='IsMouseOver' Value='True'>
          <Setter TargetName='nrow' Property='Background' Value='{DynamicResource Hover}'/>
          <Setter TargetName='nodeOpen' Property='Opacity' Value='1'/>
          <Setter TargetName='nodeDel' Property='Opacity' Value='1'/>
        </Trigger>
      </DataTemplate.Triggers>
    </DataTemplate>
  </Border.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
    </Grid.RowDefinitions>

    <!-- ===================== title bar ===================== -->
    <Border x:Name='TitleBar' Grid.Row='0' Background='Transparent' Height='48'>
      <Grid>
        <StackPanel Orientation='Horizontal' Margin='20,0' VerticalAlignment='Center'>
          <Ellipse Width='9' Height='9' Margin='0,0,10,0'>
            <Ellipse.Fill><DynamicResource ResourceKey='Accent'/></Ellipse.Fill>
          </Ellipse>
          <TextBlock Text='Clear Windows Junk' FontWeight='SemiBold' FontSize='13'
                     FontFamily='{DynamicResource FontDisplay}'/>
          <Button x:Name='BadgeBox' Margin='12,0,0,0' Style='{StaticResource Badge}'>
            <TextBlock x:Name='AdminBadge' Text='' FontSize='11'/>
          </Button>
        </StackPanel>

        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
          <!-- disk readout, promoted out of the old sidebar -->
          <StackPanel VerticalAlignment='Center' Margin='0,0,10,0' HorizontalAlignment='Right'>
            <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
              <TextBlock x:Name='FreeBig' Text='' FontSize='13' FontWeight='SemiBold'
                         FontFamily='{DynamicResource FontMono}'/>
              <TextBlock x:Name='FreeSub' Text='' FontSize='11' Margin='6,0,0,0'
                         VerticalAlignment='Center' Foreground='{DynamicResource TextFaint}'/>
            </StackPanel>
            <Border Height='3' CornerRadius='2' Background='{DynamicResource TrackBg}' Margin='0,4,0,0'>
              <Border x:Name='DiskFill' Height='3' CornerRadius='2' HorizontalAlignment='Left' Width='0'
                      Background='{DynamicResource Accent}'/>
            </Border>
          </StackPanel>

          <Button x:Name='SettingsBtn' Content='&#xE713;' FontFamily='Segoe MDL2 Assets' FontSize='13'
                  Style='{StaticResource Caption}' ToolTip='Settings'/>
          <Button x:Name='MinBtn'   Content='&#xE921;' FontFamily='Segoe MDL2 Assets' FontSize='10' Style='{StaticResource Caption}'/>
          <Button x:Name='MaxBtn'   Content='&#xE922;' FontFamily='Segoe MDL2 Assets' FontSize='10' Style='{StaticResource Caption}'/>
          <Button x:Name='CloseBtn' Content='&#xE8BB;' FontFamily='Segoe MDL2 Assets' FontSize='10' Style='{StaticResource CloseCap}'/>
        </StackPanel>
      </Grid>
    </Border>

    <Grid Grid.Row='1'>
      <!-- ===================== settings page ===================== -->
      <Border x:Name='SettingsPane' Visibility='Collapsed' Padding='26,0,26,20'>
        <ScrollViewer VerticalScrollBarVisibility='Auto'>
          <StackPanel MaxWidth='760' HorizontalAlignment='Stretch'>
            <TextBlock Text='Settings' FontSize='26' FontWeight='Light' FontFamily='{DynamicResource FontDisplay}' Margin='0,0,0,2'/>
            <TextBlock x:Name='SetPath' Text='' Foreground='{DynamicResource TextFaint}' FontSize='11'
                       TextTrimming='CharacterEllipsis' Margin='0,0,0,22'/>

            <Border Background='{DynamicResource Surface}' CornerRadius='12' Padding='18'
                    BorderBrush='{DynamicResource Line}' BorderThickness='1' Margin='0,0,0,12'>
              <StackPanel>
                <TextBlock Text='NEVER CLOSE THESE APPS' Style='{StaticResource Eyebrow}' Margin='0,0,0,6'/>
                <TextBlock Text='Skipped no matter which files they hold. Type a process or app name and press Enter.'
                           Foreground='{DynamicResource TextMuted}' FontSize='12' TextWrapping='Wrap' Margin='0,0,0,10'/>
                <Grid Margin='0,0,0,12'>
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                  </Grid.ColumnDefinitions>
                  <TextBox x:Name='SetNeverClose' Grid.Column='0' Height='36' VerticalContentAlignment='Center'/>
                  <Button x:Name='AddNeverClose' Grid.Column='1' Content='Add' Style='{StaticResource Ghost}'
                          Margin='8,0,0,0' Padding='18,7'/>
                </Grid>
                <ItemsControl x:Name='NeverCloseList'>
                  <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
                  </ItemsControl.ItemsPanel>
                  <ItemsControl.ItemTemplate>
                    <DataTemplate>
                      <Border CornerRadius='15' Background='{DynamicResource ChipBg}'
                              BorderBrush='{DynamicResource ChipLine}' BorderThickness='1'
                              Padding='13,6,7,6' Margin='0,0,8,8'>
                        <StackPanel Orientation='Horizontal'>
                          <TextBlock Text='{Binding Name}' FontSize='12' VerticalAlignment='Center'
                                     Foreground='{DynamicResource Text}'/>
                          <Button Tag='{Binding Name}' Width='18' Height='18' Margin='8,0,0,0'
                                  Cursor='Hand' ToolTip='Remove'>
                            <Button.Template>
                              <ControlTemplate TargetType='Button'>
                                <Border x:Name='x' CornerRadius='9' Background='Transparent'>
                                  <TextBlock Text='&#xE711;' FontFamily='Segoe MDL2 Assets' FontSize='8'
                                             Foreground='{DynamicResource TextMuted}' HorizontalAlignment='Center'
                                             VerticalAlignment='Center'/>
                                </Border>
                                <ControlTemplate.Triggers>
                                  <Trigger Property='IsMouseOver' Value='True'>
                                    <Setter TargetName='x' Property='Background' Value='{DynamicResource Danger}'/>
                                  </Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Button.Template>
                          </Button>
                        </StackPanel>
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock x:Name='NeverCloseEmpty' Text='Nothing protected yet.'
                           Foreground='{DynamicResource TextFaint}' FontSize='11'/>
              </StackPanel>
            </Border>

            <Border Background='{DynamicResource Surface}' CornerRadius='12' Padding='18'
                    BorderBrush='{DynamicResource Line}' BorderThickness='1' Margin='0,0,0,12'>
              <StackPanel>
                <TextBlock Text='KEEP RECENT FILES' Style='{StaticResource Eyebrow}' Margin='0,0,0,6'/>
                <TextBlock Text='Files touched inside this many days are left alone. 1 protects the current session.'
                           Foreground='{DynamicResource TextMuted}' FontSize='12' TextWrapping='Wrap' Margin='0,0,0,10'/>
                <StackPanel Orientation='Horizontal'>
                  <TextBox x:Name='SetKeepDays' Width='74' Height='34' TextAlignment='Center'
                           FontFamily='{DynamicResource FontMono}'
                           VerticalContentAlignment='Center'/>
                  <TextBlock Text='days' VerticalAlignment='Center' Foreground='{DynamicResource TextMuted}'
                             FontSize='12' Margin='10,0,0,0'/>
                </StackPanel>
              </StackPanel>
            </Border>

            <Border Background='{DynamicResource Surface}' CornerRadius='12' Padding='18'
                    BorderBrush='{DynamicResource Line}' BorderThickness='1'>
              <StackPanel>
                <TextBlock Text='STORAGE' Style='{StaticResource Eyebrow}' Margin='0,0,0,8'/>
                <CheckBox x:Name='SetPortable' Margin='0,0,0,8'
                          Content='Portable - keep settings in a file next to the app'/>
                <Button x:Name='OpenAppFolder' Content='Open app folder' Style='{StaticResource Ghost}'
                        HorizontalAlignment='Left' Padding='14,6' FontSize='12' Margin='0,0,0,8'/>
                <TextBlock Text='Off: settings live inside the app and nothing is written beside the .exe. On: a settings.txt sits next to it, which the PowerShell version also reads.'
                           Foreground='{DynamicResource TextMuted}' FontSize='11' TextWrapping='Wrap' Margin='0,0,0,18'/>

                <TextBlock Text='APPEARANCE' Style='{StaticResource Eyebrow}' Margin='0,0,0,10'/>
                <StackPanel Orientation='Horizontal' Margin='0,0,0,18'>
                  <RadioButton x:Name='ThemeSystem' Content='Follow Windows' GroupName='thm'
                               Style='{StaticResource Seg}'/>
                  <RadioButton x:Name='ThemeDark'   Content='Dark'  GroupName='thm' Style='{StaticResource Seg}'/>
                  <RadioButton x:Name='ThemeLight'  Content='Light' GroupName='thm' Style='{StaticResource Seg}'/>
                </StackPanel>

                <TextBlock Text='TYPEFACE' Style='{StaticResource Eyebrow}' Margin='0,0,0,10'/>
                <StackPanel Orientation='Horizontal' Margin='0,0,0,6'>
                  <RadioButton x:Name='FontDeveloper'  Content='Developer'  GroupName='fnt' Style='{StaticResource Seg}'/>
                  <RadioButton x:Name='FontTerminal'   Content='Terminal'   GroupName='fnt' Style='{StaticResource Seg}'/>
                  <RadioButton x:Name='FontIndustrial' Content='Industrial' GroupName='fnt' Style='{StaticResource Seg}'/>
                  <RadioButton x:Name='FontSystem'     Content='System'     GroupName='fnt' Style='{StaticResource Seg}'/>
                </StackPanel>
                <TextBlock x:Name='FontSample' Text='Reclaimable  19.83 GB  ·  140,827 files'
                           FontSize='13' Foreground='{DynamicResource TextMuted}' Margin='0,0,0,18'/>

                <TextBlock Text='DEFAULTS' Style='{StaticResource Eyebrow}' Margin='0,0,0,12'/>
                <CheckBox x:Name='SetDryRun'  Content='Start in dry run - report only, never delete' Margin='0,0,0,11'/>
                <CheckBox x:Name='SetUnlock'  Content='Offer to close apps holding locked files' Margin='0,0,0,11'/>
                <CheckBox x:Name='SetForce'   Content='Force apps that ignore the close request' Margin='0,0,0,11'/>
                <CheckBox x:Name='SetRestart' Content='Relaunch closed apps automatically' Margin='0,0,0,0'/>
              </StackPanel>
            </Border>

            <StackPanel Orientation='Horizontal' Margin='0,20,0,0' HorizontalAlignment='Right'>
              <Button x:Name='SettingsCancel' Content='Discard' Style='{StaticResource Ghost}' Margin='0,0,10,0'/>
              <Button x:Name='SettingsSave' Content='Save settings' Style='{StaticResource Primary}'/>
            </StackPanel>
          </StackPanel>
        </ScrollViewer>
      </Border>

      <!-- ===================== clean page ===================== -->
      <Grid x:Name='MainPane' Margin='26,0,26,20'>
        <Grid.RowDefinitions>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='*'/>
          <RowDefinition Height='Auto'/>
        </Grid.RowDefinitions>

        <!-- hero -->
        <Border Grid.Row='0' CornerRadius='14' Padding='24,20' BorderThickness='1' BorderBrush='{DynamicResource LineSoft}'>
          <Border.Background>
            <DynamicResource ResourceKey='HeroBg'/>
          </Border.Background>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='*'/>
              <ColumnDefinition Width='Auto'/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column='0' VerticalAlignment='Center'>
              <TextBlock x:Name='HeroLabel' Text='RECLAIMABLE' Style='{StaticResource Eyebrow}'/>
              <StackPanel Orientation='Horizontal' Margin='0,4,0,0'>
                <TextBlock x:Name='HeroValue' Text='&#x2014;' FontSize='46' FontWeight='Light'
                           FontFamily='{DynamicResource FontDisplay}'
                           Typography.NumeralAlignment='Tabular'/>
                <TextBlock x:Name='HeroUnit' Text='' FontSize='17' FontWeight='Light' Margin='7,0,0,11'
                           VerticalAlignment='Bottom' Foreground='{DynamicResource TextMuted}'/>
              </StackPanel>
              <TextBlock x:Name='HeroSub' Text='' Foreground='{DynamicResource TextMuted}' FontSize='12' Margin='0,2,0,0'/>
              <StackPanel x:Name='HeroStats' Orientation='Horizontal' Margin='0,18,0,0'>
                <StackPanel Margin='0,0,28,0'>
                  <TextBlock x:Name='StatFilesV' Text='&#x2014;' FontSize='16' FontWeight='SemiBold'
                             FontFamily='{DynamicResource FontMono}'/>
                  <TextBlock Text='files' Style='{StaticResource Eyebrow}' Margin='0,2,0,0'/>
                </StackPanel>
                <StackPanel Margin='0,0,28,0'>
                  <TextBlock x:Name='StatBiggestV' Text='&#x2014;' FontSize='16' FontWeight='SemiBold'
                             FontFamily='{DynamicResource FontMono}'/>
                  <TextBlock x:Name='StatBiggestL' Text='biggest' Style='{StaticResource Eyebrow}' Margin='0,2,0,0'/>
                </StackPanel>
                <StackPanel>
                  <TextBlock x:Name='StatAfterV' Text='&#x2014;' FontSize='16' FontWeight='SemiBold'
                             FontFamily='{DynamicResource FontMono}'/>
                  <TextBlock Text='free after' Style='{StaticResource Eyebrow}' Margin='0,2,0,0'/>
                </StackPanel>
              </StackPanel>
            </StackPanel>
            <Grid Grid.Column='1' Width='330'>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='*'/>
              </Grid.ColumnDefinitions>
              <Canvas x:Name='Donut' Grid.Column='0' Width='116' Height='116' VerticalAlignment='Center'/>
              <StackPanel x:Name='Legend' Grid.Column='1' VerticalAlignment='Center' Margin='14,0,0,0'/>
            </Grid>
          </Grid>
        </Border>

        <!-- filters -->
        <Grid Grid.Row='1' Margin='2,16,2,10'>
          <StackPanel Orientation='Horizontal'>
            <Button x:Name='SafeBtn' Content='Safe defaults' Style='{StaticResource Chip}'/>
            <Button x:Name='AllBtn'  Content='All' Style='{StaticResource Chip}'/>
            <Button x:Name='NoneBtn' Content='None' Style='{StaticResource Chip}'/>
          </StackPanel>
          <TextBlock x:Name='SelText' Text='' HorizontalAlignment='Right' VerticalAlignment='Center'
                     Foreground='{DynamicResource TextMuted}' FontSize='12'/>
        </Grid>

        <!-- target list -->
        <Border Grid.Row='2' Background='{DynamicResource Surface}' CornerRadius='14'
                BorderBrush='{DynamicResource Line}' BorderThickness='1'>
          <Grid>
          <TextBlock x:Name='EmptyState' Visibility='Collapsed' HorizontalAlignment='Center'
                     VerticalAlignment='Center' TextAlignment='Center' FontSize='13'
                     Foreground='{DynamicResource TextFaint}'
                     Text='Nothing to clean right now.&#x0a;Your caches are already empty.'/>
          <ScrollViewer x:Name='ListScroll' VerticalScrollBarVisibility='Auto' Padding='8,6,10,6'>
            <ItemsControl x:Name='TargetList'>

            </ItemsControl>
          </ScrollViewer>
          </Grid>
        </Border>

        <!-- footer: three real columns so a long status cannot run under the toggles -->
        <Grid Grid.Row='3' Margin='2,16,2,0'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>

          <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center'>
            <CheckBox x:Name='DryRun' Content='Dry run' Margin='0,0,18,0'/>
            <CheckBox x:Name='Unlock' Content='Close apps holding files' Margin='0,0,18,0'/>
            <TextBlock Text='Keep' VerticalAlignment='Center' FontSize='12'
                       Foreground='{DynamicResource TextMuted}' Margin='0,0,7,0'/>
            <TextBox x:Name='KeepDays' Width='46' Height='28' VerticalContentAlignment='Center'
                     TextAlignment='Center' FontFamily='{DynamicResource FontMono}'/>
            <TextBlock Text='days' VerticalAlignment='Center' FontSize='12'
                       Foreground='{DynamicResource TextMuted}' Margin='7,0,0,0'/>
          </StackPanel>

          <StackPanel Grid.Column='1' Orientation='Horizontal' HorizontalAlignment='Right'
                      VerticalAlignment='Center' Margin='16,0,14,0'>
            <TextBlock x:Name='Status' Text='' VerticalAlignment='Center' FontSize='12'
                       Foreground='{DynamicResource TextMuted}' TextTrimming='CharacterEllipsis'
                       MaxWidth='240' Margin='0,0,12,0'/>
            <Border x:Name='BarBox' Width='96' Height='4' CornerRadius='2'
                    Background='{DynamicResource TrackBg}' Visibility='Collapsed' VerticalAlignment='Center'>
              <Border x:Name='BarFill' Height='4' CornerRadius='2' HorizontalAlignment='Left' Width='0'
                      Background='{DynamicResource Accent}'/>
            </Border>
          </StackPanel>

          <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
            <Button x:Name='CancelBtn' Content='Cancel' Style='{StaticResource Ghost}'
                    Visibility='Collapsed' Margin='0,0,8,0'/>
            <Button x:Name='ScanBtn' Content='Rescan' Style='{StaticResource Ghost}' Margin='0,0,10,0'
                    ToolTip='Rescan every location  (F5)'/>
            <Button x:Name='CleanBtn' Content='Clean selected' Style='{StaticResource Primary}'
                    ToolTip='Clean the ticked locations  (Ctrl+Enter)'/>
          </StackPanel>
        </Grid>

        <!-- result overlay -->
        <Border x:Name='SummaryCard' Grid.Row='2' Visibility='Collapsed' CornerRadius='14'
                Background='{DynamicResource Well}' BorderBrush='{DynamicResource SummaryLine}' BorderThickness='1'>
          <Grid Margin='24,20'>
            <Grid.RowDefinitions>
              <RowDefinition Height='Auto'/>
              <RowDefinition Height='*'/>
              <RowDefinition Height='Auto'/>
            </Grid.RowDefinitions>
            <StackPanel Grid.Row='0'>
              <TextBlock x:Name='SumEyebrow' Text='RESULT' Style='{StaticResource Eyebrow}'/>
              <TextBlock x:Name='SumBig' Text='' FontSize='34' FontWeight='Light' Foreground='{DynamicResource Accent}'
                       FontFamily='{DynamicResource FontDisplay}' Margin='0,3,0,2'/>
              <TextBlock x:Name='SumSub' Text='' Foreground='{DynamicResource TextMuted}' FontSize='12'
                         TextWrapping='Wrap' Margin='0,0,0,12'/>
            </StackPanel>
            <ScrollViewer Grid.Row='1' VerticalScrollBarVisibility='Auto'>
              <StackPanel x:Name='SummaryBody'/>
            </ScrollViewer>
            <Button x:Name='SumClose' Grid.Row='2' Content='Back to list' Style='{StaticResource Ghost}'
                    HorizontalAlignment='Left' Margin='0,14,0,0'/>
          </Grid>
        </Border>
      </Grid>
    </Grid>

    <!-- ===================== progress overlay ===================== -->
    <!-- A scrim over the whole window rather than a modal Window: Engine.Clean opens
         its own modal pickers mid-run (close / force / relaunch), and a second modal
         would fight those for the dialog stack. Spanning both rows also covers the
         caption buttons, so Cancel is the only way out while files are going. -->
    <Border x:Name='BusyOverlay' Grid.Row='0' Grid.RowSpan='2' Visibility='Collapsed'
            Background='#CC070C17'>
      <Border Width='470' CornerRadius='16' Padding='26,22'
              HorizontalAlignment='Center' VerticalAlignment='Center'
              Background='{DynamicResource Surface}'
              BorderBrush='{DynamicResource Line}' BorderThickness='1'>
        <StackPanel>
          <TextBlock x:Name='BusyEyebrow' Text='WORKING' Style='{StaticResource Eyebrow}'/>
          <TextBlock x:Name='BusyTitle' Text='' FontSize='19' FontWeight='SemiBold'
                     FontFamily='{DynamicResource FontDisplay}'
                     Foreground='{DynamicResource Text}' Margin='0,4,0,0'/>
          <TextBlock x:Name='BusyItem' Text='' FontSize='12' Margin='0,8,0,0'
                     Foreground='{DynamicResource TextMuted}'
                     TextTrimming='CharacterEllipsis'/>
          <Grid Margin='0,17,0,0'>
            <Border x:Name='BusyTrack' Height='6' CornerRadius='3'
                    Background='{DynamicResource TrackBg}'/>
            <Border x:Name='BusyFill' Height='6' CornerRadius='3' Width='0'
                    HorizontalAlignment='Left' Background='{DynamicResource AccentGrad}'/>
          </Grid>
          <Grid Margin='0,13,0,0'>
            <TextBlock x:Name='BusyPct' Text='0%' FontSize='12' VerticalAlignment='Center'
                       FontFamily='{DynamicResource FontMono}'
                       Foreground='{DynamicResource TextMuted}'
                       Typography.NumeralAlignment='Tabular'/>
            <Button x:Name='BusyCancel' Content='Cancel' Style='{StaticResource Ghost}'
                    HorizontalAlignment='Right'/>
          </Grid>
        </StackPanel>
      </Border>
    </Border>
  </Grid>
</Border>";

        static Brush B(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

        public static UIElement SummaryRow(string name, string freed, string pending, string status)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var wrap = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                Background = B("#172032"),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = B("#F8FAFC") });
            left.Children.Add(new TextBlock { Text = status ?? "", FontSize = 10, Foreground = B("#64748B") });

            string colour = status != null && status.StartsWith("clean") ? "#22C55E"
                          : status == "locked / in use" ? "#F59E0B" : "#94A3B8";
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock
            {
                Text = freed,
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = B(colour)
            });
            if (pending != "0.00 B")
                right.Children.Add(new TextBlock
                {
                    Text = pending + " left",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Foreground = B("#64748B")
                });

            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            g.Children.Add(left); g.Children.Add(right);
            wrap.Child = g;
            return wrap;
        }

        public static UIElement LegendRow(Brush fill, string label, string value, string pct, string tip)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = fill,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Theme.Get("Text"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 10.5,
                Foreground = Theme.Get("TextMuted"),
                FontFamily = new FontFamily(Theme.CurrentPair.Mono)
            });
            line.Children.Add(new TextBlock
            {
                Text = pct,
                FontSize = 10.5,
                Margin = new Thickness(7, 0, 0, 0),
                Foreground = Theme.Get("TextFaint"),
                FontFamily = new FontFamily(Theme.CurrentPair.Mono)
            });
            sp.Children.Add(line);

            Grid.SetColumn(dot, 0); Grid.SetColumn(sp, 1);
            g.Children.Add(dot); g.Children.Add(sp);

            // the name is trimmed to fit, so the tooltip carries the full text
            g.Background = Brushes.Transparent;
            g.ToolTip = tip;
            g.Cursor = System.Windows.Input.Cursors.Hand;
            ToolTipService.SetInitialShowDelay(g, 120);
            ToolTipService.SetShowDuration(g, 30000);
            return g;
        }
    }
}
