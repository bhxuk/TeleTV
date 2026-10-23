using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TeleTV.UI
{
    /// <summary>TeleTV's colours, control styles and small builders for code-only layouts.</summary>
    public static class Theme
    {
        public static readonly Color BgColor = C("#0E1116");
        public static readonly Brush Bg = B("#0E1116");
        public static readonly Brush Deep = B("#08090C");
        public static readonly Brush Chrome = B("#1B1F27");
        public static readonly Brush Rail = B("#12161C");
        public static readonly Brush Surface = B("#171B22");
        public static readonly Brush Surface2 = B("#1E232C");
        public static readonly Brush Raised = B("#2A3140");
        public static readonly Brush Edge = B("#262B35");
        public static readonly Brush BorderStrong = B("#4A5363");
        public static readonly Brush Text = B("#E6EAF0");
        public static readonly Brush Sub = B("#B8C0CC");
        public static readonly Brush Muted = B("#9AA4B2");
        public static readonly Brush Accent = B("#5AA9FF");
        public static readonly Brush AccentSoft = B("#8FB8F0");
        public static readonly Brush AccentBg = B("#172238");
        public static readonly Brush Orange = B("#E8A23A");
        public static readonly Brush Green = B("#3DD68C");
        public static readonly Brush Red = B("#F07A6A");
        public static readonly Brush White = Brushes.White;
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        public static void Install(Application app)
        {
            const string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='BtnBase' TargetType='Button'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Padding' Value='18,9'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.88'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.7'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='White'/><Setter TargetName='bd' Property='BorderThickness' Value='2'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='#5AA9FF'/>
    <Setter Property='Foreground' Value='#0E1116'/>
    <Setter Property='BorderThickness' Value='0'/>
  </Style>
  <Style x:Key='Warm' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='#E8A23A'/>
    <Setter Property='Foreground' Value='#111111'/>
    <Setter Property='BorderThickness' Value='0'/>
  </Style>
  <Style x:Key='Secondary' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#E6EAF0'/>
    <Setter Property='BorderBrush' Value='#4A5363'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontWeight' Value='Normal'/>
  </Style>
  <Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#F07A6A'/>
    <Setter Property='BorderBrush' Value='#6A3A36'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontWeight' Value='Normal'/>
  </Style>
  <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource BtnBase}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='#B8C0CC'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Padding' Value='12,6'/>
    <Setter Property='FontSize' Value='13'/>
  </Style>
  <Style x:Key='NavActive' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Background' Value='#2A3140'/>
    <Setter Property='Foreground' Value='White'/>
  </Style>
  <Style x:Key='Caption' TargetType='Button'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Width' Value='46'/>
    <Setter Property='Height' Value='40'/>
    <Setter Property='Foreground' Value='#B8C0CC'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FontFamily' Value='Segoe Fluent Icons, Segoe MDL2 Assets'/>
    <Setter Property='FontSize' Value='10'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#2A3140'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='CaptionClose' TargetType='Button' BasedOn='{StaticResource Caption}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#C42B1C'/><Setter Property='Foreground' Value='White'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Tile' TargetType='Button'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' CornerRadius='12' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
            <Border.RenderTransform><ScaleTransform x:Name='sc' ScaleX='1' ScaleY='1'/></Border.RenderTransform>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#8FB8F0'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='White'/>
              <Setter TargetName='bd' Property='BorderThickness' Value='3'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='#1E232C'/>
    <Setter Property='Foreground' Value='#E6EAF0'/>
    <Setter Property='CaretBrush' Value='#E6EAF0'/>
    <Setter Property='SelectionBrush' Value='#5AA9FF'/>
    <Setter Property='BorderBrush' Value='#4A5363'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='12,0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FontSize' Value='15'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='bd' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center' Focusable='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='White'/><Setter TargetName='bd' Property='BorderThickness' Value='2'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Border x:Name='track' Width='46' Height='26' CornerRadius='13' Background='#3A4250' Padding='3'>
            <Ellipse x:Name='knob' Width='20' Height='20' Fill='White' HorizontalAlignment='Left'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='track' Property='Background' Value='#5AA9FF'/>
              <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='track' Property='BorderBrush' Value='White'/><Setter TargetName='track' Property='BorderThickness' Value='2'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ProgressBar'>
    <Setter Property='Height' Value='6'/>
    <Setter Property='Foreground' Value='#5AA9FF'/>
    <Setter Property='Background' Value='#2E3542'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Grid>
            <Border x:Name='PART_Track' CornerRadius='3' Background='{TemplateBinding Background}'/>
            <Border x:Name='PART_Indicator' CornerRadius='3' Background='{TemplateBinding Foreground}' HorizontalAlignment='Left'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ThinThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'><Border CornerRadius='4' Background='#40FFFFFF'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Page' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource Page}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Page}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Style='{StaticResource ThinThumb}' Width='6' Margin='0,4'/></Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Track x:Name='PART_Track'>
                <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource Page}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource Page}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Style='{StaticResource ThinThumb}' Height='6' Margin='4,0'/></Track.Thumb>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='#2A3140'/>
    <Setter Property='Foreground' Value='#E6EAF0'/>
    <Setter Property='BorderBrush' Value='#4A5363'/>
  </Style>
</ResourceDictionary>";
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }

        // ---------------- Builders ----------------

        public static TextBlock T(string text, double size = 14, Brush brush = null, FontWeight? weight = null, bool wrap = false)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush ?? Text,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
            };
            return t;
        }

        public static TextBlock Icon(string glyph, double size = 16, Brush brush = null) => new TextBlock
        {
            Text = glyph,
            FontFamily = Icons,
            FontSize = size,
            Foreground = brush ?? Text,
            VerticalAlignment = VerticalAlignment.Center
        };

        public static Button Btn(string text, string style = "Primary", RoutedEventHandler click = null, string glyph = null)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource(style) };
            if (glyph == null) b.Content = text;
            else
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Margin = new Thickness(0, 1, text.Length > 0 ? 8 : 0, 0), VerticalAlignment = VerticalAlignment.Center });
                if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                b.Content = sp;
                if (text.Length == 0) b.SetValue(AutomationPropertiesName, glyph);
            }
            if (click != null) b.Click += click;
            return b;
        }

        private static readonly DependencyProperty AutomationPropertiesName = System.Windows.Automation.AutomationProperties.NameProperty;

        public static Border Card(UIElement child, Thickness? padding = null, Brush border = null) => new Border
        {
            Background = Surface,
            BorderBrush = border ?? Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding ?? new Thickness(24),
            Child = child
        };

        public static StackPanel Stack(double gap, params UIElement[] children) => Stack(Orientation.Vertical, gap, children);

        public static StackPanel Row(double gap, params UIElement[] children) => Stack(Orientation.Horizontal, gap, children);

        private static StackPanel Stack(Orientation o, double gap, UIElement[] children)
        {
            var sp = new StackPanel { Orientation = o };
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is FrameworkElement fe && i > 0)
                {
                    var m = fe.Margin;
                    fe.Margin = o == Orientation.Vertical ? new Thickness(m.Left, m.Top + gap, m.Right, m.Bottom) : new Thickness(m.Left + gap, m.Top, m.Right, m.Bottom);
                    if (o == Orientation.Horizontal && fe.VerticalAlignment == VerticalAlignment.Stretch) fe.VerticalAlignment = VerticalAlignment.Center;
                }
                sp.Children.Add(children[i]);
            }
            return sp;
        }

        public static CheckBox Switch(bool on, Action<bool> changed)
        {
            var c = new CheckBox { Style = (Style)Application.Current.FindResource("Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            c.Checked += (s, e) => changed(true);
            c.Unchecked += (s, e) => changed(false);
            return c;
        }

        /// <summary>A row of pill buttons that work like radio buttons.</summary>
        public static WrapPanel Segmented<T>(IEnumerable<(string label, T value)> options, T current, Action<T> picked)
        {
            var wp = new WrapPanel();
            var buttons = new List<(Button b, T v)>();
            void Paint()
            {
                foreach (var (b, v) in buttons)
                {
                    bool on = EqualityComparer<T>.Default.Equals(v, current);
                    b.Background = on ? Accent : Brushes.Transparent;
                    b.Foreground = on ? Bg : Text;
                    b.BorderBrush = on ? Accent : BorderStrong;
                    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
            foreach (var (label, value) in options)
            {
                var b = new Button { Style = (Style)Application.Current.FindResource("Secondary"), Content = label, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 7, 14, 7), FontSize = 13 };
                var v = value;
                b.Click += (s, e) => { current = v; Paint(); picked(v); };
                buttons.Add((b, v));
                wp.Children.Add(b);
            }
            Paint();
            return wp;
        }

        /// <summary>A label with a hint underneath, and a control on the right.</summary>
        public static Grid SettingRow(string title, string hint, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = Stack(2, T(title, 15, Text, FontWeights.Medium));
            if (!string.IsNullOrEmpty(hint)) left.Children.Add(T(hint, 13, Muted, wrap: true));
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 0, 24, 0);
            g.Children.Add(left);
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public static Border Divider() => new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 16, 0, 16) };

        public static ScrollViewer Scroll(UIElement content) => new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };

        /// <summary>A round spinner that turns for as long as it's visible.</summary>
        public static FrameworkElement Spinner(double size = 20, Brush brush = null)
        {
            var arc = new Ellipse
            {
                Width = size,
                Height = size,
                StrokeThickness = Math.Max(2, size / 9),
                Stroke = new SolidColorBrush(C("#2E3A4F")),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var top = new Ellipse
            {
                Width = size,
                Height = size,
                StrokeThickness = Math.Max(2, size / 9),
                Stroke = brush ?? Accent,
                StrokeDashArray = new DoubleCollection { Math.PI * (size - size / 9) / (Math.Max(2, size / 9)) * 0.25, 1000 },
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var rot = new RotateTransform();
            top.RenderTransform = rot;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            rot.BeginAnimation(RotateTransform.AngleProperty, anim);
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(arc);
            g.Children.Add(top);
            return g;
        }

        // Segoe Fluent Icons glyphs
        public const string GHome = "";
        public const string GImport = "";
        public const string GDownload = "";
        public const string GApps = "";
        public const string GSettings = "";
        public const string GBack = "";
        public const string GForward = "";
        public const string GRefresh = "";
        public const string GFull = "";
        public const string GExitFull = "";
        public const string GMin = "";
        public const string GMax = "";
        public const string GRestore = "";
        public const string GClose = "";
        public const string GTick = "";
        public const string GError = "";
        public const string GFolder = "";
        public const string GGlobe = "";
        public const string GStar = "";
        public const string GStarFilled = "";
        public const string GDelete = "";
        public const string GPlay = "";
        public const string GStop = "";
        public const string GUpload = "";
        public const string GTv = "";
        public const string GInfo = "";
        public const string GPower = "";
        public const string GKeyboard = "";
        public const string GList = "";
        public const string GMore = "";
        public const string GSearch = "";
    }
}
