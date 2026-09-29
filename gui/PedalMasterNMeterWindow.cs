// Pedal Master N — meter window.
//
// A separate, resizable window opened from the panel's METERS button, one per
// machine. It reads its own meter slot (1), so it never takes peaks from the
// panel (slot 0).
//
//   INPUT L R │ OUTPUT L R (true peak) │ GR COMP LIM
//   RMS solid (300 ms) with the peak translucent above it, 3 s hold lines,
//   clip lights (out 2 s after the last over), held-peak and RMS readouts.
//   Gain reduction bars hang from the top, 0 .. 24 dB.
//
//   Loudness strip: M and S bars on an LU scale around the target,
//   I (EBU R128 gated) with its distance from the target, LRA (EBU Tech 3342),
//   PLR (max true peak − I), max short-term/momentary, and the output
//   correlation. TARGET cycles -23 (broadcast) / -16 / -14 (streaming) LUFS;
//   RESET restarts I, LRA and PLR; "→ T" sets Lim Gain to land I on the target.
//   All output values measure the master (before Bypass, Match and Dither).
//
// Closes with its machine or when the song changes. Redraws on
// CompositionTarget.Rendering (≈ 60 fps) and always unsubscribes on close.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BuzzGUI.Interfaces;

namespace WDE.PedalMasterN
{
    public sealed class MeterWindow : Window
    {
        static readonly Dictionary<IMachine, MeterWindow> open = new Dictionary<IMachine, MeterWindow>();

        public static void ShowFor(IMachine machine)
        {
            if (machine == null) return;
            MeterWindow w;
            if (open.TryGetValue(machine, out w))
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return;
            }
            w = new MeterWindow(machine);
            open[machine] = w;
            w.Show();
        }

        readonly IMachine machine;
        readonly MeterLink link = new MeterLink(1);
        readonly MasterBridge bridge = new MasterBridge();
        readonly LoudnessView loudness = new LoudnessView();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTick;
        bool running, everOk, discardNext = true;
        int failures;
        IBuzz buzz;

        MeterWindow(IMachine machine)
        {
            this.machine = machine;
            Title         = MachineName() + " — Meters";
            Width         = 400;
            Height        = 540;
            MinWidth      = 330;
            MinHeight     = 380;
            ShowInTaskbar = false;
            Background    = Theme.Background;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Owned by Buzz's (native) main window: stays above it, minimises with it.
            try
            {
                var main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (main != IntPtr.Zero) new WindowInteropHelper(this).Owner = main;
            }
            catch { }

            // ── Toolbar ──
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 2) };
            bar.Children.Add(MakeButton("CLEAR", "Clear all peak holds and clip lights", () => bridge.ResetAll()));
            bar.Children.Add(new TextBlock
            {
                Text = "solid = RMS (300 ms), translucent = peak\noutput = the master, true peak\n(before Bypass, Match and Dither)",
                Foreground = Theme.Dim, FontFamily = Theme.MonoFamily, FontSize = 9, LineHeight = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            });

            // ── Loudness strip ──
            TextBlock targetText = null;
            var targetBtn = MakeButton(TargetLabel(loudness.Target),
                "Loudness target: -23 LUFS (EBU R128 broadcast), -16, or -14 LUFS (streaming)", () =>
                {
                    loudness.Target = loudness.Target == -23 ? -16 : loudness.Target == -16 ? -14 : -23;
                    LoudnessView.LastTarget = loudness.Target;
                    loudness.InvalidateVisual();
                });
            targetText = (TextBlock)targetBtn.Child;
            loudness.TargetChanged = () => targetText.Text = TargetLabel(loudness.Target);
            var resetBtn = MakeButton("RESET", "Restart the integrated loudness (I), LRA, PLR and the maxima",
                                      () => { loudness.Meter.Reset(); loudness.ResetPeak(); loudness.InvalidateVisual(); });
            resetBtn.Margin = new Thickness(0, 6, 0, 0);

            var toTarget = MakeButton("→ T",
                "Set the limiter's Gain so the integrated loudness (I) lands on the target.\n" +
                "Play the song (or a representative part) first. The measurement restarts after the change;\n" +
                "play it again to check. Heavy limiting can keep it a little under the target: press again.",
                SetGainToTarget);
            toTarget.Margin = new Thickness(0, 6, 0, 0);

            var loudButtons = new StackPanel { Margin = new Thickness(8, 8, 4, 8), Width = 62 };
            loudButtons.Children.Add(targetBtn);
            loudButtons.Children.Add(resetBtn);
            loudButtons.Children.Add(toTarget);

            var strip = new DockPanel { Height = 124, Background = Theme.Panel };
            DockPanel.SetDock(loudButtons, Dock.Left);
            strip.Children.Add(loudButtons);
            strip.Children.Add(loudness);

            var dock = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            DockPanel.SetDock(strip, Dock.Bottom);
            dock.Children.Add(strip);
            dock.Children.Add(bridge);
            Content = dock;

            CompositionTarget.Rendering += OnFrame;
            running = true;

            try
            {
                if (machine.Graph != null)
                {
                    machine.Graph.MachineRemoved += OnMachineRemoved;
                    buzz = machine.Graph.Buzz;
                    if (buzz != null) buzz.PropertyChanged += OnBuzzPropertyChanged;
                }
            }
            catch { }

            Closed += (_, __) =>
            {
                Stop();
                open.Remove(machine);
                try
                {
                    if (machine.Graph != null) machine.Graph.MachineRemoved -= OnMachineRemoved;
                    if (buzz != null) buzz.PropertyChanged -= OnBuzzPropertyChanged;
                }
                catch { }
            };
        }

        // "→ T": move Lim Gain by (target − I). The limiter holds the peaks, so the
        // loudness follows the gain almost 1:1 until it limits hard.
        void SetGainToTarget()
        {
            double I = loudness.Meter.Integrated;
            if (double.IsNaN(I)) { loudness.Flash("Play some music first: no integrated loudness yet"); return; }

            IParameter gain = null, limiter = null;
            try
            {
                foreach (var g in machine.ParameterGroups)
                    if (g?.Parameters != null && g.Type == ParameterGroupType.Global)
                        foreach (var p in g.Parameters)
                        {
                            if (p?.Name == "Lim Gain") gain = p;
                            if (p?.Name == "Limiter")  limiter = p;
                        }
            }
            catch { }
            if (gain == null) return;
            if (limiter != null && limiter.GetValue(0) == 0)
            {
                loudness.Flash("The limiter is off: switch it on first (it keeps the peaks under the ceiling)");
                return;
            }

            double delta = loudness.Target - I;
            int cur = gain.GetValue(0);
            int want = cur + (int)Math.Round(delta * 10.0);          // Lim Gain: 0.1 dB steps
            int set = Math.Max(gain.MinValue, Math.Min(gain.MaxValue, want));
            if (set != cur) gain.SetValue(0, set);
            loudness.Meter.Reset();
            loudness.ResetPeak();

            string now = "Lim Gain " + (set / 10.0).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " dB";
            loudness.Flash(set != want
                ? now + " (at its limit): lower the target or add gain elsewhere"
                : now + " (" + delta.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "): play again to check");
            loudness.InvalidateVisual();
        }

        static string TargetLabel(double t) => "T " + t.ToString("F0", CultureInfo.InvariantCulture);

        string MachineName()
        {
            try { return string.IsNullOrEmpty(machine.Name) ? "Pedal Master N" : machine.Name; }
            catch { return "Pedal Master N"; }
        }

        void Stop()
        {
            if (!running) return;
            CompositionTarget.Rendering -= OnFrame;    // static event: must unsubscribe
            running = false;
        }

        void OnFrame(object sender, EventArgs e)
        {
            if (clock.Elapsed.TotalSeconds - lastTick < 1.0 / 62) return;
            Poll();
        }

        void OnMachineRemoved(IMachine m)
        {
            if (ReferenceEquals(m, machine)) { Stop(); Close(); }
        }

        void OnBuzzPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Song") { Stop(); Close(); }
        }

        void Poll()
        {
            double now = clock.Elapsed.TotalSeconds, dt = now - lastTick;
            lastTick = now;
            if (dt < 0 || dt > 0.25) dt = 0.25;

            if (!link.Poll(machine))
            {
                if (everOk)
                {
                    if (++failures > 60) { Stop(); Close(); }     // machine gone: give it ~1 s
                    bridge.Feed(null, dt);
                }
                else
                {
                    bridge.Notice = "No meter data from the Pedal Master N machine.\nRebuild and deploy the native machine DLL.";
                    bridge.InvalidateVisual();
                }
                return;
            }

            everOk = true; failures = 0; bridge.Notice = null;
            if (discardNext) { discardNext = false; return; }

            var f = link.Frame;
            bridge.Feed(f, dt);
            for (int i = 0; i < f.BlockCount; i++) loudness.Meter.Add(f.Blocks[i]);
            loudness.AddTruePeak(Math.Max(f.OutTpL, f.OutTpR));
            loudness.UpdateCorrelation(f.LL, f.LR, f.RR, dt);
            loudness.InvalidateVisual();
        }

        static Border MakeButton(string text, string tooltip, Action onClick)
        {
            var b = new Border
            {
                MinWidth = 48, Height = 18, Padding = new Thickness(6, 0, 6, 0),
                Background = Theme.ButtonBg, BorderBrush = Theme.ButtonEdge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Cursor = Cursors.Hand, ToolTip = tooltip,
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
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MasterBridge — INPUT L/R, OUTPUT L/R (true peak), GR COMP/LIM in one pass.
    // ══════════════════════════════════════════════════════════════════════════
    sealed class MasterBridge : FrameworkElement
    {
        public string Notice;

        readonly LevelBallistics[] lv = { new LevelBallistics(), new LevelBallistics(), new LevelBallistics(), new LevelBallistics() };
        readonly GrBallistics comp = new GrBallistics(), lim = new GrBallistics();

        static readonly int[] TickDb = { 0, -3, -6, -9, -12, -18, -24, -30, -40, -50, -60 };
        static readonly int[] GrTicks = { 0, 3, 6, 12, 18, 24 };

        LinearGradientBrush zone, zoneDim;
        double zTop = -1, zBottom = -1;

        public MasterBridge()
        {
            ClipToBounds = true;
            Cursor = Cursors.Hand;
            ToolTip = "Click to clear peak holds and clip lights";
            MouseLeftButtonDown += (_, e) => { ResetAll(); e.Handled = true; };
        }

        public void ResetAll()
        {
            foreach (var m in lv) m.Reset();
            comp.Reset(); lim.Reset();
            InvalidateVisual();
        }

        public void Feed(MeterFrame f, double dt)
        {
            if (f == null)
            {
                foreach (var m in lv) m.Update(0, 0, dt);
                comp.Update(0, dt); lim.Update(0, dt);
            }
            else
            {
                lv[0].Update(f.InPkL, f.InMsL, dt);   lv[1].Update(f.InPkR, f.InMsR, dt);
                lv[2].Update(f.OutTpL, f.OutMsL, dt); lv[3].Update(f.OutTpR, f.OutMsR, dt);
                comp.Update(f.CompGr, dt); lim.Update(f.LimGr, dt);
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));
            if (Notice != null) { dc.DrawText(Theme.Text(this, Notice, 12, Theme.TextBrush), new Point(12, 12)); return; }

            const double pad = 8, scaleW = 34, gap = 18, grScaleW = 22;
            const double captionH = 12, readH = 28, clipH = 7, labelH = 15;

            // 6 columns: in L, in R | out L, out R | comp, lim
            double avail = W - 2 * pad - scaleW - 2 * gap - grScaleW;
            double colW  = Math.Max(14, Math.Min(64, avail / 6));
            double barW  = Math.Max(8, colW - 6);

            double top = pad + captionH + readH + clipH + 3, bottom = H - pad - labelH;
            if (bottom - top < 40) return;
            double hgt = bottom - top;
            double y0 = bottom - MeterScale.Frac(0) * hgt;

            if (zone == null || zTop != top || zBottom != bottom)
            {
                zone    = MeterScale.ZoneBrush(new Point(0, bottom), new Point(0, top), MeterScale.Frac, 255);
                zoneDim = MeterScale.ZoneBrush(new Point(0, bottom), new Point(0, top), MeterScale.Frac, 110);
                zTop = top; zBottom = bottom;
            }

            double inLeft  = pad + scaleW;
            double outLeft = inLeft + 2 * colW + gap;
            double grLeft  = outLeft + 2 * colW + gap;
            Func<int, double> colX = c =>
                (c < 2 ? inLeft + c * colW : c < 4 ? outLeft + (c - 2) * colW : grLeft + (c - 4) * colW) + (colW - barW) / 2;

            // Section shading, dividers and captions.
            dc.DrawRectangle(Theme.Panel, null, new Rect(outLeft - 4, pad, 2 * colW + 8, H - 2 * pad));
            foreach (double dx in new[] { outLeft - gap / 2, grLeft - gap / 2 })
            {
                double x = Math.Round(dx) + 0.5;
                dc.DrawLine(Theme.Divider, new Point(x, pad), new Point(x, H - pad));
            }
            Caption(dc, "INPUT",  inLeft  + colW, pad);
            Caption(dc, "OUTPUT", outLeft + colW, pad);
            Caption(dc, "GAIN RED.", grLeft + colW, pad);

            // Level meters.
            for (int c = 0; c < 4; c++)
            {
                var m = lv[c];
                double x = colX(c);
                dc.DrawRectangle(Theme.Track, null, new Rect(x, top, barW, hgt));
                dc.DrawRectangle(Theme.HeadroomTint, null, new Rect(x, top, barW, Math.Max(0, y0 - top)));
                double yPk = bottom - MeterScale.Frac(m.PeakDb) * hgt, yRms = bottom - MeterScale.Frac(m.RmsDb) * hgt;
                if (m.PeakDb > LevelBallistics.DB_MIN + 0.1) dc.DrawRectangle(zoneDim, null, new Rect(x, yPk,  barW, bottom - yPk));
                if (m.RmsDb  > LevelBallistics.DB_MIN + 0.1) dc.DrawRectangle(zone,    null, new Rect(x, yRms, barW, bottom - yRms));
            }

            // Level scale ticks over the level bars; labels at the left.
            double levelRight = outLeft + 2 * colW;
            foreach (int t in TickDb)
            {
                double y = Math.Round(bottom - MeterScale.Frac(t) * hgt) + 0.5;
                dc.DrawLine(t == 0 ? Theme.Zero : Theme.Tick, new Point(inLeft - 4, y), new Point(levelRight, y));
                if (t == -50) continue;
                var ft = Theme.Text(this, t.ToString(CultureInfo.InvariantCulture), 10, Theme.Dim);
                dc.DrawText(ft, new Point(inLeft - 7 - ft.Width, y - ft.Height / 2));
            }

            // GR meters: hang from the top, 0 .. 24 dB, linear.
            GrBallistics[] gr = { comp, lim };
            for (int k = 0; k < 2; k++)
            {
                double x = colX(4 + k);
                dc.DrawRectangle(Theme.Track, null, new Rect(x, top, barW, hgt));
                if (gr[k].Db > 0.05) dc.DrawRectangle(Theme.Gr, null, new Rect(x, top, barW, MeterScale.GrFrac(gr[k].Db) * hgt));
                if (gr[k].HoldDb > 0.05)
                    dc.DrawRectangle(Theme.Hold, null, new Rect(x, top + MeterScale.GrFrac(gr[k].HoldDb) * hgt - 1, barW, 2));
            }
            double grRight = grLeft + 2 * colW;
            foreach (int t in GrTicks)
            {
                double y = Math.Round(top + MeterScale.GrFrac(t) * hgt) + 0.5;
                dc.DrawLine(Theme.Tick, new Point(grLeft, y), new Point(grRight + 2, y));
                var ft = Theme.Text(this, t == 0 ? "0" : "-" + t, 9, Theme.Dim);
                dc.DrawText(ft, new Point(grRight + 4, y - ft.Height / 2));
            }

            bool narrow = colW < 34;
            double fontPk = narrow ? 8.5 : 10.5, fontRms = narrow ? 7.5 : 9;
            string[] labels = { "L", "R", "L", "R", "COMP", "LIM" };

            for (int c = 0; c < 6; c++)
            {
                double x = colX(c), cx = x + barW / 2, ry = pad + captionH;
                if (c < 4)
                {
                    var m = lv[c];
                    if (m.HoldDb > LevelBallistics.DB_MIN + 0.5)
                    {
                        double yH = Math.Round(bottom - MeterScale.Frac(m.HoldDb) * hgt);
                        dc.DrawRectangle(Theme.Hold, null, new Rect(x, yH - 1, barW, 2));
                    }
                    var clip = new Rect(x + 0.5, top - clipH - 2 + 0.5, barW - 1, clipH - 1);
                    if (m.Clipped) dc.DrawRectangle(Theme.ClipOn, null, clip);
                    else           dc.DrawRectangle(Theme.ClipOff, Theme.ClipEdge, clip);

                    bool silent = m.HoldDb <= LevelBallistics.DB_MIN + 0.5;
                    var pb = m.Clipped ? Theme.ClipOn : silent ? Theme.Faint : m.HoldDb > -6 ? Theme.Amber : Theme.TextBrush;
                    var pt = Theme.Text(this, Theme.Db(m.HoldDb), fontPk, pb, bold: !silent);
                    dc.DrawText(pt, new Point(cx - pt.Width / 2, ry));
                    if (m.RmsDb > LevelBallistics.DB_MIN + 0.5)
                    {
                        var rt = Theme.Text(this, Theme.Db(m.RmsDb), fontRms, Theme.Dim);
                        dc.DrawText(rt, new Point(cx - rt.Width / 2, ry + readH / 2 + 1));
                    }
                }
                else
                {
                    var g = gr[c - 4];
                    bool active = g.HoldDb > 0.05;
                    var pt = Theme.Text(this, active ? "-" + g.HoldDb.ToString("F1", CultureInfo.InvariantCulture) : "0.0",
                                        fontPk, active ? Theme.Gr : Theme.Faint, bold: active);
                    dc.DrawText(pt, new Point(cx - pt.Width / 2, ry));
                    if (g.Db > 0.05)
                    {
                        var rt = Theme.Text(this, "-" + g.Db.ToString("F1", CultureInfo.InvariantCulture), fontRms, Theme.Dim);
                        dc.DrawText(rt, new Point(cx - rt.Width / 2, ry + readH / 2 + 1));
                    }
                }
                var lt = Theme.Text(this, labels[c], narrow && c >= 4 ? 8 : 10, c >= 2 && c < 4 ? Theme.TextBrush : Theme.Dim, bold: c >= 2 && c < 4);
                dc.DrawText(lt, new Point(cx - lt.Width / 2, bottom + 3));
            }
        }

        void Caption(DrawingContext dc, string text, double cx, double y)
        {
            var t = Theme.Text(this, text, 9, Theme.Dim, bold: true);
            dc.DrawText(t, new Point(cx - t.Width / 2, y));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LoudnessView — M / S bars around the target, I with its distance from the
    // target, LRA, max short-term, and the correlation meter.
    // ══════════════════════════════════════════════════════════════════════════
    sealed class LoudnessView : FrameworkElement
    {
        public static double LastTarget = -23;      // remembered between windows

        public readonly LoudnessMeter Meter = new LoudnessMeter();
        double target = LastTarget;
        public Action TargetChanged;
        public double Target
        {
            get => target;
            set { target = value; TargetChanged?.Invoke(); }
        }

        // Max true peak since reset (linear, 1.0 = 0 dBTP) → PLR = peak − I.
        double maxTp;
        public void AddTruePeak(float tp) { if (tp > maxTp) maxTp = tp; }
        public void ResetPeak() { maxTp = 0; }

        // One-line message under the strip for a few seconds (target assist).
        string flash; DateTime flashUntil;
        public void Flash(string text) { flash = text; flashUntil = DateTime.UtcNow.AddSeconds(6); InvalidateVisual(); }

        const double CORR_TAU = 0.300;
        double mLL, mLR, mRR;
        public double Correlation = double.NaN;

        public void UpdateCorrelation(double ll, double lr, double rr, double dt)
        {
            double a = 1.0 - Math.Exp(-dt / CORR_TAU);
            mLL += (ll - mLL) * a; mLR += (lr - mLR) * a; mRR += (rr - mRR) * a;
            double den = Math.Sqrt(mLL * mRR);
            Correlation = den > 1e-9 ? Math.Max(-1, Math.Min(1, mLR / den)) : double.NaN;
        }

        const double LU_BELOW = 20, LU_ABOVE = 10;

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth;
            const double labelW = 16, valueW = 58, rowH = 11;
            double x0 = labelW + 4, x1 = W - valueW - 6;
            if (x1 - x0 < 40) return;
            double span = x1 - x0;
            Func<double, double> luX = lufs =>
                x0 + Math.Max(0, Math.Min(1, (lufs - (target - LU_BELOW)) / (LU_BELOW + LU_ABOVE))) * span;
            double xT = luX(target);

            double yM = 8, yS = yM + rowH + 4, yScale = yS + rowH + 2, yI = yScale + 12, yR = yI + 19, yC = yR + 17;

            Bar(dc, "M", Meter.Momentary, yM, rowH, x0, span, luX, xT, W, valueW);
            Bar(dc, "S", Meter.ShortTerm, yS, rowH, x0, span, luX, xT, W, valueW);

            foreach (int lu in new[] { -20, -10, 0, 10 })
            {
                double x = luX(target + lu);
                var t = Theme.Text(this, lu == 0 ? "0 LU" : (lu > 0 ? "+" : "") + lu, 8, Theme.Dim);
                dc.DrawText(t, new Point(Math.Max(x0, Math.Min(x1 - t.Width, x - t.Width / 2)), yScale));
            }

            // I and its distance from the target.
            dc.DrawText(Theme.Text(this, "I", 10, Theme.Dim, bold: true), new Point(4, yI + 2));
            var it = Theme.Text(this, double.IsNaN(Meter.Integrated) ? "--  LUFS" : Theme.F1(Meter.Integrated) + " LUFS",
                                14, Theme.TextBrush, bold: true);
            dc.DrawText(it, new Point(x0, yI - 1));
            string rel = double.IsNaN(Meter.Integrated) ? "target " + Theme.F1(target) + " LUFS"
                       : Theme.F1(Meter.Integrated - target) + " LU re " + Theme.F1(target);
            dc.DrawText(Theme.Text(this, rel, 9, Theme.Dim), new Point(x0 + it.Width + 10, yI + 3));

            // LRA and max short-term.
            string lra = "LRA " + (double.IsNaN(Meter.Range) ? "--" : Meter.Range.ToString("F1", CultureInfo.InvariantCulture)) + " LU";
            var lt = Theme.Text(this, lra, 11, Theme.TextBrush, bold: true);
            dc.DrawText(lt, new Point(x0, yR));
            double plr = maxTp > 1e-6 && !double.IsNaN(Meter.Integrated) ? 20 * Math.Log10(maxTp) - Meter.Integrated : double.NaN;
            dc.DrawText(Theme.Text(this, "PLR " + (double.IsNaN(plr) ? "--" : plr.ToString("F1", CultureInfo.InvariantCulture)) +
                                         "  max S " + Theme.F1(Meter.MaxShortTerm) + "  max M " + Theme.F1(Meter.MaxMomentary), 9, Theme.Dim),
                        new Point(x0 + lt.Width + 12, yR + 2));

            // Correlation −1 … +1.
            dc.DrawText(Theme.Text(this, "C", 10, Theme.Dim, bold: true), new Point(4, yC - 1));
            dc.DrawRectangle(Theme.Track, null, new Rect(x0, yC, span, rowH));
            double xc = x0 + span / 2;
            if (!double.IsNaN(Correlation))
            {
                double xr = x0 + (Correlation + 1) / 2 * span;
                dc.DrawRectangle(Correlation >= 0 ? Theme.CorrPos : Theme.CorrNeg, null,
                                 new Rect(Math.Min(xc, xr), yC, Math.Abs(xr - xc), rowH));
                dc.DrawRectangle(Theme.Hold, null, new Rect(xr - 1, yC, 2, rowH));
            }
            dc.DrawLine(Theme.Divider, new Point(Math.Round(xc) + 0.5, yC - 2), new Point(Math.Round(xc) + 0.5, yC + rowH + 2));
            var cv = Theme.Text(this, double.IsNaN(Correlation) ? "--"
                                : (Correlation >= 0 ? "+" : "") + Correlation.ToString("F2", CultureInfo.InvariantCulture),
                                10, Theme.TextBrush, bold: true);
            dc.DrawText(cv, new Point(W - valueW, yC - 1));

            if (flash != null && DateTime.UtcNow < flashUntil)
                dc.DrawText(Theme.Text(this, flash, 9, Theme.Amber), new Point(4, yC + rowH + 12));
        }

        void Bar(DrawingContext dc, string label, double lufs, double y, double h,
                 double x0, double span, Func<double, double> luX, double xT, double W, double valueW)
        {
            dc.DrawText(Theme.Text(this, label, 10, Theme.Dim, bold: true), new Point(4, y - 1));
            dc.DrawRectangle(Theme.Track, null, new Rect(x0, y, span, h));
            if (!double.IsNaN(lufs) && !double.IsInfinity(lufs))
            {
                double x = luX(lufs);
                dc.DrawRectangle(Theme.Under, null, new Rect(x0, y, Math.Max(0, Math.Min(x, xT) - x0), h));
                if (lufs > target) dc.DrawRectangle(Theme.Over, null, new Rect(xT, y, x - xT, h));
            }
            dc.DrawRectangle(Theme.Hold, null, new Rect(xT - 1, y - 2, 2, h + 4));
            dc.DrawText(Theme.Text(this, Theme.F1(lufs), 10, Theme.TextBrush, bold: true), new Point(W - valueW, y - 1));
        }
    }
}
