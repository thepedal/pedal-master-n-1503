// Pedal Master N — loudness maths (no WPF, so it can be unit-tested on its own).
//
// Fed with the machine's 100 ms K-weighted block energies (ITU-R BS.1770):
//   momentary  (M) = mean energy of the last 4 blocks  (400 ms)
//   short-term (S) = mean energy of the last 30 blocks (3 s)
//   integrated (I) = EBU R128 gated mean of the 400 ms blocks since reset:
//                    absolute gate -70 LUFS, relative gate -10 LU
//   loudness range (LRA, EBU Tech 3342) = 95th − 10th percentile of the
//                    short-term values since reset, after an absolute gate of
//                    -70 LUFS and a relative gate 20 LU below their power mean.
// Block energy e = (Σ kL² + Σ kR²) / N with full scale = 1.0; LUFS = -0.691 + 10·log10(e).

using System;
using System.Collections.Generic;

namespace WDE.PedalMasterN
{
    public sealed class LoudnessMeter
    {
        const int ShortBlocks = 30, MomentaryBlocks = 4;
        const double AbsGateLufs = -70.0, RelGateLu = -10.0, LraRelGateLu = -20.0;
        const int LraEvery = 10;                        // recompute LRA once a second

        readonly double[] ring = new double[ShortBlocks];
        int head, count;

        readonly List<double> gated = new List<double>();   // 400 ms energies above -70 LUFS
        double gatedSum;

        readonly List<double> stGated = new List<double>(); // short-term energies above -70 LUFS
        double stSum;
        int sinceLra;
        double[] sortBuf = new double[0];

        public double Momentary  = double.NaN;
        public double ShortTerm  = double.NaN;
        public double Integrated = double.NaN;
        public double Range      = double.NaN;             // LRA, LU
        public double MaxMomentary = double.NaN;           // since reset
        public double MaxShortTerm = double.NaN;

        public static double Lufs(double e) => e > 0 ? -0.691 + 10.0 * Math.Log10(e) : double.NegativeInfinity;
        public static double Energy(double lufs) => Math.Pow(10.0, (lufs + 0.691) / 10.0);

        public void Reset()
        {
            head = count = 0;
            gated.Clear();  gatedSum = 0;
            stGated.Clear(); stSum = 0; sinceLra = 0;
            Momentary = ShortTerm = Integrated = Range = MaxMomentary = MaxShortTerm = double.NaN;
        }

        double MeanOfLast(int k)
        {
            double s = 0;
            for (int i = 1; i <= k; i++) s += ring[(head - i + ShortBlocks) % ShortBlocks];
            return s / k;
        }

        public void Add(double e)
        {
            if (double.IsNaN(e) || e < 0) e = 0;
            ring[head] = e;
            head = (head + 1) % ShortBlocks;
            if (count < ShortBlocks) count++;

            if (count >= MomentaryBlocks)
            {
                double e400 = MeanOfLast(MomentaryBlocks);
                Momentary = Lufs(e400);
                if (!(MaxMomentary >= Momentary)) MaxMomentary = Momentary;
                if (Momentary > AbsGateLufs)
                {
                    gated.Add(e400);
                    gatedSum += e400;
                    UpdateIntegrated();
                }
            }

            if (count >= ShortBlocks)
            {
                double e3 = MeanOfLast(ShortBlocks);
                ShortTerm = Lufs(e3);
                if (!(MaxShortTerm >= ShortTerm)) MaxShortTerm = ShortTerm;
                if (ShortTerm > AbsGateLufs) { stGated.Add(e3); stSum += e3; }
                if (++sinceLra >= LraEvery) { sinceLra = 0; UpdateRange(); }
            }
            else ShortTerm = double.NaN;
        }

        void UpdateIntegrated()
        {
            double relThreshold = Energy(Lufs(gatedSum / gated.Count) + RelGateLu);
            double s = 0; int k = 0;
            foreach (double e in gated)
                if (e > relThreshold) { s += e; k++; }
            Integrated = k > 0 ? Lufs(s / k) : double.NaN;
        }

        // Call after the last block if an up-to-the-block LRA is needed (tests).
        public void UpdateRange()
        {
            int n = stGated.Count;
            if (n < 2) { Range = double.NaN; return; }
            double thr = Energy(Lufs(stSum / n) + LraRelGateLu);
            if (sortBuf.Length < n) sortBuf = new double[Math.Max(n, sortBuf.Length * 2)];
            int k = 0;
            foreach (double e in stGated)
                if (e > thr) sortBuf[k++] = e;
            if (k < 2) { Range = double.NaN; return; }
            Array.Sort(sortBuf, 0, k);
            double lo = Lufs(sortBuf[(int)Math.Round(0.10 * (k - 1))]);
            double hi = Lufs(sortBuf[(int)Math.Round(0.95 * (k - 1))]);
            Range = hi - lo;
        }
    }
}
