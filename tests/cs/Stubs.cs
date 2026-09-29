// Compile-check stand-ins for the WPF and BuzzGUI.Interfaces types the GUI uses.
// Mono has no WPF, so this only proves the GUI code is well-typed; it is never run.
using System; using System.Collections.Generic; using System.ComponentModel;
namespace BuzzGUI.Interfaces {
  public enum ParameterGroupType { Input, Global, Track }
  public interface IParameter { string Name {get;} int MinValue {get;} int MaxValue {get;} int DefValue {get;}
    int GetValue(int track); void SetValue(int track, int value); string DescribeValue(int value); }
  public interface IParameterGroup { ParameterGroupType Type {get;} IList<IParameter> Parameters {get;} int TrackCount {get;set;} }
  public interface IBuzz : INotifyPropertyChanged {}
  public interface IMachineGraph { event Action<IMachine> MachineRemoved; IBuzz Buzz {get;} }
  public interface IMachine { string Name {get;} IList<IParameterGroup> ParameterGroups {get;} IMachineGraph Graph {get;} byte[] SendGUIMessage(byte[] m); }
  public interface IMachineGUIHost {}
  public interface IMachineGUI { IMachine Machine {get;set;} }
  public interface IMachineGUIFactory { IMachineGUI CreateGUI(IMachineGUIHost host); }
}
namespace System.Windows.Threading {
  public enum DispatcherPriority { Render, ContextIdle }
  public class Dispatcher { public object BeginInvoke(DispatcherPriority p, Delegate d) => null; }
  public class DispatcherTimer { public DispatcherTimer(DispatcherPriority p){} public TimeSpan Interval {get;set;} public event EventHandler Tick; public void Start(){} public void Stop(){} }
}
namespace System.Windows.Interop { public class WindowInteropHelper { public WindowInteropHelper(System.Windows.Window w){} public IntPtr Owner {get;set;} } }
namespace System.Windows {
  public struct Point { public Point(double x,double y){X=x;Y=y;} public double X,Y; }
  public struct Size { public Size(double w,double h){Width=w;Height=h;} public double Width,Height; }
  public struct Rect { public Rect(double x,double y,double w,double h){} }
  public struct Thickness { public Thickness(double a){} public Thickness(double a,double b,double c,double d){} }
  public struct CornerRadius { public CornerRadius(double a){} }
  public enum HorizontalAlignment { Left, Center, Right, Stretch } public enum VerticalAlignment { Top, Center, Bottom, Stretch }
  public enum FlowDirection { LeftToRight } public enum WindowState { Normal, Minimized } public enum WindowStartupLocation { CenterScreen }
  public struct FontWeight {} public static class FontWeights { public static FontWeight Bold, Normal; }
  public struct FontStyle {} public static class FontStyles { public static FontStyle Normal; }
  public struct FontStretch {} public static class FontStretches { public static FontStretch Normal; }
  public class RoutedEventArgs : EventArgs { public bool Handled; }
  public delegate void RoutedEventHandler(object s, RoutedEventArgs e);
  public class Freezable { public void Freeze(){} }
  public class DpiScale { public double PixelsPerDip => 1; }
  public class UIElement : System.Windows.Media.Visual {
    public System.Windows.Threading.Dispatcher Dispatcher => null;
    public void Measure(Size s){} public Size DesiredSize => default(Size); public void InvalidateMeasure(){} public void InvalidateVisual(){}
    public Point TranslatePoint(Point p, UIElement e) => p; public bool CaptureMouse() => true; public void ReleaseMouseCapture(){} public bool IsMouseCaptured => false;
    public bool ClipToBounds {get;set;}
    public event System.Windows.Input.MouseButtonEventHandler MouseLeftButtonDown;
    protected virtual void OnRender(System.Windows.Media.DrawingContext dc){}
    protected virtual void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e){}
    protected virtual void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e){}
    protected virtual void OnMouseMove(System.Windows.Input.MouseEventArgs e){}
    protected virtual void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e){}
  }
  public class FrameworkElement : UIElement {
    public double Width {get;set;} public double Height {get;set;} public double MinWidth {get;set;} public double MinHeight {get;set;}
    public double ActualWidth => 0; public double ActualHeight => 0; public Thickness Margin {get;set;}
    public System.Windows.Input.Cursor Cursor {get;set;} public object ToolTip {get;set;}
    public HorizontalAlignment HorizontalAlignment {get;set;} public VerticalAlignment VerticalAlignment {get;set;}
    public event RoutedEventHandler Loaded, Unloaded;
  }
  public class Window : System.Windows.Controls.ContentControl {
    public static Window GetWindow(DependencyObjectStub d) => null;
    public string Title {get;set;} public bool ShowInTaskbar {get;set;} public System.Windows.Media.Brush Background {get;set;}
    public WindowStartupLocation WindowStartupLocation {get;set;} public WindowState WindowState {get;set;}
    public void Show(){} public bool Activate() => true; public void Close(){} public event EventHandler Closed;
  }
  public class DependencyObjectStub {}
}
namespace System.Windows.Controls {
  using System.Windows; using System.Windows.Media;
  public enum Orientation { Horizontal, Vertical } public enum Dock { Left, Top, Right, Bottom }
  public class UIElementCollection { public void Add(UIElement e){} public int Count => 0; }
  public class Panel : FrameworkElement { public UIElementCollection Children {get;} = new UIElementCollection(); public Brush Background {get;set;} }
  public class StackPanel : Panel { public Orientation Orientation {get;set;} }
  public class DockPanel : Panel { public bool LastChildFill {get;set;} public static void SetDock(UIElement e, Dock d){} }
  public class ContentControl : FrameworkElement { public object Content {get;set;} }
  public class UserControl : ContentControl {}
  public class TextBlock : FrameworkElement { public string Text {get;set;} public FontFamily FontFamily {get;set;} public double FontSize {get;set;}
    public FontWeight FontWeight {get;set;} public Brush Foreground {get;set;} public double LineHeight {get;set;} }
  public class Border : FrameworkElement { public Brush Background {get;set;} public Brush BorderBrush {get;set;} public Thickness BorderThickness {get;set;}
    public CornerRadius CornerRadius {get;set;} public UIElement Child {get;set;} public Thickness Padding {get;set;} }
}
namespace System.Windows.Input {
  public class Cursor {} public static class Cursors { public static Cursor Hand; }
  [Flags] public enum ModifierKeys { None = 0, Control = 2 } public static class Keyboard { public static ModifierKeys Modifiers => 0; }
  public class MouseEventArgs : System.Windows.RoutedEventArgs { public System.Windows.Point GetPosition(System.Windows.UIElement e) => default(System.Windows.Point); }
  public class MouseButtonEventArgs : MouseEventArgs { public int ClickCount => 1; }
  public class MouseWheelEventArgs : MouseEventArgs { public int Delta => 0; }
  public delegate void MouseButtonEventHandler(object s, MouseButtonEventArgs e);
}
namespace System.Windows.Media {
  using System.Windows;
  public class Visual : DependencyObjectStub {}
  public struct Color { public byte A,R,G,B; public static Color FromRgb(byte r,byte g,byte b) => new Color{R=r,G=g,B=b}; public static Color FromArgb(byte a,byte r,byte g,byte b) => new Color{A=a,R=r,G=g,B=b}; }
  public class Brush : Freezable {} public class SolidColorBrush : Brush { public SolidColorBrush(Color c){} }
  public static class Brushes { public static Brush Transparent; }
  public enum BrushMappingMode { Absolute }
  public class GradientStop { public GradientStop(Color c, double o){} }
  public class GradientStopCollection { public void Add(GradientStop g){} }
  public class LinearGradientBrush : Brush { public BrushMappingMode MappingMode {get;set;} public Point StartPoint {get;set;} public Point EndPoint {get;set;} public GradientStopCollection GradientStops {get;} = new GradientStopCollection(); }
  public class Pen : Freezable { public Pen(Brush b, double t){} }
  public class FontFamily { public FontFamily(string n){} }
  public class Typeface { public Typeface(FontFamily f, FontStyle s, FontWeight w, FontStretch st){} }
  public class FormattedText { public FormattedText(string s, System.Globalization.CultureInfo c, FlowDirection d, Typeface t, double size, Brush b, double ppd){} public double Width => 0; public double Height => 0; }
  public abstract class DrawingContext { public abstract void DrawRectangle(Brush b, Pen p, Rect r); public abstract void DrawLine(Pen p, Point a, Point b); public abstract void DrawText(FormattedText t, Point p); }
  public static class VisualTreeHelper { public static DpiScale GetDpi(Visual v) => new DpiScale(); }
  public static class CompositionTarget { public static event EventHandler Rendering; }
}
