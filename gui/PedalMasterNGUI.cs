// Pedal Master N — parameter panel (code-only WPF, no XAML), Buzz 1503.
//
// Buzz loads "Pedal Master N.GUI.dll" from the gear folder next to the native
// machine and shows this panel at the top of the parameter window.
//
//   ┌ PEDAL MASTER N  A/B matched: master −11.1 dB  [METERS][MATCH][BYPASS] ┐
//   │ IN   L ███████████████░░░░░░░░░░░░░░░   -8.1                   │
//   │      R ██████████████░░░░░░░░░░░░░░░░   -8.4                   │
//   │ OUT  L ████████████████████░░░░░░░░░░   -1.0   (true peak)     │
//   │      R ████████████████████░░░░░░░░░░   -1.0                   │
//   │ GR COMP ███░░░░░░░░░░░░░   2.1    LIM █░░░░░░░░░░░░   0.8      │
//   │ M -13.9  S -14.2  I -14.1 LUFS  LRA 5.2 LU  C +0.84            │
//   │ INPUT          │ COMP  [ON]                                     │
//   │ EQ  [ON]       │ LIMITER [ON]                                   │
//   │ STEREO         │ OUTPUT (Dither)                                │
//   └──────────────────────────────────────────────────────────────────┘
//
// Every control is bound to a machine parameter and writes through
// IParameter.SetValue, so the parameter window, patterns, undo and saving
// stay in step; the controls read their parameter back on every tick.
// Meters come from the machine's slot 0 (GUI message v1, PedalMasterN.cpp).
// net48 (Buzz 1503's .NET Framework 4 CLR): no Math.Clamp, no MathF.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BuzzGUI.Interfaces;

namespace WDE.PedalMasterN
{
    public class PedalMasterNGUIFactory : IMachineGUIFactory
    {
        public IMachineGUI CreateGUI(IMachineGUIHost host) => new PedalMasterNGUI();
    }

    public class PedalMasterNGUI : UserControl, IMachineGUI
    {
        const double ColW = 236, ColGap = 16;
        const double PanelW = ColW * 2 + ColGap;

        IMachine imachine;
        readonly MeterLink link = new MeterLink(0);
        readonly PanelMeters meters;
        readonly List<ParamRow> rows = new List<ParamRow>();
        readonly List<Action> refreshers = new List<Action>();
        readonly Dictionary<string, IParameter> paramCache = new Dictionary<string, IParameter>();
        readonly DispatcherTimer timer;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTick;
        bool discardNext = true;
        TextBlock matchStatus;
        string shownStatus;

        public IMachine Machine
        {
            get => imachine;
            set { imachine = value; paramCache.Clear(); discardNext = true; }
        }

        public PedalMasterNGUI()
        {
            var root = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };

            // ── Header: title, METERS, BYPASS ──
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            var bypass = MakeSwitch("BYPASS", "Bypass (latency-compensated, click-free) — compare with the dry input",
                                    "Bypass", Theme.BypassBg, 62);
            DockPanel.SetDock(bypass, Dock.Right);
            header.Children.Add(bypass);
            var match = MakeSwitch("MATCH",
                "Level-matched A/B: the louder of master and dry is turned down to the other's loudness,\n" +
                "so BYPASS compares sound, not level. Switch it off again before rendering.",
                "Match", Theme.MatchBg, 54);
            match.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(match, Dock.Right);
            header.Children.Add(match);
            var metersBtn = MakeButton("METERS", "Open the large meter window (loudness, LRA, PLR, correlation, target assist)",
                                       () => MeterWindow.ShowFor(imachine), 62);
            metersBtn.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(metersBtn, Dock.Right);
            header.Children.Add(metersBtn);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock
            {
                Text = "PEDAL MASTER N", FontFamily = Theme.MonoFamily, FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = Theme.Heading, VerticalAlignment = VerticalAlignment.Center
            });
            matchStatus = new TextBlock
            {
                FontFamily = Theme.MonoFamily, FontSize = 10, Foreground = Theme.MatchBg,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            };
            title.Children.Add(matchStatus);
            header.Children.Add(title);
            root.Children.Add(header);

            // ── Meters + loudness line ──
            meters = new PanelMeters { Width = PanelW };
            root.Children.Add(meters);

            // ── Sections, two columns ──
            var cols = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var left  = new StackPanel { Width = ColW };
            var right = new StackPanel { Width = ColW, Margin = new Thickness(ColGap, 0, 0, 0) };
            cols.Children.Add(left);
            cols.Children.Add(right);
            root.Children.Add(cols);

            Section(left, "INPUT", null, new[] { Row("Input", "Input", bipolar: true, wheel: 5) });
            Section(left, "EQ", "EQ", new[]
            {
                Row("Low Cut",   "Low Cut",   false, 2),
                Row("Slope",     "Slope",     false, 1),
                Row("Low Freq",  "Low Freq",  false, 2),
                Row("Low Gain",  "Low Gain",  true,  5),
                Row("High Freq", "High Freq", false, 2),
                Row("High Gain", "High Gain", true,  5),
                Row("Tilt",      "Tilt",      true,  5),
            });
            Section(left, "STEREO", null, new[]
            {
                Row("Low Mono", "Low Mono", false, 2),
                Row("Width",    "Width",    true,  5),
            });
            Section(right, "COMP", "Comp", new[]
            {
                Row("Threshold", "Threshold", false, 1),
                Row("Ratio",     "Ratio",     false, 1),
                Row("Attack",    "Attack",    false, 1),
                Row("Release",   "Release",   false, 1),
                Row("SC HPF",    "SC HPF",    false, 1),
                Row("Makeup",    "Makeup",    false, 5),
                Row("Mix",       "Comp Mix",  false, 5),
            });
            Section(right, "LIMITER", "Limiter", new[]
            {
                Row("Gain",    "Lim Gain",    false, 5),
                Row("Ceiling", "Ceiling",     false, 1),
                Row("Release", "Lim Release", false, 2),
            });
            Section(right, "OUTPUT", null, new[] { Row("Dither", "Dither", false, 1) });

            Content = new Border
            {
                Background = Theme.Background, CornerRadius = new CornerRadius(3), Child = root,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += Tick;
            Loaded += (_, __) =>
            {
                discardNext = true;
                timer.Start();
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FitHostWindow));
            };
            Unloaded += (_, __) => timer.Stop();
        }

        // ── Parameters ───────────────────────────────────────────────────────
        IParameter Param(string name)
        {
            IParameter p;
            if (paramCache.TryGetValue(name, out p)) return p;
            p = null;
            try
            {
                if (imachine?.ParameterGroups != null)
                    foreach (var g in imachine.ParameterGroups)
                    {
                        if (g?.Parameters == null || g.Type != ParameterGroupType.Global) continue;
                        foreach (var q in g.Parameters)
                            if (q?.Name == name) { p = q; break; }
                        if (p != null) break;
                    }
            }
            catch { }
            if (p != null) paramCache[name] = p;
            return p;
        }

        bool IsOn(string name)
        {
            var p = Param(name);
            try { return p != null && p.GetValue(0) != 0; } catch { return false; }
        }

        void Toggle(string name)
        {
            var p = Param(name);
            if (p == null) return;
            try { p.SetValue(0, p.GetValue(0) == 0 ? 1 : 0); } catch { }
        }

        // ── Building blocks ──────────────────────────────────────────────────
        ParamRow Row(string label, string param, bool bipolar, int wheel)
        {
            var r = new ParamRow(label, () => Param(param), bipolar, wheel);
            rows.Add(r);
            return r;
        }

        void Section(StackPanel col, string title, string switchParam, ParamRow[] sectionRows)
        {
            var head = new DockPanel { Margin = new Thickness(0, col.Children.Count == 0 ? 0 : 8, 0, 3) };
            head.Children.Add(new Rectangle1px());
            if (switchParam != null)
            {
                var sw = MakeSwitch("ON", title + " section on/off", switchParam, Theme.OnBg, 34);
                DockPanel.SetDock(sw, Dock.Right);
                head.Children.Add(sw);
                refreshers.Add(() =>
                {
                    bool on = IsOn(switchParam);
                    foreach (var r in sectionRows) r.Dim = !on;
                });
            }
            head.Children.Add(new TextBlock
            {
                Text = title, FontFamily = Theme.MonoFamily, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = Theme.Heading, VerticalAlignment = VerticalAlignment.Center
            });
            col.Children.Add(head);
            foreach (var r in sectionRows) col.Children.Add(r);
        }

        // Thin rule under each section title (zero-width placeholder that keeps DockPanel happy).
        sealed class Rectangle1px : FrameworkElement
        {
            public Rectangle1px() { DockPanel.SetDock(this, Dock.Bottom); Height = 1; Margin = new Thickness(0, 3, 0, 0); }
            protected override void OnRender(DrawingContext dc) =>
                dc.DrawRectangle(Theme.Track, null, new Rect(0, 0, ActualWidth, 1));
        }

        Border MakeButton(string text, string tooltip, Action onClick, double width)
        {
            var b = new Border
            {
                Width = width, Height = 18, Background = Theme.ButtonBg, BorderBrush = Theme.ButtonEdge,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Child = new TextBlock
                {
                    Text = text, FontFamily = Theme.MonoFamily, FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = Theme.TextBrush, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseLeftButtonDown += (_, e) => { onClick(); e.Handled = true; };
            return b;
        }

        // A button bound to a switch parameter; lit with `onBg` when the switch is on.
        Border MakeSwitch(string text, string tooltip, string param, Brush onBg, double width)
        {
            var b = MakeButton(text, tooltip, () => Toggle(param), width);
            var label = (TextBlock)b.Child;
            bool shown = false, first = true;
            refreshers.Add(() =>
            {
                bool on = IsOn(param);
                if (on == shown && !first) return;
                shown = on; first = false;
                b.Background     = on ? onBg : Theme.ButtonBg;
                label.Foreground = on ? Theme.OnFg : Theme.Dim;
                if (text == "ON" || text == "OFF") label.Text = on ? "ON" : "OFF";
            });
            return b;
        }

        // Widen the hosting parameter window if the panel doesn't fit (Buzz 1503
        // opens it at a fixed width). Only ever grows the window.
        void FitHostWindow()
        {
            try
            {
                var win = Window.GetWindow(this);
                var client = win?.Content as FrameworkElement;
                if (win == null || client == null) return;
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double need = DesiredSize.Width;
                InvalidateMeasure();
                Point left = TranslatePoint(new Point(0, 0), client);
                double spare = Math.Min(Math.Max(left.X, 4), 16);
                double deficit = left.X + need + spare - client.ActualWidth;
                if (deficit > 0.5) win.Width = win.ActualWidth + Math.Ceiling(deficit);
            }
            catch { }
        }

        // ── Tick ─────────────────────────────────────────────────────────────
        void Tick(object sender, EventArgs e)
        {
            if (imachine == null) return;

            double now = clock.Elapsed.TotalSeconds, dt = now - lastTick;
            lastTick = now;
            if (dt < 0 || dt > 0.25) dt = 0.25;

            foreach (var a in refreshers) a();
            foreach (var r in rows) r.Refresh();

            bool ok = link.Poll(imachine);
            UpdateMatchStatus(ok ? link.Frame.MatchDb : float.NaN);
            if (ok)
            {
                if (discardNext) { discardNext = false; return; }
                meters.Feed(link.Frame, dt);
            }
            else meters.Feed(null, dt);
        }

        // "A/B matched: master −11.1 dB" while MATCH is on.
        void UpdateMatchStatus(float matchDb)
        {
            string text = "";
            if (IsOn("Match") && !float.IsNaN(matchDb))
                text = Math.Abs(matchDb) < 0.05f ? "A/B matched: equal loudness"
                     : "A/B matched: " + (matchDb > 0 ? "master " : "dry ") +
                       (-Math.Abs(matchDb)).ToString("F1", CultureInfo.InvariantCulture) + " dB";
            if (text != shownStatus) { matchStatus.Text = text; shownStatus = text; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ParamRow — label · fader · value, bound to one global parameter.
    //   drag = move relative to where you grab (a click alone never changes the
    //   value) · Ctrl-drag = fine (1/5 speed) · double-click = default ·
    //   wheel = ±step (Ctrl = ±1)
    // ══════════════════════════════════════════════════════════════════════════
    sealed class ParamRow : FrameworkElement
    {
        const double LabelW = 66, FaderW = 108, ValueGap = 6, RowH = 17, TrackH = 8;

        readonly string label;
        readonly Func<IParameter> param;
        readonly bool bipolar;
        readonly int wheel;
        int shown = int.MinValue;
        string desc = "";
        bool dim, shownDim;

        public bool Dim { get => dim; set => dim = value; }

        public ParamRow(string label, Func<IParameter> param, bool bipolar, int wheel)
        {
            this.label = label; this.param = param; this.bipolar = bipolar; this.wheel = wheel;
            Width = 236; Height = RowH;
            Cursor = Cursors.Hand;
        }

        public void Refresh()
        {
            var p = param();
            if (p == null) return;
            int v;
            try { v = p.GetValue(0); } catch { return; }
            if (v == shown && dim == shownDim) return;
            shown = v; shownDim = dim;
            string d = null;
            try { d = p.DescribeValue(v); } catch { }
            desc = string.IsNullOrEmpty(d) ? v.ToString(CultureInfo.InvariantCulture) : d;
            ToolTip = p.Name + ": " + desc + "\nDrag to change (Ctrl = fine) · double-click to reset · wheel ±" + wheel + " (Ctrl ±1)";
            InvalidateVisual();
        }

        double Frac(IParameter p, int v) =>
            p.MaxValue > p.MinValue ? (double)(v - p.MinValue) / (p.MaxValue - p.MinValue) : 0;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, RowH));   // hit-testable
            var p = param();
            double y = (RowH - TrackH) / 2;

            dc.DrawText(Theme.Text(this, label, 10, dim ? Theme.Faint : Theme.Dim), new Point(0, 2));
            dc.DrawRectangle(Theme.Track, null, new Rect(LabelW, y, FaderW, TrackH));
            if (p == null || shown == int.MinValue) return;

            double x  = LabelW + Frac(p, shown) * FaderW;
            double xd = LabelW + Frac(p, p.DefValue) * FaderW;
            var fill = dim ? Theme.FaderOff : Theme.Fader;
            if (bipolar) dc.DrawRectangle(fill, null, new Rect(Math.Min(x, xd), y, Math.Max(Math.Abs(x - xd), 1), TrackH));
            else         dc.DrawRectangle(fill, null, new Rect(LabelW, y, x - LabelW, TrackH));
            dc.DrawRectangle(Theme.Faint, null, new Rect(Math.Min(xd, LabelW + FaderW - 1), y, 1, TrackH));   // default mark
            dc.DrawRectangle(Theme.Hold, null, new Rect(Math.Max(LabelW, Math.Min(x - 1, LabelW + FaderW - 2)), y - 1, 2, TrackH + 2));

            dc.DrawText(Theme.Text(this, desc, 10, dim ? Theme.Faint : Theme.TextBrush), new Point(LabelW + FaderW + ValueGap, 2));
        }

        double dragX, dragValue;      // grab point and the value there (unrounded)

        void DragTo(double px)
        {
            var p = param();
            if (p == null) return;
            bool fine = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            double perPx = (double)(p.MaxValue - p.MinValue) / FaderW * (fine ? 0.2 : 1.0);
            dragValue += (px - dragX) * perPx;
            dragX = px;
            if (dragValue < p.MinValue) dragValue = p.MinValue;
            if (dragValue > p.MaxValue) dragValue = p.MaxValue;
            Set(p, (int)Math.Round(dragValue));
        }

        void Set(IParameter p, int v)
        {
            if (v < p.MinValue) v = p.MinValue;
            if (v > p.MaxValue) v = p.MaxValue;
            try { if (v != p.GetValue(0)) p.SetValue(0, v); } catch { }
            Refresh();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var p = param();
            if (p == null) return;
            if (e.ClickCount == 2) Set(p, p.DefValue);
            else
            {
                int v;
                try { v = p.GetValue(0); } catch { return; }
                CaptureMouse();
                dragX = e.GetPosition(this).X;
                dragValue = v;
            }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) DragTo(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var p = param();
            if (p == null) return;
            int step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 1 : wheel;
            int v;
            try { v = p.GetValue(0); } catch { return; }
            Set(p, v + (e.Delta > 0 ? step : -step));
            e.Handled = true;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PanelMeters — input / output (true peak) level, gain reduction and a
    // one-line loudness summary, drawn in one OnRender pass.
    //   click the meters   = clear holds and clip lights
    //   click the loudness = restart I and LRA
    // ══════════════════════════════════════════════════════════════════════════
    sealed class PanelMeters : FrameworkElement
    {
        const double LabelW = 48, ReadW = 52, BarH = 8, RowH = 11, LoudH = 16;
        const double LevelTop = 0, GrTop = 4 * RowH + 16, LoudTop = GrTop + RowH + 8;
        const double TotalH = LoudTop + LoudH;

        readonly LevelBallistics inL = new LevelBallistics(), inR = new LevelBallistics();
        readonly LevelBallistics outL = new LevelBallistics(), outR = new LevelBallistics();
        readonly GrBallistics comp = new GrBallistics(), lim = new GrBallistics();
        readonly LoudnessMeter loud = new LoudnessMeter();
        double mLL, mLR, mRR, corr = double.NaN;

        LinearGradientBrush zone;
        double zoneW = -1;

        public PanelMeters()
        {
            Height = TotalH;
            Cursor = Cursors.Hand;
            ToolTip = "OUT and the loudness line measure the master: after the limiter, before Bypass, Match and Dither\n" +
                      "Gain reduction bars run 0 to 24 dB (ticks at 3, 6, 12 dB)\n" +
                      "Click the meters to clear peak holds and clip lights · click the loudness line to restart I and LRA";
        }

        public void Feed(MeterFrame f, double dt)
        {
            if (f == null)
            {
                inL.Update(0, 0, dt); inR.Update(0, 0, dt); outL.Update(0, 0, dt); outR.Update(0, 0, dt);
                comp.Update(0, dt); lim.Update(0, dt);
            }
            else
            {
                inL.Update(f.InPkL, f.InMsL, dt);   inR.Update(f.InPkR, f.InMsR, dt);
                outL.Update(f.OutTpL, f.OutMsL, dt); outR.Update(f.OutTpR, f.OutMsR, dt);
                comp.Update(f.CompGr, dt); lim.Update(f.LimGr, dt);
                for (int i = 0; i < f.BlockCount; i++) loud.Add(f.Blocks[i]);
                double a = 1.0 - Math.Exp(-dt / 0.300);
                mLL += (f.LL - mLL) * a; mLR += (f.LR - mLR) * a; mRR += (f.RR - mRR) * a;
                double den = Math.Sqrt(mLL * mRR);
                corr = den > 1e-9 ? Math.Max(-1, Math.Min(1, mLR / den)) : double.NaN;
            }
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.GetPosition(this).Y >= LoudTop - 4) loud.Reset();
            else { inL.Reset(); inR.Reset(); outL.Reset(); outR.Reset(); comp.Reset(); lim.Reset(); }
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, TotalH));
            double barX = LabelW, barW = W - LabelW - ReadW - 10;
            if (barW < 40) return;

            if (zone == null || zoneW != barW)
            {
                zone = MeterScale.ZoneBrush(new Point(barX, 0), new Point(barX + barW, 0), MeterScale.PanelFrac);
                zoneW = barW;
            }

            // Level rows: IN L/R, OUT L/R (true peak).
            LevelRow(dc, "IN",  "L", inL,  LevelTop,            barX, barW);
            LevelRow(dc, null,  "R", inR,  LevelTop + RowH,     barX, barW);
            LevelRow(dc, "OUT", "L", outL, LevelTop + 2 * RowH + 3, barX, barW);   // true peak
            LevelRow(dc, null,  "R", outR, LevelTop + 3 * RowH + 3, barX, barW);

            // Level scale.
            double ys = LevelTop + 4 * RowH + 4;
            foreach (int db in new[] { -40, -30, -20, -12, -6, -3, 0 })
            {
                var t = Theme.Text(this, db.ToString(CultureInfo.InvariantCulture), 8, Theme.Faint);
                dc.DrawText(t, new Point(barX + MeterScale.PanelFrac(db) * barW - t.Width / 2, ys));
            }
            dc.DrawText(Theme.Text(this, "TP", 8, Theme.Faint), new Point(0, LevelTop + 3 * RowH + 4));

            // Gain reduction: COMP and LIM side by side, 0 .. 24 dB from the left.
            //   [GR COMP][bar][read]  [LIM][bar][read]
            const double grRead = 40, grGap = 12, limLabel = 26;
            double grBar = (W - LabelW - limLabel - grGap - 2 * grRead) / 2;
            GrRow(dc, "GR COMP", 0, comp, GrTop, LabelW, grBar, grRead);
            double x2 = LabelW + grBar + grRead + grGap;
            GrRow(dc, "LIM", x2, lim, GrTop, x2 + limLabel, grBar, grRead);

            // Loudness line.
            string line =
                "M " + Theme.F1(loud.Momentary) + "  S " + Theme.F1(loud.ShortTerm) +
                "  I " + Theme.F1(loud.Integrated) + " LUFS  LRA " +
                (double.IsNaN(loud.Range) ? "--" : loud.Range.ToString("F1", CultureInfo.InvariantCulture)) + " LU  C " +
                (double.IsNaN(corr) ? "--" : (corr >= 0 ? "+" : "") + corr.ToString("F2", CultureInfo.InvariantCulture));
            dc.DrawRectangle(Theme.Panel, null, new Rect(0, LoudTop, W, LoudH));
            dc.DrawText(Theme.Text(this, line, 10, Theme.TextBrush, bold: true), new Point(4, LoudTop + 2));
        }

        void LevelRow(DrawingContext dc, string group, string side, LevelBallistics m, double y, double barX, double barW)
        {
            if (group != null) dc.DrawText(Theme.Text(this, group, 9, Theme.Heading, bold: true), new Point(0, y));
            dc.DrawText(Theme.Text(this, side, 9, Theme.Dim), new Point(LabelW - 12, y));

            double by = y + (RowH - BarH) / 2;
            dc.DrawRectangle(Theme.Track, null, new Rect(barX, by, barW, BarH));
            double w = MeterScale.PanelFrac(m.PeakDb) * barW;
            if (m.PeakDb > LevelBallistics.DB_MIN + 0.1) dc.DrawRectangle(zone, null, new Rect(barX, by, w, BarH));
            if (m.HoldDb > LevelBallistics.DB_MIN + 0.5)
            {
                double xh = barX + MeterScale.PanelFrac(m.HoldDb) * barW;
                dc.DrawRectangle(Theme.Hold, null, new Rect(Math.Max(barX, Math.Min(xh - 1, barX + barW - 2)), by, 2, BarH));
            }
            dc.DrawRectangle(m.Clipped ? Theme.ClipOn : Theme.ClipOff, m.Clipped ? null : Theme.ClipEdge,
                             new Rect(barX + barW + 2.5, by + 0.5, 5, BarH - 1));

            bool silent = m.HoldDb <= LevelBallistics.DB_MIN + 0.5;
            var brush = m.Clipped ? Theme.ClipOn : silent ? Theme.Faint : m.HoldDb > -6 ? Theme.Amber : Theme.TextBrush;
            var t = Theme.Text(this, Theme.Db(m.HoldDb), 10, brush, bold: !silent);
            dc.DrawText(t, new Point(barX + barW + 10 + ReadW - t.Width - 4, y - 1));
        }

        void GrRow(DrawingContext dc, string label, double labelX, GrBallistics g, double y,
                   double barX, double barW, double readW)
        {
            dc.DrawText(Theme.Text(this, label, 9, Theme.Heading, bold: true), new Point(labelX, y));
            if (barW < 20) return;
            double by = y + (RowH - BarH) / 2;
            dc.DrawRectangle(Theme.Track, null, new Rect(barX, by, barW, BarH));
            if (g.Db > 0.05) dc.DrawRectangle(Theme.Gr, null, new Rect(barX, by, MeterScale.GrFrac(g.Db) * barW, BarH));
            if (g.HoldDb > 0.05)
                dc.DrawRectangle(Theme.Hold, null, new Rect(barX + Math.Min(MeterScale.GrFrac(g.HoldDb) * barW, barW - 2), by, 2, BarH));
            foreach (int db in new[] { 3, 6, 12 })      // faint ticks at 3, 6 and 12 dB of reduction
            {
                double tx = Math.Round(barX + MeterScale.GrFrac(db) * barW) + 0.5;
                dc.DrawLine(Theme.Tick, new Point(tx, by), new Point(tx, by + BarH));
            }
            bool active = g.HoldDb > 0.05;
            var t = Theme.Text(this, active ? "-" + g.HoldDb.ToString("F1", CultureInfo.InvariantCulture) : "0.0",
                               10, active ? Theme.Gr : Theme.Faint, bold: active);
            dc.DrawText(t, new Point(barX + barW + readW - t.Width - 2, y - 1));
        }
    }
}
