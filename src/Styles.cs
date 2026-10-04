using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace TaskPad
{
    /// Dark templates for scrollbars and menus (the stock WPF ones are light).
    public static class Styles
    {
        const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        public static void Install(ResourceDictionary r)
        {
            r[typeof(ScrollBar)] = Parse($@"
<Style {Ns} TargetType='ScrollBar'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ScrollBar'>
      <Border Background='Transparent'>
        <Track x:Name='PART_Track' Orientation='{{TemplateBinding Orientation}}'>
          <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'>
            <Border x:Name='T' Background='{H(Theme.Hover)}' CornerRadius='4' Margin='3'/>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Background' Value='{H(Theme.FgFaint)}'/></Trigger>
              <Trigger Property='IsDragging' Value='True'><Setter TargetName='T' Property='Background' Value='{H(Theme.FgDim)}'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='Orientation' Value='Vertical'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='True'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
  <Style.Triggers>
    <Trigger Property='Orientation' Value='Vertical'><Setter Property='Width' Value='12'/><Setter Property='MinWidth' Value='12'/></Trigger>
    <Trigger Property='Orientation' Value='Horizontal'><Setter Property='Height' Value='12'/><Setter Property='MinHeight' Value='12'/></Trigger>
  </Style.Triggers>
</Style>");

            r[typeof(ContextMenu)] = Parse($@"
<Style {Ns} TargetType='ContextMenu'>
  <Setter Property='HasDropShadow' Value='False'/>
  <Setter Property='Foreground' Value='{H(Theme.Fg)}'/>
  <Setter Property='FontFamily' Value='Segoe UI'/>
  <Setter Property='FontSize' Value='12.5'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ContextMenu'>
      <Border Background='{H(Theme.Popup)}' BorderBrush='{H(Theme.ChromeBorder)}' BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='240'>
        <StackPanel IsItemsHost='True'/>
      </Border>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[typeof(MenuItem)] = Parse($@"
<Style {Ns} TargetType='MenuItem'>
  <Setter Property='Foreground' Value='{H(Theme.Fg)}'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='MenuItem'>
      <Border x:Name='Bd' Background='Transparent' Padding='12,6,10,6' CornerRadius='5'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='14'/></Grid.ColumnDefinitions>
          <TextBlock x:Name='Chk' Text='✓' Foreground='{H(Theme.Accent)}' Visibility='Hidden' FontWeight='Bold'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'/>
          <TextBlock x:Name='Gesture' Grid.Column='2' Text='{{TemplateBinding InputGestureText}}' Margin='28,0,4,0' Foreground='{H(Theme.FgDim)}'/>
          <TextBlock x:Name='Arrow' Grid.Column='3' Text='›' FontSize='15' Margin='0,-3,0,0' Foreground='{H(Theme.FgDim)}' Visibility='Collapsed'/>
          <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='10' VerticalOffset='-7' AllowsTransparency='True' Focusable='False'
                 PopupAnimation='Fade' IsOpen='{{Binding IsSubmenuOpen, RelativeSource={{RelativeSource TemplatedParent}}}}'>
            <Border Background='{H(Theme.Popup)}' BorderBrush='{H(Theme.ChromeBorder)}' BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='230'>
              <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Border>
          </Popup>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='Role' Value='SubmenuHeader'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='Role' Value='TopLevelHeader'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{H(Theme.Hover)}'/></Trigger>
        <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Bd' Property='Background' Value='{H(Theme.Hover)}'/></Trigger>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chk' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[typeof(Separator)] = Parse($@"
<Style {Ns} TargetType='Separator'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4' Background='{H(Theme.Hover)}'/></ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            // separators inside menus use this key, not typeof(Separator)
            r[MenuItem.SeparatorStyleKey] = r[typeof(Separator)];

            r[typeof(ToolTip)] = Parse($@"
<Style {Ns} TargetType='ToolTip'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ToolTip'>
      <Border Background='{H(Theme.Popup)}' BorderBrush='{H(Theme.ChromeBorder)}' BorderThickness='1' CornerRadius='5' Padding='8,4'>
        <ContentPresenter TextElement.Foreground='{H(Theme.Fg)}' TextElement.FontFamily='Segoe UI' TextElement.FontSize='12'/>
      </Border>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[SystemColors.ControlBrushKey] = new System.Windows.Media.SolidColorBrush(Theme.Bg.Color); // resources get frozen: use a copy
        }

        static object Parse(string xaml) => XamlReader.Parse(xaml);
        static string H(System.Windows.Media.SolidColorBrush b) => b.Color.ToString();
    }
}
