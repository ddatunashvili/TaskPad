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
            <Border x:Name='T' Background='#2A2A33' CornerRadius='4' Margin='3'/>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Background' Value='#3C3C48'/></Trigger>
              <Trigger Property='IsDragging' Value='True'><Setter TargetName='T' Property='Background' Value='#4A4A5A'/></Trigger>
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
  <Setter Property='Foreground' Value='#D4D4D8'/>
  <Setter Property='FontFamily' Value='Segoe UI'/>
  <Setter Property='FontSize' Value='12.5'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ContextMenu'>
      <Border Background='#141418' BorderBrush='#2A2A33' BorderThickness='1' CornerRadius='8' Padding='4' MinWidth='240'>
        <StackPanel IsItemsHost='True'/>
      </Border>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[typeof(MenuItem)] = Parse($@"
<Style {Ns} TargetType='MenuItem'>
  <Setter Property='Foreground' Value='#D4D4D8'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='MenuItem'>
      <Border x:Name='Bd' Background='Transparent' Padding='12,6,14,6' CornerRadius='5'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
          <TextBlock x:Name='Chk' Text='✓' Foreground='#7C6CF6' Visibility='Hidden' FontWeight='Bold'/>
          <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'/>
          <TextBlock Grid.Column='2' Text='{{TemplateBinding InputGestureText}}' Margin='28,0,0,0' Foreground='#71717A'/>
        </Grid>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='#24242C'/></Trigger>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chk' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[typeof(Separator)] = Parse($@"
<Style {Ns} TargetType='Separator'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4' Background='#24242C'/></ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[typeof(ToolTip)] = Parse($@"
<Style {Ns} TargetType='ToolTip'>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ToolTip'>
      <Border Background='#1A1A20' BorderBrush='#2A2A33' BorderThickness='1' CornerRadius='5' Padding='8,4'>
        <ContentPresenter TextElement.Foreground='#D4D4D8' TextElement.FontFamily='Segoe UI' TextElement.FontSize='12'/>
      </Border>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");

            r[SystemColors.ControlBrushKey] = Theme.Bg;
        }

        static object Parse(string xaml) => XamlReader.Parse(xaml);
    }
}
