// Pedal Master N — native (C++) Buzz 1503 mastering machine, 32-bit
//
// Stereo in → stereo out, meant to sit last in the chain (before Master).
// Built on the Pedal Gain Multi N v1.7.0 code base: same dB/glide conventions,
// the same BS.1770 true-peak interpolator and K-weighting, and the same
// slot-based meter link to a companion WPF GUI ("Pedal Master N.GUI.dll").
//
// Signal chain (every section can be switched out click-free):
//
//   In → Input trim → EQ (Low Cut, Low Shelf, High Shelf, Tilt)
//      → Glue compressor (stereo-linked, SC high-pass, parallel Mix, Makeup)
//      → Stereo (Low Mono, Width — M/S)
//      → True-peak limiter (Gain, Ceiling in dBTP, Release, 2 ms look-ahead)
//      → Out
//
//   Bypass crossfades to the dry input, delayed by the machine's latency so the
//   A/B comparison stays sample-aligned. Match (A/B) turns whichever of the two
//   is louder down to the other's loudness, so the comparison is about sound,
//   not level. Dither (TPDF, optional noise shaping) is the very last stage.
//
// Latency: the limiter's look-ahead (2 ms) plus the true-peak detector's
// interpolation delay (15 samples). It is constant for a given sample rate —
// also when the limiter is switched off — and reported via GetLatency() for
// hosts with delay compensation.
//
// v0.1.0 — first version (engine + meter link).
// v0.2.0 — companion GUI. Parameter window: switches read On/Off, Comp Mix reads
//          plain %, and the shelf ranges are re-centred so their defaults land
//          exactly on 100 Hz and 10 kHz (frequencies show 3 significant figures).
// v0.3.0 — level-matched A/B (Match), dither for the final render, Low Cut
//          10..250 Hz with a 12/24 dB/oct Slope, an always-on 5 Hz DC blocker,
//          and the output meters/loudness measure the master before
//          Bypass/Match/Dither (GUI protocol v2 adds the match gain).
// v0.3.1 — dither output clamped to the 16/24-bit range (Ceiling 0.0 dBTP + dither
//          could reach +32768). Speed: the true-peak interpolators use mirrored
//          histories and SSE dot products and skip the trivial phase 0; the
//          compressor's gain maths is skipped while it is fully off. Output is
//          unchanged (differences at float rounding, below -125 dBFS).

#include <math.h>
#include <stdio.h>
#include <atomic>
#include "MachineInterface.h"

#if defined(_M_IX86) || defined(_M_X64) || defined(__SSE__)
#include <xmmintrin.h>
#define PMN_HAVE_SSE 1
#endif

static float const FULL_SCALE = 32768.0f;         // Buzz ±32768 → 1.0 = 0 dBFS
static float const SMOOTH_SECONDS = 0.010f;       // parameter glides, ~10 ms
static byte  const SAVE_VERSION = 2;

static double const PI_D = 3.14159265358979323846;

// ── Parameter encodings ────────────────────────────────────────────────────
// Input      : word 0..480, 240 = 0 dB, 0.1 dB steps (-24 .. +24 dB)
// Low Cut    : byte 0 = Off, 1..100 → 10..250 Hz (log); Slope 12 or 24 dB/oct
// Low Freq   : byte 0..100 → 25..400 Hz (log), 50 = 100 Hz      Low shelf
// Low Gain   : byte 0..240, 120 = 0 dB, 0.1 dB steps (±12 dB)
// High Freq  : byte 0..100 → 1.25..20 kHz (log), 75 = 10 kHz   High shelf
// High Gain  : byte 0..240, 120 = 0 dB
// Tilt       : byte 0..120, 60 = 0 dB, 0.1 dB steps (±6 dB, pivot 1 kHz)
// Threshold  : byte 0..80 → -40..0 dB, 0.5 dB steps
// Ratio      : byte 0..5 → 1.5 2 3 4 6 10 :1
// Attack     : byte 0..5 → 0.1 0.3 1 3 10 30 ms
// Release    : byte 0..4 → 0.1 0.3 0.6 1.2 s, Auto
// SC HPF     : byte 0..4 → Off 60 90 120 150 Hz
// Makeup     : byte 0..200 → 0..+20 dB, 0.1 dB steps
// Comp Mix   : byte 0..100 %
// Low Mono   : byte 0 = Off, 1..100 → 40..300 Hz (log), LR4 crossover
// Width      : byte 0..200 %, 100 = unchanged
// Lim Gain   : word 0..240 → 0..+24 dB, 0.1 dB steps
// Ceiling    : byte 0..60 → -6.0..0.0 dBTP, 0.1 dB steps
// Lim Release: byte 0..100 → 10..1000 ms (log)
// Slope      : byte 0..1 → 12, 24 dB/oct (Low Cut)              ┐ appended in v0.3 so
// Match      : switch — level-matched A/B                       │ songs saved with
// Dither     : byte 0..3 → Off, 24-bit, 16-bit, 16-bit shaped   ┘ v0.2 still load

static float const RATIOS[6]      = { 1.5f, 2.0f, 3.0f, 4.0f, 6.0f, 10.0f };
static float const ATTACKS_MS[6]  = { 0.1f, 0.3f, 1.0f, 3.0f, 10.0f, 30.0f };
static float const RELEASES_MS[4] = { 100.0f, 300.0f, 600.0f, 1200.0f };
static int   const REL_AUTO = 4;
static float const SC_HPF_HZ[5]   = { 0.0f, 60.0f, 90.0f, 120.0f, 150.0f };

static double LogMap(int v, int vmin, int vmax, double lo, double hi)
{
    double t = (double)(v - vmin) / (vmax - vmin);
    return lo * pow(hi / lo, t);
}

static double LowCutHz   (int v) { return LogMap(v, 1, 100, 10.0, 250.0); }
static double LowShelfHz (int v) { return LogMap(v, 0, 100, 25.0, 400.0); }
static double HighShelfHz(int v) { return LogMap(v, 0, 100, 1250.0, 20000.0); }
static double LowMonoHz  (int v) { return LogMap(v, 1, 100, 40.0, 300.0); }
static double LimRelMs   (int v) { return LogMap(v, 0, 100, 10.0, 1000.0); }

static float InputDb   (int v) { return (v - 240) * 0.1f; }
static float ShelfDb   (int v) { return (v - 120) * 0.1f; }
static float TiltDb    (int v) { return (v - 60)  * 0.1f; }
static float ThreshDb  (int v) { return -40.0f + v * 0.5f; }
static float MakeupDb  (int v) { return v * 0.1f; }
static float LimGainDb (int v) { return v * 0.1f; }
static float CeilingDb (int v) { return (v - 60) * 0.1f; }

static inline float DbToLin(float db) { return powf(10.0f, db / 20.0f); }

// ── GUI message protocol (shared with the GUI) ─────────────────────────────
// Request : int32 GUIMSG_GET_METERS, int32 slot (0 = panel, 1..3 = meter windows)
// Reply   : int32 version (1), int32 sampleRate, int32 latencySamples,
//           float inPeakL, inPeakR,      // input sample peak      (1.0 = 0 dBFS)
//           float inMsL,   inMsR,        // input mean square      (1.0 = 0 dBFS)
//           float outTpL,  outTpR,       // output true peak (4x)
//           float outMsL,  outMsR,       // output mean square
//           float compGrDb, limGrDb,     // max gain reduction (dB, >= 0)
//           float matchDb,               // v2: master loudness minus input loudness (LU)
//           int32 B, int32 dropped, float blockEnergy[B],  // output K-weighted 100 ms blocks
//           float meanLL, meanLR, meanRR                   // output correlation sums
// Every value covers the time since that slot was last read; reading resets it.
// The output values measure the master: after the limiter, before Bypass, Match
// and Dither, so loudness and the target assist don't move while comparing.
static int const GUIMSG_GET_METERS = 1;
static int const GUI_PROTOCOL      = 2;
static int const METER_SLOTS       = 4;
static int const LOUD_BLOCK_CAP    = 64;

// ── True-peak interpolator (identical to Pedal Gain Multi N) ───────────────
static int const TP_PHASES = 4;
static int const TP_TAPS   = 12;
static int const TP_HIST   = TP_TAPS - 1;
static float g_tpCoef[TP_PHASES][TP_TAPS];

static void InitTruePeakFilter()
{
    static bool done = false;
    if (done) return;
    for (int ph = 0; ph < TP_PHASES; ph++)
    {
        double f = (double)ph / TP_PHASES, sum = 0, c[TP_TAPS];
        for (int j = 0; j < TP_TAPS; j++)
        {
            double d = (TP_TAPS / 2) - j - f;
            double sinc = fabs(d) < 1e-12 ? 1.0 : sin(PI_D * d) / (PI_D * d);
            double w = fabs(d) >= TP_TAPS / 2 ? 0.0
                     : 0.42 + 0.5 * cos(PI_D * d / (TP_TAPS / 2)) + 0.08 * cos(2 * PI_D * d / (TP_TAPS / 2));
            c[j] = sinc * w;
            sum += c[j];
        }
        for (int j = 0; j < TP_TAPS; j++)
            g_tpCoef[ph][j] = (float)(c[j] / sum);
    }
    done = true;
}

// ── Limiter detector interpolator ─────────────────────────────────────────
// Stricter than the meter: DET_PHASES x oversampling with a Kaiser-windowed
// sinc of DET_TAPS taps, accurate much closer to Nyquist, so broadband material
// (cymbals, noise, distortion) doesn't slip inter-sample peaks past the ceiling.
#ifndef DET_PHASES
#define DET_PHASES 8
#endif
#ifndef DET_TAPS
#define DET_TAPS 32
#endif
#ifndef DET_BETA
#define DET_BETA 8.0
#endif
static int const DET_HALF = DET_TAPS / 2;
static float g_detCoef[DET_PHASES][DET_TAPS];

static double BesselI0(double x)
{
    double sum = 1, term = 1;
    for (int k = 1; k < 40; k++) { term *= (x / (2 * k)) * (x / (2 * k)); sum += term; }
    return sum;
}

static void InitDetectorFilter()
{
    static bool done = false;
    if (done) return;
    double i0b = BesselI0(DET_BETA);
    for (int ph = 0; ph < DET_PHASES; ph++)
    {
        double f = (double)ph / DET_PHASES, sum = 0, c[DET_TAPS];
        for (int j = 0; j < DET_TAPS; j++)
        {
            double d = DET_HALF - j - f;                  // distance from the interpolated point
            double sinc = fabs(d) < 1e-12 ? 1.0 : sin(PI_D * d) / (PI_D * d);
            double r = d / DET_HALF;
            double w = fabs(r) >= 1.0 ? 0.0 : BesselI0(DET_BETA * sqrt(1.0 - r * r)) / i0b;
            c[j] = sinc * w;
            sum += c[j];
        }
        for (int j = 0; j < DET_TAPS; j++)
            g_detCoef[ph][j] = (float)(c[j] / sum);
    }
    done = true;
}

// ── Interpolated peak search (shared by the limiter detector and the meter) ──
// Each channel keeps a mirrored history: every sample is written twice, T apart,
// so the last T samples are always contiguous (oldest first). The coefficients
// are stored reversed to match, and each phase is one contiguous dot product
// (SSE: four multiplies at a time). Phase 0 falls exactly on a sample, so its
// value is that sample: no arithmetic needed.
template <int T> struct Mirror
{
    float b[2][2 * T];
    int   pos = 0;
    void Reset() { for (int c = 0; c < 2; c++) for (int i = 0; i < 2 * T; i++) b[c][i] = 0.0f; pos = 0; }
    inline void Push(float l, float r)
    {
        pos = pos + 1 == T ? 0 : pos + 1;
        b[0][pos] = b[0][pos + T] = l;
        b[1][pos] = b[1][pos + T] = r;
    }
    inline float const *Win(int c) const { return b[c] + pos + 1; }   // [0] oldest … [T-1] newest
};

template <int T> static inline float Dot(float const *a, float const *w)
{
#ifdef PMN_HAVE_SSE
    __m128 acc = _mm_setzero_ps();
    for (int k = 0; k < T; k += 4)
        acc = _mm_add_ps(acc, _mm_mul_ps(_mm_loadu_ps(a + k), _mm_loadu_ps(w + k)));
    __m128 hi = _mm_movehl_ps(acc, acc);
    acc = _mm_add_ps(acc, hi);
    acc = _mm_add_ss(acc, _mm_shuffle_ps(acc, acc, 1));
    return _mm_cvtss_f32(acc);
#else
    float y = 0.0f;
    for (int k = 0; k < T; k++) y += a[k] * w[k];
    return y;
#endif
}

// Peak over the interval [age H, age H-1) of one channel's window: phase 0 is the
// sample at age H itself, phases 1..P-1 are interpolated.
template <int T, int P> static inline float WindowPeak(float const *w, float const (*rc)[T], int H)
{
    float peak = fabsf(w[T - 1 - H]);
    for (int ph = 1; ph < P; ph++)
    {
        float a = fabsf(Dot<T>(rc[ph], w));
        if (a > peak) peak = a;
    }
    return peak;
}

static float g_tpRev[TP_PHASES][TP_TAPS];      // meter coefficients, reversed for Mirror windows
static float g_detRev[DET_PHASES][DET_TAPS];   // detector coefficients, reversed

static void InitReversed()
{
    for (int ph = 0; ph < TP_PHASES; ph++)
        for (int k = 0; k < TP_TAPS; k++) g_tpRev[ph][k] = g_tpCoef[ph][TP_TAPS - 1 - k];
    for (int ph = 0; ph < DET_PHASES; ph++)
        for (int k = 0; k < DET_TAPS; k++) g_detRev[ph][k] = g_detCoef[ph][DET_TAPS - 1 - k];
}
static_assert(TP_TAPS % 4 == 0 && DET_TAPS % 4 == 0, "interpolator lengths must be multiples of 4");

// ── Biquad (double precision, transposed direct form II, 2 channels) ──────
struct Biquad
{
    double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    double z1[2] = { 0, 0 }, z2[2] = { 0, 0 };
    void Reset() { z1[0] = z1[1] = z2[0] = z2[1] = 0.0; }
    inline double Run(int ch, double x)
    {
        double y = b0 * x + z1[ch];
        z1[ch] = b1 * x - a1 * y + z2[ch];
        z2[ch] = b2 * x - a2 * y;
        return y;
    }
    void Set(double B0, double B1, double B2, double A0, double A1, double A2)
    {
        b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0; a1 = A1 / A0; a2 = A2 / A0;
    }
    // RBJ cookbook designs.
    void LowPass(double fs, double f, double q)
    {
        double w = 2 * PI_D * f / fs, cw = cos(w), al = sin(w) / (2 * q);
        Set((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + al, -2 * cw, 1 - al);
    }
    void HighPass(double fs, double f, double q)
    {
        double w = 2 * PI_D * f / fs, cw = cos(w), al = sin(w) / (2 * q);
        Set((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + al, -2 * cw, 1 - al);
    }
    void LowShelf(double fs, double f, double db)       // shelf slope S = 1
    {
        double A = pow(10.0, db / 40.0), w = 2 * PI_D * f / fs, cw = cos(w);
        double al = sin(w) / 2 * sqrt(2.0), sA = 2 * sqrt(A) * al;
        Set(A * ((A + 1) - (A - 1) * cw + sA), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sA),
            (A + 1) + (A - 1) * cw + sA, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sA);
    }
    void HighShelf(double fs, double f, double db)
    {
        double A = pow(10.0, db / 40.0), w = 2 * PI_D * f / fs, cw = cos(w);
        double al = sin(w) / 2 * sqrt(2.0), sA = 2 * sqrt(A) * al;
        Set(A * ((A + 1) + (A - 1) * cw + sA), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sA),
            (A + 1) - (A - 1) * cw + sA, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sA);
    }
    // First-order tilt around f0: gain 1/A below, A above, exactly 0 dB at f0.
    // Analog H(s) = (A·s + w0) / (s + A·w0), bilinear with prewarping.
    void Tilt(double fs, double f0, double db)
    {
        double A = pow(10.0, db / 40.0);
        double k = tan(PI_D * f0 / fs);                  // prewarped w0 / (2 fs)
        // s = (1 - z^-1)/(1 + z^-1) with w0 → k
        Set(A + k, k - A, 0, 1 + A * k, A * k - 1, 0);
    }
};

// ── State-variable filter (trapezoidal, after A. Simper, Cytomic) ─────────
// Same responses as the RBJ biquads, but it stays well-behaved while its
// frequency and gain move, so the EQ and Low Mono can glide without clicks.
struct Svf
{
    // c = current coefficients {a1, a2, a3, m0, m1, m2}; t = target; d = per-sample step.
    double c[6] = { 1, 0, 0, 1, 0, 0 }, t[6] = { 1, 0, 0, 1, 0, 0 }, d[6] = { 0, 0, 0, 0, 0, 0 };
    int    left = 0;                                   // samples of ramp remaining
    double ic1[2] = { 0, 0 }, ic2[2] = { 0, 0 };
    void Reset() { ic1[0] = ic1[1] = ic2[0] = ic2[1] = 0.0; }

    void Core(double g, double k, double m0, double m1, double m2)
    {
        double a1 = 1.0 / (1.0 + g * (g + k));
        t[0] = a1; t[1] = g * a1; t[2] = g * g * a1; t[3] = m0; t[4] = m1; t[5] = m2;
    }
    void LowPass (double fs, double f, double q) { Core(tan(PI_D * f / fs), 1 / q, 0, 0, 1); }
    void HighPass(double fs, double f, double q) { double k = 1 / q; Core(tan(PI_D * f / fs), k, 1, -k, -1); }
    void LowShelf(double fs, double f, double db)                 // Q = 0.7071 (slope S = 1)
    {
        double A = pow(10.0, db / 40.0), k = sqrt(2.0);
        Core(tan(PI_D * f / fs) / sqrt(A), k, 1, k * (A - 1), A * A - 1);
    }
    void HighShelf(double fs, double f, double db)
    {
        double A = pow(10.0, db / 40.0), k = sqrt(2.0);
        Core(tan(PI_D * f / fs) * sqrt(A), k, A * A, k * (1 - A) * A, 1 - A * A);
    }
    // Move to the new design: at once (n = 0) or linearly over n samples.
    void Apply(int n)
    {
        if (n <= 0) { for (int i = 0; i < 6; i++) { c[i] = t[i]; d[i] = 0; } left = 0; return; }
        for (int i = 0; i < 6; i++) d[i] = (t[i] - c[i]) / n;
        left = n;
    }
    inline void Step()
    {
        if (left <= 0) return;
        if (--left == 0) for (int i = 0; i < 6; i++) c[i] = t[i];
        else             for (int i = 0; i < 6; i++) c[i] += d[i];
    }
    inline double Tick(int ch, double v0)
    {
        double v3 = v0 - ic2[ch];
        double v1 = c[0] * ic1[ch] + c[1] * v3;
        double v2 = ic2[ch] + c[1] * ic1[ch] + c[2] * v3;
        ic1[ch] = 2 * v1 - ic1[ch];
        ic2[ch] = 2 * v2 - ic2[ch];
        return c[3] * v0 + c[4] * v1 + c[5] * v2;
    }
    inline double Run1(double x) { Step(); return Tick(0, x); }                         // one channel
    inline void   Run2(double &l, double &r) { Step(); l = Tick(0, l); r = Tick(1, r); } // stereo pair
};

// K-weighting coefficients (BS.1770-4), as in Pedal Gain Multi N.
static void KShelf(double rate, Biquad &q)
{
    double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
    double K = tan(PI_D * f0 / rate), Vh = pow(10.0, G / 20.0), Vb = pow(Vh, 0.4996667741545416);
    double a0 = 1.0 + K / Q + K * K;
    q.b0 = (Vh + Vb * K / Q + K * K) / a0; q.b1 = 2.0 * (K * K - Vh) / a0; q.b2 = (Vh - Vb * K / Q + K * K) / a0;
    q.a1 = 2.0 * (K * K - 1.0) / a0;       q.a2 = (1.0 - K / Q + K * K) / a0;
}
static void KHighPass(double rate, Biquad &q)
{
    double f0 = 38.13547087602444, Q = 0.5003270373238773;
    double K = tan(PI_D * f0 / rate), a0 = 1.0 + K / Q + K * K;
    q.b0 = 1.0; q.b1 = -2.0; q.b2 = 1.0;
    q.a1 = 2.0 * (K * K - 1.0) / a0; q.a2 = (1.0 - K / Q + K * K) / a0;
}

// ── Meter accumulator (one per reader slot) ────────────────────────────────
struct MeterAcc
{
    float  inPk[2], outTp[2];
    double inSq[2], outSq[2];
    float  compGr, limGr;            // dB
    double count;
    float  blocks[LOUD_BLOCK_CAP];
    int    nBlocks, dropped;
    double cLL, cLR, cRR;

    void Clear()
    {
        inPk[0] = inPk[1] = outTp[0] = outTp[1] = 0.0f;
        inSq[0] = inSq[1] = outSq[0] = outSq[1] = 0.0;
        compGr = limGr = 0.0f;
        count = 0.0;
        nBlocks = dropped = 0;
        cLL = cLR = cRR = 0.0;
    }
};

// ── Parameters ─────────────────────────────────────────────────────────────
enum
{
    P_INPUT = 0,
    P_EQ, P_LOWCUT, P_LOWFREQ, P_LOWGAIN, P_HIGHFREQ, P_HIGHGAIN, P_TILT,
    P_COMP, P_THRESH, P_RATIO, P_ATTACK, P_RELEASE, P_SCHPF, P_MAKEUP, P_MIX,
    P_LOWMONO, P_WIDTH,
    P_LIMITER, P_LIMGAIN, P_CEILING, P_LIMREL,
    P_BYPASS,
    P_SLOPE, P_MATCH, P_DITHER,             // v0.3 (appended)
    P_COUNT
};

#define SW(name, desc, def) { pt_switch, name, desc, -1, -1, SWITCH_NO, MPF_STATE, def }
#define BY(name, desc, mx, def) { pt_byte, name, desc, 0, mx, 0xFF, MPF_STATE, def }
#define WO(name, desc, mx, def) { pt_word, name, desc, 0, mx, 0xFFFF, MPF_STATE, def }

static CMachineParameter const pars[P_COUNT] =
{
    WO("Input",       "Input trim, -24..+24 dB (240 = 0 dB, 0.1 dB steps)", 480, 240),
    SW("EQ",          "EQ section on/off", SWITCH_ON),
    BY("Low Cut",     "Low cut: 0 = Off, 1..100 = 10..250 Hz (slope: Slope)", 100, 0),
    BY("Low Freq",    "Low shelf frequency, 25..400 Hz (50 = 100 Hz)", 100, 50),
    BY("Low Gain",    "Low shelf gain, -12..+12 dB (120 = 0 dB)", 240, 120),
    BY("High Freq",   "High shelf frequency, 1.25..20 kHz (75 = 10 kHz)", 100, 75),
    BY("High Gain",   "High shelf gain, -12..+12 dB (120 = 0 dB)", 240, 120),
    BY("Tilt",        "Tilt around 1 kHz, -6..+6 dB end to end (60 = flat); +6 = lows -3 dB, highs +3 dB", 120, 60),
    SW("Comp",        "Glue compressor on/off", SWITCH_OFF),
    BY("Threshold",   "Compressor threshold, -40..0 dB (0.5 dB steps)", 80, 60),
    BY("Ratio",       "Compressor ratio: 1.5, 2, 3, 4, 6, 10 :1", 5, 1),
    BY("Attack",      "Compressor attack: 0.1, 0.3, 1, 3, 10, 30 ms", 5, 4),
    BY("Release",     "Compressor release: 0.1, 0.3, 0.6, 1.2 s, Auto", 4, REL_AUTO),
    BY("SC HPF",      "Sidechain high-pass: Off, 60, 90, 120, 150 Hz", 4, 0),
    BY("Makeup",      "Compressor makeup gain, 0..+20 dB", 200, 0),
    BY("Comp Mix",    "Compressor dry/wet (parallel) mix, %", 100, 100),
    BY("Low Mono",    "Mono below: 0 = Off, 1..100 = 40..300 Hz", 100, 0),
    BY("Width",       "Stereo width, % (100 = unchanged, 0 = mono)", 200, 100),
    SW("Limiter",     "True-peak limiter on/off", SWITCH_ON),
    WO("Lim Gain",    "Gain into the limiter, 0..+24 dB", 240, 0),
    BY("Ceiling",     "Limiter ceiling, -6.0..0.0 dBTP (60 = 0 dBTP)", 60, 50),
    BY("Lim Release", "Limiter release, 10..1000 ms", 100, 50),
    SW("Bypass",      "Bypass (latency-compensated, click-free)", SWITCH_OFF),
    BY("Slope",       "Low cut slope: 0 = 12 dB/oct, 1 = 24 dB/oct", 1, 1),
    SW("Match",       "Level-matched A/B: the louder of master and dry is turned down to match", SWITCH_OFF),
    BY("Dither",      "Final-render dither: Off, 24-bit, 16-bit, 16-bit shaped", 3, 0),
};

static CMachineParameter const *pParameters[P_COUNT] =
{
    &pars[0],  &pars[1],  &pars[2],  &pars[3],  &pars[4],  &pars[5],  &pars[6],  &pars[7],
    &pars[8],  &pars[9],  &pars[10], &pars[11], &pars[12], &pars[13], &pars[14], &pars[15],
    &pars[16], &pars[17], &pars[18], &pars[19], &pars[20], &pars[21], &pars[22],
    &pars[23], &pars[24], &pars[25]
};

#pragma pack(1)
struct gvals
{
    word input;
    byte eq, lowCut, lowFreq, lowGain, highFreq, highGain, tilt;
    byte comp, thresh, ratio, attack, release, scHpf, makeup, mix;
    byte lowMono, width;
    byte limiter;
    word limGain;
    byte ceiling, limRel;
    byte bypass;
    byte slope, match, dither;
};
#pragma pack()
static_assert(sizeof(gvals) == 28, "gvals must be packed and mirror pParameters");

CMachineInfo const MacInfo =
{
    MT_EFFECT,
    MI_VERSION,
    MIF_STEREO_EFFECT,
    0, 0,
    P_COUNT, 0,
    pParameters,
    0, NULL,
    "Pedal Master N",
    "PMasterN",
    "WDE",
    NULL,
    NULL
};

// ── Limiter geometry ───────────────────────────────────────────────────────
static double const LOOKAHEAD_S = 0.002;
static int const TP_DELAY = DET_HALF - 1;        // detector interval delay (see the limiter timing note)
static int const RING     = 2048;                // delay rings (power of two)
static int const MAX_LOOK = 1000;                // look-ahead cap (192 kHz → 384)
static int const EQ_STEP  = 16;                  // samples between filter redesigns while gliding
static double const MATCH_TAU = 2.0;             // loudness averaging for Match, seconds
static double const MATCH_MAX_DB = 30.0;

class mi;
class miex : public CMachineInterfaceEx
{
public:
    mi *pmi = nullptr;
    virtual bool HandleGUIMessage(CMachineDataOutput *pout, CMachineDataInput *pin);
    virtual int  GetLatency();
};

class mi : public CMachineInterface
{
public:
    mi();
    virtual void Init(CMachineDataInput * const pi);
    virtual void Tick();
    virtual void Save(CMachineDataOutput * const po) { po->Write(SAVE_VERSION); }
    virtual bool Work(float *psamples, int numsamples, int const mode);
    virtual char const *DescribeValue(int const param, int const value);

    bool HandleGUIMessage(CMachineDataOutput *pout, CMachineDataInput *pin);
    int  Latency() const { return latency.load(std::memory_order_relaxed); }

    // Parameter state (written in Tick, read in Work). Public for the test harness.
    int v[P_COUNT];

private:
    void Configure(int sr);
    void UpdateEq(int n, bool snap);
    void Process(float *ps, int n);
    void UpdateMatch(double eIn, double eOut);
    bool anyAudible = false;

    gvals gval;
    miex  ex;
    char  descBuf[64];
    int   sr = 0;
    std::atomic<int> latency;
    bool  first = true;

    // Glided per-sample values.
    float gIn = 1, eqMix = 1, lcMix = 0, compOn = 0, makeup = 1, mix = 1;
    float lmMix = 0, width = 1, limOn = 1, limGain = 1, ceilLin = 1, bypass = 0;
    float slopeMix = 1, gMaster = 1, gDry = 1;         // 24 dB/oct share; Match gains
    float kGlide = 1;

    // EQ (block-rate glide of the design values).
    double lcHz = 20, lsHz = 100, lsDb = 0, hsHz = 10000, hsDb = 0, tiltDb = 0;
    double lmHz = 120;
    Svf    lowCut, lowCut24a, lowCut24b, lowShelf, highShelf;   // lowCut = 12 dB/oct; 24a+24b = 24 dB/oct
    Svf    dcBlock;                                             // 5 Hz, always on
    Biquad tilt;                                        // first order, fixed 1 kHz pivot
    Svf    lmLp1, lmLp2, lmHp1, lmHp2, lmSHp1, lmSHp2;   // Low Mono LR4 crossover
    int    scHpfSel = -1;
    Biquad scHpf;

    // Compressor (gain reduction in dB, >= 0).
    float  grFast = 0, grSlow = 0;
    double detMs = 0;

    // Limiter.
    int    look = 96;                   // look-ahead, samples
    Mirror<DET_TAPS> hist;              // detector history
    float  prevPk = 0;
    float  dq_val[RING]; int dq_idx[RING]; int dqHead = 0, dqTail = 0;  // monotonic min deque
    float  box[RING];  double boxSum = 0; int boxPos = 0;
    float  relState = 1;
    int    sampleIdx = 0;               // running sample counter (wraps harmlessly via masks)
    float  dPre[2][RING], dLim[2][RING], dRaw[2][RING];
    int    dw = 0;
    float  limRelK = 0;

    // Output metering.
    Mirror<TP_TAPS> mh;                 // output true-peak meter history
    Biquad kS, kH;  int kRate = 0, blockLen = 0, blockFill = 0; double blockSum = 0;

    // Match: K-weighted loudness of the dry input vs the master, per 100 ms block.
    Biquad kSi, kHi;  double inBlockSum = 0;
    double avgIn = 0, avgOut = 0;
    std::atomic<float> matchDb;

    // Dither.
    unsigned rng = 0x9E3779B9u;
    float  dErr[2][3] = {};

    MeterAcc         meterAcc[METER_SLOTS];
    std::atomic_flag meterLock;
    void LockMeters()   { while (meterLock.test_and_set(std::memory_order_acquire)) {} }
    void UnlockMeters() { meterLock.clear(std::memory_order_release); }
};

mi::mi()
{
    GlobalVals = &gval;
    TrackVals  = NULL;
    AttrVals   = NULL;
    for (int i = 0; i < P_COUNT; i++) v[i] = pars[i].DefValue;
    latency.store(0);
    matchDb.store(0.0f);
    meterLock.clear();
    for (int s = 0; s < METER_SLOTS; s++) meterAcc[s].Clear();
    InitTruePeakFilter();
    InitDetectorFilter();
    InitReversed();
}

void mi::Init(CMachineDataInput * const pi)
{
    ex.pmi = this;
    pCB->SetMachineInterfaceEx(&ex);
    if (pi != NULL) { byte ver = 0; pi->Read(ver); }
    Configure(pMasterInfo->SamplesPerSec);
}

void mi::Tick()
{
    byte const *b = (byte const *)&gval;
    int off = 0;
    for (int i = 0; i < P_COUNT; i++)
    {
        CMachineParameter const &p = pars[i];
        int val;
        if (p.Type == pt_word) { word w; memcpy(&w, b + off, 2); val = w; off += 2; }
        else                   { val = b[off]; off += 1; }
        if (val != p.NoValue) v[i] = val;
    }
}

// (Re)design everything that depends on the sample rate and reset the
// limiter, delay lines and loudness blocks.
void mi::Configure(int rate)
{
    if (rate <= 0) rate = 44100;
    sr = rate;
    kGlide = 1.0f - expf(-1.0f / (SMOOTH_SECONDS * sr));

    look = (int)(LOOKAHEAD_S * sr + 0.5);
    if (look < 1) look = 1;
    if (look > MAX_LOOK) look = MAX_LOOK;
    latency.store(look - 1 + TP_DELAY + 1, std::memory_order_relaxed);   // see Limiter timing

    for (int c = 0; c < 2; c++)
    {
        for (int j = 0; j < RING; j++) dPre[c][j] = dLim[c][j] = dRaw[c][j] = 0.0f;
    }
    for (int j = 0; j < RING; j++) box[j] = 1.0f;
    boxSum = look; boxPos = 0; dqHead = dqTail = 0; relState = 1; prevPk = 0;
    hist.Reset(); mh.Reset(); dw = 0; sampleIdx = 0;

    KShelf(sr, kS); KHighPass(sr, kH); kS.Reset(); kH.Reset();
    KShelf(sr, kSi); KHighPass(sr, kHi); kSi.Reset(); kHi.Reset();
    inBlockSum = 0; avgIn = avgOut = 0; matchDb.store(0.0f);
    for (int c = 0; c < 2; c++) dErr[c][0] = dErr[c][1] = dErr[c][2] = 0.0f;
    dcBlock.HighPass(sr, 5.0, 0.70710678); dcBlock.Apply(0); dcBlock.Reset();
    kRate = sr; blockLen = (sr + 5) / 10; blockFill = 0; blockSum = 0;

    lowCut.Reset(); lowCut24a.Reset(); lowCut24b.Reset(); lowShelf.Reset(); highShelf.Reset(); tilt.Reset();
    lmLp1.Reset(); lmLp2.Reset(); lmHp1.Reset(); lmHp2.Reset(); lmSHp1.Reset(); lmSHp2.Reset();
    scHpf.Reset(); scHpfSel = -1;
    first = true;
}

// Block-rate glide of the filter design values; redesign when they move.
void mi::UpdateEq(int n, bool snap)
{
    double k = snap ? 1.0 : 1.0 - exp(-(double)n / (SMOOTH_SECONDS * sr));
    auto glideLog = [&](double &cur, double tgt) -> bool {
        if (cur == tgt) return false;
        double lc = log(cur), lt = log(tgt);
        lc += (lt - lc) * k;
        cur = fabs(lt - lc) < 1e-4 ? tgt : exp(lc);
        return true;
    };
    auto glideLin = [&](double &cur, double tgt) -> bool {
        if (cur == tgt) return false;
        cur += (tgt - cur) * k;
        if (fabs(tgt - cur) < 0.005) cur = tgt;
        return true;
    };

    // A frequency whose section is fully faded out jumps straight to its target,
    // so switching the section on never sweeps the filter through the audio.
    bool dirty = snap;
    if (v[P_LOWCUT] > 0)
    {
        double t = LowCutHz(v[P_LOWCUT]);
        if (lcMix == 0.0f && lcHz != t) { lcHz = t; dirty = true; }
        else if (glideLog(lcHz, t)) dirty = true;
    }
    dirty |= glideLog(lsHz, LowShelfHz(v[P_LOWFREQ]));
    dirty |= glideLin(lsDb, ShelfDb(v[P_LOWGAIN]));
    dirty |= glideLog(hsHz, HighShelfHz(v[P_HIGHFREQ]));
    dirty |= glideLin(hsDb, ShelfDb(v[P_HIGHGAIN]));
    dirty |= glideLin(tiltDb, TiltDb(v[P_TILT]));
    int ramp = snap ? 0 : n;               // coefficients move linearly across the step
    if (dirty)
    {
        double fmax = 0.45 * sr;                 // keep designs below Nyquist at low sample rates
        double fc = lcHz < fmax ? lcHz : fmax;
        lowCut.HighPass(sr, fc, 0.70710678);      lowCut.Apply(ramp);
        lowCut24a.HighPass(sr, fc, 0.54119610);   lowCut24a.Apply(ramp);   // 4th-order Butterworth
        lowCut24b.HighPass(sr, fc, 1.30656296);   lowCut24b.Apply(ramp);
        lowShelf.LowShelf(sr, lsHz < fmax ? lsHz : fmax, lsDb);           lowShelf.Apply(ramp);
        highShelf.HighShelf(sr, hsHz < fmax ? hsHz : fmax, hsDb);         highShelf.Apply(ramp);
        tilt.Tilt(sr, 1000.0, tiltDb);
    }

    bool lmDirty = snap;
    if (v[P_LOWMONO] > 0)
    {
        double t = LowMonoHz(v[P_LOWMONO]);
        if (lmMix == 0.0f && lmHz != t) { lmHz = t; lmDirty = true; }
        else if (glideLog(lmHz, t)) lmDirty = true;
    }
    if (lmDirty)
    {
        double const q = 0.70710678;         // Butterworth² = Linkwitz-Riley 4th order
        Svf *f[6] = { &lmLp1, &lmLp2, &lmHp1, &lmHp2, &lmSHp1, &lmSHp2 };
        for (int i = 0; i < 6; i++)
        {
            if (i < 2) f[i]->LowPass(sr, lmHz, q); else f[i]->HighPass(sr, lmHz, q);
            f[i]->Apply(ramp);
        }
    }

    int sel = v[P_SCHPF];
    if (sel != scHpfSel)
    {
        if (sel > 0) scHpf.HighPass(sr, SC_HPF_HZ[sel], 0.70710678);
        scHpf.Reset();
        scHpfSel = sel;
    }
}

static inline float Glide(float cur, float tgt, float k)
{
    cur += (tgt - cur) * k;
    return fabsf(tgt - cur) < 1e-4f ? tgt : cur;
}

bool mi::Work(float *psamples, int numsamples, int const mode)
{
#ifdef PMN_HAVE_SSE
    unsigned int csr = _mm_getcsr();
    _mm_setcsr(csr | 0x8040);                    // flush-to-zero + denormals-are-zero
#endif
    if (!(mode & WM_READ))
        for (int s = 0; s < numsamples * 2; s++) psamples[s] = 0.0f;

    int rate = pMasterInfo->SamplesPerSec;
    if (rate > 0 && rate != sr) Configure(rate);

    bool audible = false;
    for (int done = 0; done < numsamples; done += MAX_BUFFER_LENGTH)
    {
        int m = numsamples - done < MAX_BUFFER_LENGTH ? numsamples - done : MAX_BUFFER_LENGTH;
        Process(psamples + 2 * done, m);
        if (anyAudible) audible = true;
    }
    // Silent unless the (pre-dither) output is audible: digital silence gets no dither noise.
    // (anyAudible is set per block inside Process.)

#ifdef PMN_HAVE_SSE
    _mm_setcsr(csr);
#endif
    return audible && (mode & WM_WRITE) != 0;
}

void mi::Process(float *ps, int n)
{
    // ── Targets ──
    float tIn     = DbToLin(InputDb(v[P_INPUT]));
    float tEq     = v[P_EQ] ? 1.0f : 0.0f;
    float tLc     = (v[P_EQ] && v[P_LOWCUT] > 0) ? 1.0f : 0.0f;
    float tComp   = v[P_COMP] ? 1.0f : 0.0f;
    float tMakeup = DbToLin(MakeupDb(v[P_MAKEUP]));
    float tMix    = v[P_MIX] * 0.01f;
    float tLm     = v[P_LOWMONO] > 0 ? 1.0f : 0.0f;
    float tWidth  = v[P_WIDTH] * 0.01f;
    float tLim    = v[P_LIMITER] ? 1.0f : 0.0f;
    float tLimG   = DbToLin(LimGainDb(v[P_LIMGAIN]));
    float tCeil   = FULL_SCALE * DbToLin(CeilingDb(v[P_CEILING]));
    float tByp    = v[P_BYPASS] ? 1.0f : 0.0f;
    float tSlope  = v[P_SLOPE] ? 1.0f : 0.0f;

    // Match: turn the louder of master and dry down to the other's loudness.
    float mdb = v[P_MATCH] ? matchDb.load(std::memory_order_relaxed) : 0.0f;
    float tGM = mdb > 0 ? DbToLin(-mdb) : 1.0f;
    float tGD = mdb < 0 ? DbToLin(mdb)  : 1.0f;
    float kMatch = 1.0f - expf(-1.0f / (0.1f * sr));      // 100 ms glide

    int   dither = v[P_DITHER];
    float qStep  = dither == 1 ? 1.0f / 256.0f : 1.0f;    // Buzz units: 1.0 = one 16-bit LSB
    float qInv   = 1.0f / qStep;

    bool const snapEq = first;
    if (first)
    {
        gIn = tIn; eqMix = tEq; lcMix = tLc; compOn = tComp; makeup = tMakeup; mix = tMix;
        lmMix = tLm; width = tWidth; limOn = tLim; limGain = tLimG; ceilLin = tCeil; bypass = tByp;
        slopeMix = tSlope; gMaster = tGM; gDry = tGD;
        first = false;
    }

    // Compressor constants.
    float thr   = ThreshDb(v[P_THRESH]);
    float ratio = RATIOS[v[P_RATIO] < 6 ? v[P_RATIO] : 5];
    float slope = 1.0f - 1.0f / ratio;
    float const knee = 6.0f;
    float aAtt  = 1.0f - expf(-1.0f / (ATTACKS_MS[v[P_ATTACK] < 6 ? v[P_ATTACK] : 5] * 0.001f * sr));
    bool  autoRel = v[P_RELEASE] >= REL_AUTO;
    float aRel  = 1.0f - expf(-1.0f / ((autoRel ? 100.0f : RELEASES_MS[v[P_RELEASE]]) * 0.001f * sr));
    // Auto release: a second, slow envelope only builds up under sustained
    // compression (0.8 s) and lets go over 0.4 s, so short peaks recover at the
    // fast rate (~0.1 s) and long passages at roughly 1 s.
    float aSlowAtt = 1.0f - expf(-1.0f / (0.8f * sr));
    float aSlowRel = 1.0f - expf(-1.0f / (0.4f * sr));
    bool  useSc = v[P_SCHPF] > 0;
    double aDet = 1.0 - exp(-1.0 / (0.005 * sr));

    limRelK = 1.0f - expf(-1.0f / ((float)LimRelMs(v[P_LIMREL]) * 0.001f * sr));
    float const invLook = 1.0f / look;
    int   const D = look - 1 + TP_DELAY + 1;            // output delay, = latency

    float  k = kGlide;
    float  inPk[2] = { 0, 0 }, outTp[2] = { 0, 0 };
    double inSq[2] = { 0, 0 }, outSq[2] = { 0, 0 };
    float  maxCompGr = 0, minLimG = 1;
    anyAudible = false;
    float  doneBlocks[4]; int nDone = 0;
    double cLL = 0, cLR = 0, cRR = 0;
    double const invFs = 1.0 / FULL_SCALE;

    for (int s = 0; s < n; s++)
    {
        // Filter designs glide in steps of EQ_STEP samples: small enough that a
        // live frequency sweep doesn't jolt the filters.
        if ((s % EQ_STEP) == 0) UpdateEq(EQ_STEP, snapEq && s == 0);

        float inL = ps[2 * s], inR = ps[2 * s + 1];

        float a = fabsf(inL), b = fabsf(inR);
        if (a > inPk[0]) inPk[0] = a;
        if (b > inPk[1]) inPk[1] = b;
        inSq[0] += (double)inL * inL;
        inSq[1] += (double)inR * inR;

        gIn = Glide(gIn, tIn, k);        eqMix = Glide(eqMix, tEq, k);   lcMix = Glide(lcMix, tLc, k);
        compOn = Glide(compOn, tComp, k); makeup = Glide(makeup, tMakeup, k); mix = Glide(mix, tMix, k);
        lmMix = Glide(lmMix, tLm, k);    width = Glide(width, tWidth, k);
        limOn = Glide(limOn, tLim, k);   limGain = Glide(limGain, tLimG, k); ceilLin = Glide(ceilLin, tCeil, k);
        bypass = Glide(bypass, tByp, k);  slopeMix = Glide(slopeMix, tSlope, k);
        gMaster = Glide(gMaster, tGM, kMatch); gDry = Glide(gDry, tGD, kMatch);

        // ── Dry loudness for Match (K-weighted, per 100 ms block) ──
        {
            double nl = inL / FULL_SCALE, nr = inR / FULL_SCALE;
            double kl = kHi.Run(0, kSi.Run(0, nl)), kr = kHi.Run(1, kSi.Run(1, nr));
            inBlockSum += kl * kl + kr * kr;
        }

        // ── Input trim, DC blocker (5 Hz, 2nd order, always on) ──
        double xl = inL * gIn, xr = inR * gIn;
        dcBlock.Run2(xl, xr);

        // ── EQ ──  (filters always run, so switching never starts from stale state)
        {
            double hl = xl, hr = xr, ql = xl, qr = xr;
            lowCut.Run2(hl, hr);                      // 12 dB/oct
            lowCut24a.Run2(ql, qr);                   // 24 dB/oct
            lowCut24b.Run2(ql, qr);
            hl += (ql - hl) * slopeMix;
            hr += (qr - hr) * slopeMix;
            double yl = xl + (hl - xl) * lcMix, yr = xr + (hr - xr) * lcMix;
            lowShelf.Run2(yl, yr);
            highShelf.Run2(yl, yr);
            yl = tilt.Run(0, yl);
            yr = tilt.Run(1, yr);
            xl += (yl - xl) * eqMix;
            xr += (yr - xr) * eqMix;
        }

        // ── Glue compressor (feed-forward, log domain, stereo-linked) ──
        // The detector always runs, so switching the compressor on lands straight
        // on its settled gain reduction; only the gain maths is skipped while off.
        {
            double sl = xl, sr2 = xr;
            if (useSc) { sl = scHpf.Run(0, xl); sr2 = scHpf.Run(1, xr); }
            // RMS detector (5 ms), louder side wins: steady on low frequencies
            // where a raw peak detector would ripple.
            double ql = sl / FULL_SCALE, qr = sr2 / FULL_SCALE;
            double q  = ql * ql > qr * qr ? ql * ql : qr * qr;
            detMs += (q - detMs) * aDet;
            float db  = detMs > 1e-12 ? 10.0f * (float)log10(detMs) : -120.0f;
            float over = db - thr, gr;
            if (2 * over < -knee)     gr = 0.0f;
            else if (2 * over > knee) gr = over * slope;
            else { float t = over + knee / 2; gr = slope * t * t / (2 * knee); }

            grFast += (gr - grFast) * (gr > grFast ? aAtt : aRel);
            float grTot = grFast;
            if (autoRel)
            {
                grSlow += (gr - grSlow) * (gr > grSlow ? aSlowAtt : aSlowRel);
                if (grSlow > grTot) grTot = grSlow;
            }
            if (compOn > 0.0f || tComp > 0.0f)       // fully off = exactly unity gain
            {
                float gc   = DbToLin(-grTot);
                float eff  = makeup * (1.0f - mix + mix * gc);
                float g    = 1.0f + (eff - 1.0f) * compOn;
                xl *= g; xr *= g;
                float shown = grTot * compOn * mix;
                if (shown > maxCompGr) maxCompGr = shown;
            }
        }

        // ── Stereo: Low Mono (LR4 on the side channel) and Width ──
        {
            double M = (xl + xr) * 0.5, S = (xl - xr) * 0.5;
            double mAp = lmLp2.Run1(lmLp1.Run1(M)) + lmHp2.Run1(lmHp1.Run1(M));   // LR4 sum = all-pass
            double sHp = lmSHp2.Run1(lmSHp1.Run1(S));
            M += (mAp - M) * lmMix;
            S += (sHp - S) * lmMix;
            S *= width;
            xl = M + S; xr = M - S;
        }

        // ── True-peak limiter ──
        // Timing, for the sample j that enters now (step n):
        //   the detector sees the interval [j-H, j-H+1) (H = DET_HALF samples of interpolation delay);
        //   need(j-H) = max(interval(j-H), interval(j-H-1)) → attenuation a(j-H);
        //   s = min of a over the last `look` samples, r = s with release, G = mean of r over `look`;
        //   G at this step is safe for sample (j-H) - (look-1), which is what we output,
        //   so the latency is look - 1 + H.
        float pl = (float)xl, pr = (float)xr;
        float gl = pl * limGain, gr2 = pr * limGain;
        // The detector runs even while the limiter is off, so switching it on lands
        // on a settled gain envelope instead of letting the first peaks through.
        hist.Push(gl, gr2);
        float ipk  = WindowPeak<DET_TAPS, DET_PHASES>(hist.Win(0), g_detRev, DET_HALF);
        float ipk2 = WindowPeak<DET_TAPS, DET_PHASES>(hist.Win(1), g_detRev, DET_HALF);
        if (ipk2 > ipk) ipk = ipk2;
        float need = ipk > prevPk ? ipk : prevPk;
        prevPk = ipk;
        float att = need > ceilLin ? ceilLin / need : 1.0f;

        int idx = sampleIdx++;
        while (dqTail != dqHead && dq_val[(dqTail - 1) & (RING - 1)] >= att) dqTail = (dqTail - 1) & (RING - 1);
        dq_val[dqTail] = att; dq_idx[dqTail] = idx; dqTail = (dqTail + 1) & (RING - 1);
        while (idx - dq_idx[dqHead] >= look) dqHead = (dqHead + 1) & (RING - 1);
        float smin = dq_val[dqHead];

        relState += (1.0f - relState) * limRelK;
        if (smin < relState) relState = smin;

        boxSum += relState - box[boxPos];
        box[boxPos] = relState;
        boxPos = (boxPos + 1) % look;
        float G = (float)(boxSum * invLook);
        if (G > 1.0f) G = 1.0f;

        dPre[0][dw] = pl;  dPre[1][dw] = pr;
        dLim[0][dw] = gl;  dLim[1][dw] = gr2;
        dRaw[0][dw] = inL; dRaw[1][dw] = inR;
        int rd = (dw - D) & (RING - 1);
        dw = (dw + 1) & (RING - 1);

        float ol = dLim[0][rd] * G, orr = dLim[1][rd] * G;
        if (limOn >= 1.0f)           // sample-level safety net at the ceiling
        {
            if (ol >  ceilLin) ol =  ceilLin; else if (ol < -ceilLin) ol = -ceilLin;
            if (orr > ceilLin) orr = ceilLin; else if (orr < -ceilLin) orr = -ceilLin;
        }
        float pdl = dPre[0][rd], pdr = dPre[1][rd];
        ol  = pdl + (ol  - pdl) * limOn;
        orr = pdr + (orr - pdr) * limOn;
        if (limOn > 0.0f && G < minLimG) minLimG = G;

        // ── Output metering: the master (before Bypass, Match and Dither) ──
        outSq[0] += (double)ol * ol;
        outSq[1] += (double)orr * orr;
        mh.Push(ol, orr);
        float t0 = WindowPeak<TP_TAPS, TP_PHASES>(mh.Win(0), g_tpRev, TP_TAPS / 2);
        float t1 = WindowPeak<TP_TAPS, TP_PHASES>(mh.Win(1), g_tpRev, TP_TAPS / 2);
        if (t0 > outTp[0]) outTp[0] = t0;
        if (t1 > outTp[1]) outTp[1] = t1;

        double nl = ol * invFs, nr = orr * invFs;
        cLL += nl * nl; cLR += nl * nr; cRR += nr * nr;
        double kl = kH.Run(0, kS.Run(0, nl)), kr = kH.Run(1, kS.Run(1, nr));
        blockSum += kl * kl + kr * kr;
        if (++blockFill >= blockLen)
        {
            double eOut = blockSum / blockLen, eIn = inBlockSum / blockLen;
            if (nDone < 4) doneBlocks[nDone++] = (float)eOut;
            UpdateMatch(eIn, eOut);
            blockSum = 0; inBlockSum = 0; blockFill = 0;
        }

        // ── Bypass (to the delayed dry input), level-matched when Match is on ──
        float rl = dRaw[0][rd] * gDry, rr = dRaw[1][rd] * gDry;
        ol  *= gMaster;
        orr *= gMaster;
        ol  += (rl - ol)  * bypass;
        orr += (rr - orr) * bypass;

        if (fabsf(ol) > 0.5f || fabsf(orr) > 0.5f) anyAudible = true;

        // ── Dither (last): TPDF at the target word length, optionally noise-shaped ──
        if (dither > 0)
        {
            float o[2] = { ol, orr };
            for (int c = 0; c < 2; c++)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float r1 = (rng & 0xFFFF) * (1.0f / 65536.0f), r2 = (rng >> 16) * (1.0f / 65536.0f);
                float tpdf = (r1 - r2) * qStep;                     // triangular, ±1 LSB
                float want = o[c];
                if (dither == 3)                                     // E-weighted 3-tap error feedback
                    want -= 1.623f * dErr[c][0] - 0.982f * dErr[c][1] + 0.109f * dErr[c][2];
                float q = floorf((want + tpdf) * qInv + 0.5f) * qStep;
                if (dither == 3)
                {
                    dErr[c][2] = dErr[c][1]; dErr[c][1] = dErr[c][0];
                    dErr[c][0] = q - want;                   // shaping error from the unclamped value
                }
                float hi = FULL_SCALE - qStep;                   // 16-bit: +32767, 24-bit: +32767.996
                if (q > hi) q = hi; else if (q < -FULL_SCALE) q = -FULL_SCALE;
                o[c] = q;
            }
            ol = o[0]; orr = o[1];
        }

        ps[2 * s] = ol; ps[2 * s + 1] = orr;
    }

    float limGrDb = minLimG < 1.0f ? -20.0f * log10f(minLimG > 1e-6f ? minLimG : 1e-6f) : 0.0f;

    LockMeters();
    for (int sl = 0; sl < METER_SLOTS; sl++)
    {
        MeterAcc &m = meterAcc[sl];
        for (int c = 0; c < 2; c++)
        {
            if (inPk[c]  > m.inPk[c])  m.inPk[c]  = inPk[c];
            if (outTp[c] > m.outTp[c]) m.outTp[c] = outTp[c];
            m.inSq[c]  += inSq[c];
            m.outSq[c] += outSq[c];
        }
        if (maxCompGr > m.compGr) m.compGr = maxCompGr;
        if (limGrDb   > m.limGr)  m.limGr  = limGrDb;
        m.count += n;
        for (int b = 0; b < nDone; b++)
        {
            if (m.nBlocks < LOUD_BLOCK_CAP) m.blocks[m.nBlocks++] = doneBlocks[b];
            else                            m.dropped++;
        }
        m.cLL += cLL; m.cLR += cLR; m.cRR += cRR;
    }
    UnlockMeters();
}

// Running K-weighted loudness of dry and master (100 ms blocks, τ = MATCH_TAU),
// updated only while both are above -70 LUFS so silence doesn't skew it.
void mi::UpdateMatch(double eIn, double eOut)
{
    double const gate = 1.17e-7;                        // -70 LUFS as block energy
    if (eIn < gate || eOut < gate) return;
    double a = 1.0 - exp(-0.1 / MATCH_TAU);
    if (avgIn <= 0 || avgOut <= 0) { avgIn = eIn; avgOut = eOut; }
    else { avgIn += (eIn - avgIn) * a; avgOut += (eOut - avgOut) * a; }
    double d = 10.0 * log10(avgOut / avgIn);
    if (d >  MATCH_MAX_DB) d =  MATCH_MAX_DB;
    if (d < -MATCH_MAX_DB) d = -MATCH_MAX_DB;
    matchDb.store((float)d, std::memory_order_relaxed);
}

char const *mi::DescribeValue(int const param, int const value)
{
    static char const *ratios[]  = { "1.5:1", "2:1", "3:1", "4:1", "6:1", "10:1" };
    static char const *attacks[] = { "0.1 ms", "0.3 ms", "1 ms", "3 ms", "10 ms", "30 ms" };
    static char const *rels[]    = { "0.1 s", "0.3 s", "0.6 s", "1.2 s", "Auto" };
    static char const *schpf[]   = { "Off", "60 Hz", "90 Hz", "120 Hz", "150 Hz" };

    // Three significant figures: 42.1 Hz, 101 Hz, 1.25 kHz, 10.0 kHz.
    auto hz = [&](double f) -> char const * {
        if      (f >= 10000.0) snprintf(descBuf, sizeof(descBuf), "%.1f kHz", f / 1000.0);
        else if (f >= 1000.0)  snprintf(descBuf, sizeof(descBuf), "%.2f kHz", f / 1000.0);
        else if (f >= 100.0)   snprintf(descBuf, sizeof(descBuf), "%.0f Hz",  f);
        else                   snprintf(descBuf, sizeof(descBuf), "%.1f Hz",  f);
        return descBuf;
    };
    auto db = [&](float d, char const *unit) -> char const * {
        snprintf(descBuf, sizeof(descBuf), "%+.1f %s", d, unit);
        return descBuf;
    };

    switch (param)
    {
    case P_EQ: case P_COMP: case P_LIMITER: case P_BYPASS: case P_MATCH:
        return value ? "On" : "Off";
    case P_SLOPE:    return value ? "24 dB/oct" : "12 dB/oct";
    case P_DITHER:   { static char const *d[] = { "Off", "24-bit", "16-bit", "16-bit shaped" };
                       return (value >= 0 && value < 4) ? d[value] : NULL; }
    case P_INPUT:    return db(InputDb(value), "dB");
    case P_LOWCUT:   return value <= 0 ? "Off" : hz(LowCutHz(value));
    case P_LOWFREQ:  return hz(LowShelfHz(value));
    case P_LOWGAIN:  return db(ShelfDb(value), "dB");
    case P_HIGHFREQ: return hz(HighShelfHz(value));
    case P_HIGHGAIN: return db(ShelfDb(value), "dB");
    case P_TILT:     return db(TiltDb(value), "dB");
    case P_THRESH:   snprintf(descBuf, sizeof(descBuf), "%.1f dB", ThreshDb(value)); return descBuf;
    case P_RATIO:    return (value >= 0 && value < 6) ? ratios[value]  : NULL;
    case P_ATTACK:   return (value >= 0 && value < 6) ? attacks[value] : NULL;
    case P_RELEASE:  return (value >= 0 && value < 5) ? rels[value]    : NULL;
    case P_SCHPF:    return (value >= 0 && value < 5) ? schpf[value]   : NULL;
    case P_MAKEUP:   return db(MakeupDb(value), "dB");
    case P_MIX:      snprintf(descBuf, sizeof(descBuf), "%d%%", value); return descBuf;
    case P_LOWMONO:  return value <= 0 ? "Off" : hz(LowMonoHz(value));
    case P_WIDTH:    snprintf(descBuf, sizeof(descBuf), value == 0 ? "Mono" : "%d%%", value); return descBuf;
    case P_LIMGAIN:  return db(LimGainDb(value), "dB");
    case P_CEILING:  snprintf(descBuf, sizeof(descBuf), "%.1f dBTP", CeilingDb(value)); return descBuf;
    case P_LIMREL:   snprintf(descBuf, sizeof(descBuf), "%.0f ms", LimRelMs(value)); return descBuf;
    }
    return NULL;
}

bool mi::HandleGUIMessage(CMachineDataOutput *pout, CMachineDataInput *pin)
{
    if (pout == nullptr || pin == nullptr) return false;
    int id = 0, slot = 0;
    pin->Read(id);
    if (id != GUIMSG_GET_METERS) return false;
    pin->Read(slot);
    if (slot < 0 || slot >= METER_SLOTS) return false;

    MeterAcc a;
    LockMeters();
    a = meterAcc[slot];
    meterAcc[slot].Clear();
    UnlockMeters();

    float  const inv  = 1.0f / FULL_SCALE;
    double const norm = a.count > 0 ? 1.0 / ((double)FULL_SCALE * FULL_SCALE * a.count) : 0.0;
    double const cn   = a.count > 0 ? 1.0 / a.count : 0.0;

    pout->Write(GUI_PROTOCOL);
    pout->Write(sr);
    pout->Write(Latency());
    pout->Write(a.inPk[0] * inv);   pout->Write(a.inPk[1] * inv);
    pout->Write((float)(a.inSq[0] * norm));  pout->Write((float)(a.inSq[1] * norm));
    pout->Write(a.outTp[0] * inv);  pout->Write(a.outTp[1] * inv);
    pout->Write((float)(a.outSq[0] * norm)); pout->Write((float)(a.outSq[1] * norm));
    pout->Write(a.compGr);
    pout->Write(a.limGr);
    pout->Write(matchDb.load(std::memory_order_relaxed));
    pout->Write(a.nBlocks);
    pout->Write(a.dropped);
    for (int b = 0; b < a.nBlocks; b++) pout->Write(a.blocks[b]);
    pout->Write((float)(a.cLL * cn));
    pout->Write((float)(a.cLR * cn));
    pout->Write((float)(a.cRR * cn));
    return true;
}

bool miex::HandleGUIMessage(CMachineDataOutput *pout, CMachineDataInput *pin)
{
    return pmi ? pmi->HandleGUIMessage(pout, pin) : false;
}
int miex::GetLatency() { return pmi ? pmi->Latency() : 0; }

DLL_EXPORTS
