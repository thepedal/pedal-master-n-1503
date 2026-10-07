# Pedal Master N
<img width="395" height="698" alt="master-n" src="https://github.com/user-attachments/assets/5644cd60-0886-4fdf-9333-d4f4c4cdb37a" />

Native C++ **mastering machine** for **Jeskola Buzz build 1503 (32-bit)**, built on the
Pedal Gain Multi N v1.7.0 code base. Stereo in → stereo out; put it last in the chain,
just before Master. Buzz 1503 only, installed at `C:\Program Files (x86)\Jeskola\Buzz`.
It is not intended for ReBuzz.

**Status: v0.4.0.** v0.3.x is verified live in Buzz 1503. v0.4.0 adds a vintage-style
low end to the EQ, tested in the sandbox and not yet checked live (see *Verify in Buzz
1503*):
- **Low Dip** (Off to 6 dB) cuts a broad band at three times the Low Freq. Combined with
  a Low Gain boost, it gives the classic passive-EQ "boost and attenuate" curve: deeper
  bass with less low-mid mud.
- **Low Shape** switches the low shelf between **Clean** (as before) and **Vintage**, a
  resonant corner with a small bump below it and a small dip just above.

**Upgrading songs from v0.3:** the two new parameters are appended, and at their defaults
(Low Dip Off, Low Shape Clean) the output is identical to v0.3.1, sample for sample. v0.3
songs therefore sound exactly as before.

**Upgrading songs from v0.2:** the three new parameters are appended, so v0.2 songs load
normally. Two things change in them:
- **Low Cut** now spans 10–250 Hz, so a song saved with Low Cut switched on reopens at a
  slightly different frequency.
- **Slope** defaults to 24 dB/oct.

## GUI

`Pedal Master N.GUI.dll` sits next to the machine DLL, and Buzz shows it at the top of
the parameter window. All of its controls write through the machine's parameters, so
patterns, undo and saving stay in step.

- **Header:**
  - **METERS** opens the meter window.
  - **MATCH** lights blue when engaged, and the header then shows what is being turned
    down, for example *A/B matched: master −11.1 dB*.
  - **BYPASS** lights amber when engaged.
- **Meters:**
  - Input L/R (sample peak) and output L/R (true peak), with a 3 s hold and clip lights.
  - **GR** bars for the compressor and the limiter, 0–24 dB, with a 3 s hold of the
    deepest reduction.
  - A loudness line: **M**, **S**, **I**, **LRA** and correlation.
  - Click the meters to clear holds. Click the loudness line to restart I and LRA.
- **Sections:** INPUT, EQ (with Slope, Low Dip and Low Shape), STEREO, COMP, LIMITER and
  OUTPUT (Dither).
  - EQ, COMP and LIMITER have an **ON** switch, and their faders dim while the section is off.
  - Each fader shows the machine's own value text.
  - Drag to change the value from where you grab it (a click alone never moves it; Ctrl-drag is fine),
    double-click to reset, and use the mouse wheel to step (Ctrl for single steps).
- **Meter window** (one per machine):
  - Big INPUT / OUTPUT (true peak) meters: RMS solid, peak translucent.
  - **GAIN RED.** bars hanging from the top.
  - A loudness strip:
    - **M** and **S** bars around the target.
    - **I** and its distance from the target.
    - **LRA** (EBU Tech 3342).
    - **PLR**: max true peak minus I.
    - Max S and max M.
    - Correlation.
  - **TARGET** cycles −23 / −16 / −14 LUFS.
  - **RESET** restarts I, LRA, PLR and the maxima.
  - **→ T** sets Lim Gain so I lands on the target. It measures from what you've played,
    restarts the measurement after the change, and tells you what it did. With heavy
    limiting the result can fall a little short, so press it again.
  - The OUTPUT meters and all loudness values measure **the master**: after the limiter,
    before Bypass, Match and Dither. Comparing sounds therefore never disturbs the
    measurement.
  - It closes with its machine or the song.

The panel and the window read separate meter slots, so neither takes peaks from the other.
Loudness integration runs from when each opens.

## Signal chain

```
In → DC blocker → Input → EQ → Glue compressor → Stereo (Low Mono, Width)
   → True-peak limiter → [master meters] → Match / Bypass → Dither → Out
```

Every section switches in and out with a 10 ms crossfade, and every level, frequency and
gain glides (≈10 ms), so automation and switching don't click. **Bypass** crossfades to
the dry input, delayed by the machine's latency, so A/B comparisons stay aligned.

- **DC blocker:** a 5 Hz second-order high-pass at the input, always on. It is −0.02 dB at
  20 Hz and only shifts phase slightly above that (see Tests).
- **Match (level-matched A/B):**
  - The machine keeps a running K-weighted loudness of the dry input and of the master
    (2 s averaging, skipping silence below −70 LUFS).
  - While Match is on, whichever of the two is louder is turned **down** to the other's
    loudness, so switching Bypass compares sound, not level.
  - Nothing is ever turned up, so matching can't push anything over 0 dBFS.
  - The gains glide over about 100 ms.
  - Switch Match off before rendering.
- **Dither:** the very last stage, for when the render is written at 16 or 24 bits.
  - **24-bit** and **16-bit** are TPDF (±1 LSB).
  - **16-bit shaped** adds E-weighted 3-tap noise shaping, which puts the noise where the ear
    is least sensitive: about 14 dB less noise below 4 kHz, more above 15 kHz.
  - In Buzz units, 1.0 is exactly one 16-bit LSB.
  - Leave Dither off unless this machine is the last thing before a 16- or 24-bit file,
    and keep Master's volume at 0 dB so nothing changes the level after it.
  - When the output is digital silence, Buzz receives silence (no dither noise).

- **Input:** trim, −24 … +24 dB in 0.1 dB steps.
- **EQ:**
  - **Low Cut:** 10–250 Hz or Off.
    - **Slope:** 24 dB/oct (4th-order Butterworth, the default) or 12 dB/oct.
    - Switching the slope crossfades.
  - **Low shelf:** 25–400 Hz.
    - **Low Shape:** **Clean** is a Q 0.707 shelf with no overshoot. **Vintage** raises the
      corner's Q to 1.2: at +6 dB that gives about +0.8 dB of bump below the corner and
      about −0.9 dB just above it, like a passive network. Switching morphs smoothly.
  - **Low Dip:** a broad bell cut (Q 0.9), 0–6 dB, centred at **3 × Low Freq** so it
    follows the shelf (Low Freq 60 Hz → dip at 180 Hz). Combined with a low-shelf boost,
    it lifts the deep bass and cleans up the low mids, for example Low Freq 57 Hz,
    +6 dB Vintage and Low Dip 4 dB: +6.7 dB at 30 Hz and −4.4 dB at 180 Hz. It is unity
    when Off, and switches off with the EQ.
  - **High shelf:** 1.25–20 kHz.
  - Filter frequencies are kept below 0.45 × the sample rate.
  - Both shelves are ±12 dB with slope S = 1.
  - **Tilt:** first-order, ±6 dB end to end around 1 kHz. For example, +6 means lows −3 dB and highs +3 dB.
  - The filters are state-variable (trapezoidal) with per-sample coefficient ramps, so
    frequencies can be swept live.
- **Glue compressor:**
  - Feed-forward and stereo-linked, with an RMS detector (5 ms, louder side) and a 6 dB soft knee.
  - **Ratio:** 1.5 / 2 / 3 / 4 / 6 / 10 : 1.
  - **Attack:** 0.1 / 0.3 / 1 / 3 / 10 / 30 ms.
  - **Release:** 0.1 / 0.3 / 0.6 / 1.2 s or **Auto**. Auto recovers from short peaks in about
    0.25 s and from sustained compression in about 1 s.
  - **SC HPF:** Off / 60 / 90 / 120 / 150 Hz, so the bass doesn't pump the mix.
  - **Makeup:** 0 … +20 dB.
  - **Comp Mix:** parallel compression, 0–100 %.
- **Stereo:**
  - **Low Mono** makes everything below 40–300 Hz mono. It uses a Linkwitz-Riley
    (LR4) crossover on the side channel, with the matching all-pass on the mid, so the
    image above the crossover is untouched (M/S phase match better than −70 dB at 1 kHz).
  - **Width:** 0–200 % (0 = mono, 100 = unchanged).
- **True-peak limiter:**
  - **Lim Gain:** 0 … +24 dB into the limiter.
  - **Ceiling:** −6.0 … 0.0 dBTP, default −1.0.
  - **Lim Release:** 10–1000 ms.
  - Uses a 2 ms look-ahead.
  - The detector is 8× oversampled with a 32-tap Kaiser-windowed sinc, stricter than
    the meter's BS.1770 interpolator, so cymbals and noise don't sneak inter-sample peaks
    past the ceiling. The gain is a sliding-minimum plus box-filter envelope, which
    guarantees sample peaks never exceed the ceiling. A sample-level clamp at the ceiling
    is the final safety net.

### Latency

The latency is 2 ms of look-ahead plus 15 samples of detector delay: **111 samples at
48 kHz** and **103 at 44.1 kHz**. It is constant, including with the limiter off or
bypassed, and it is reported through `GetLatency()` for delay compensation.

## Parameters

| # | Name | Range | Default |
|---|---|---|---|
| 0 | Input | −24 … +24 dB (0.1 dB) | 0 dB |
| 1 | EQ | switch | On |
| 2 | Low Cut | Off, 10–250 Hz | Off |
| 3 | Low Freq | 25–400 Hz | 100 Hz |
| 4 | Low Gain | ±12 dB (0.1 dB) | 0 dB |
| 5 | High Freq | 1.25–20 kHz | 10 kHz |
| 6 | High Gain | ±12 dB (0.1 dB) | 0 dB |
| 7 | Tilt | ±6 dB (0.1 dB) | 0 dB |
| 8 | Comp | switch | Off |
| 9 | Threshold | −40 … 0 dB (0.5 dB) | −10 dB |
| 10 | Ratio | 1.5, 2, 3, 4, 6, 10 : 1 | 2:1 |
| 11 | Attack | 0.1 … 30 ms (6 steps) | 10 ms |
| 12 | Release | 0.1, 0.3, 0.6, 1.2 s, Auto | Auto |
| 13 | SC HPF | Off, 60, 90, 120, 150 Hz | Off |
| 14 | Makeup | 0 … +20 dB (0.1 dB) | 0 dB |
| 15 | Comp Mix | 0–100 % wet | 100 % |
| 16 | Low Mono | Off, 40–300 Hz | Off |
| 17 | Width | 0–200 % | 100 % |
| 18 | Limiter | switch | On |
| 19 | Lim Gain | 0 … +24 dB (0.1 dB) | 0 dB |
| 20 | Ceiling | −6.0 … 0.0 dBTP (0.1 dB) | −1.0 dBTP |
| 21 | Lim Release | 10–1000 ms | 100 ms |
| 22 | Bypass | switch | Off |
| 23 | Slope | 12, 24 dB/oct | 24 dB/oct |
| 24 | Match | switch | Off |
| 25 | Dither | Off, 24-bit, 16-bit, 16-bit shaped | Off |
| 26 | Low Dip | Off, 0.1–6.0 dB (0.1 dB), at 3 × Low Freq | Off |
| 27 | Low Shape | Clean, Vintage | Clean |

With the defaults, the machine is transparent until the signal reaches the −1 dBTP ceiling.
The output is a delayed copy of the input apart from the DC blocker, which has a flat level
response above 20 Hz and a small phase shift.

## Meter link (for the GUI)

The link works like Pedal Gain Multi N's: independent reader slots (0 = panel, 1–3 = meter
windows), so readers never steal each other's peaks. The GUI sends `int32 1, int32 slot`
through `SendGUIMessage`.

The reply (protocol v2) is `int32 version, int32 sampleRate, int32 latency`, then these
floats:

- input sample peak L/R
- input mean square L/R
- output true peak L/R (4×, BS.1770)
- output mean square L/R
- max compressor GR (dB)
- max limiter GR (dB)
- match (LU): master loudness minus dry loudness (new in v2)

The output values are measured on the master, before Bypass, Match and Dither. Then come `int32 B, int32 dropped` and B × 100 ms K-weighted output block energies, and
finally the output correlation means LL, LR, RR. Reading a slot resets it.

The GUI derives momentary, short-term, gated integrated loudness and **LRA** (EBU Tech
3342) from the blocks.

## Building

Install Visual Studio 2022 or later with the **Desktop development with C++** and
**.NET desktop development** workloads. Then, from a Developer PowerShell for VS
(elevated if Buzz is under Program Files):

```powershell
msbuild native\PedalMasterN.vcxproj /p:Configuration=Release /p:Platform=Win32
dotnet build gui\PedalMasterN.GUI.csproj -c Release
```

**Close Buzz before building.** Buzz keeps both DLLs open while it runs, and a failed
copy now fails the build instead of leaving the old DLL in place.

The two builds deploy `Pedal Master N.dll` and `Pedal Master N.GUI.dll` to
`$(BuzzDir)\Gear\Effects`. The GUI build needs `BuzzGUI.Interfaces.dll` from the Buzz
folder and, on its first run, internet access for the .NET Framework reference-assembly
package.

- The build is 32-bit only; other platforms fail with an error.
- `MachineInterface.h` (MI_VERSION 66) is downloaded on the first build from a pinned
  commit.
- Only `Pedal Master N.dll` is deployed, to `$(BuzzDir)\Gear\Effects`. `BuzzDir`
  defaults to `C:\Program Files (x86)\Jeskola\Buzz`; override it with `/p:BuzzDir=…`.
- Release builds produce no `.pdb`.

**.NET rule:** the GUI targets `net48`. That is the documented Buzz 1503 exception to the
".NET 10 or higher" rule. Its `.csproj` carries the mandatory `DebugType none` /
`DebugSymbols false` / `GenerateDependencyFile false`, and only the `.dll` is deployed.

## Tests (sandbox, before Buzz)

`tests/run_all.sh` compiles the machine against the real header with g++ and drives
`Work()` directly. Python/numpy scripts check the results against an ideal 16×
oversampled reference.

| Check | Result |
|---|---|
| Transparency at defaults | delayed copy of the input apart from the DC blocker's phase shift: residual −43 dB at 1 kHz, −33 dB at 331 Hz (phase only; level flat) |
| DC blocker | −3.0 dB at 5 Hz, −0.26 dB at 10 Hz, −0.02 dB at 20 Hz; a 0.1 FS DC offset is removed completely |
| Low Cut 30 Hz, 24 dB/oct | −49.3 / −24.4 / −3.1 / −0.02 dB at 7.5 / 15 / 30 / 60 Hz (12 dB/oct: −25.2 / −12.5 / −3.1 / −0.27) |
| Match: +11.1 LU louder master; −8 LU quieter master | A/B difference 0.02 LU and 0.00 LU; nothing is ever raised |
| Dither | output exactly on the 16-bit / 24-bit grid; 16-bit shaped: 14 dB less noise below 4 kHz than flat TPDF; a −90 dBFS sine keeps no measurable harmonics |
| Bypass | delayed dry input |
| Tilt ±6 | −3.0 / 0.0 / +3.0 dB at 20 Hz / 1 kHz / 20 kHz |
| Shelves | RBJ-exact: +6 dB shelf reads +5.97 dB well below 100 Hz and +3.0 dB at 100 Hz |
| Low Dip, Vintage shape and the two together, 44.1 and 48 kHz | match the filter design within 0.017 dB (the remainder is the DC blocker at 20 Hz); the dip centre follows Low Freq exactly (100 Hz → 300 Hz) |
| v0.4.0 against v0.3.1, 9 scenarios including EQ settings and sweeps | identical output, sample for sample |
| Switching Low Shape, Low Dip Off → 6 dB, sweeping Low Freq with the dip on | no clicks |
| Low Mono 120 Hz | side −48 dB at 30 Hz, −6 dB at 120 Hz, 0 dB at 1 kHz; mid 0.0 dB everywhere |
| Width 0 / 200 | side −∞ / +6.02 dB |
| Limiter, sines (1 k, fs/4, 0.4 fs) at up to 19 dB GR | ≤ −1.00 dBTP, except 0.4 fs at +0.26 dB |
| Limiter, drums + bass at 24 dB GR | +0.03 dB over the ceiling at most |
| Limiter, white noise at 20 dB GR (worst case) | +0.21 dB over the ceiling at most |
| Compressor static curve | within 0.04 dB of theory |
| Mix 50 % + makeup 6 dB | within 0.02 dB |
| EBU 3341 cases 1–5, output meter blocks | −22.99, −32.99, −23.01, −23.01, −22.98 LUFS at 44.1 and 48 kHz |
| EBU 3342 cases 1–4, LRA reference maths | 10.0, 5.0, 20.0, 15.0 LU |
| Correlation | +1.000 / −1.000 / −0.003 |
| Switching every section, sweeping every frequency | no clicks (Δ² ratio ≈ the level change) |
| GUI loudness class (C#, run under Mono) on the machine's own blocks | EBU 3341 I: −22.99, −32.99, −23.01, −23.01, −22.98; EBU 3342 LRA: 10.0, 5.0, 20.0, 15.0 |
| GUI | compiles cleanly against WPF / BuzzGUI stand-ins (layout and look need a live check) |
| v0.3.1 against v0.3.0, 7 scenarios, including switching the limiter and compressor mid-song | identical output apart from float rounding (at most 0.012 of a 16-bit step, −129 dBFS) |
| Dither clamp, output driven to 1.2 × full scale | 16-bit: +32767 / −32768; 24-bit: +32767.996 / −32768 |
| CPU (64-bit sandbox build, file I/O included) | about 1.3–1.7 % of one core at defaults and 1.8–2.3 % with everything on (v0.3.0: 3.3 % and 3.6 %); v0.4.0 adds about 0.1 % |

## Verify in Buzz 1503 (v0.4.0)

1. **Panel:** the EQ section shows **Low Dip** (reading Off) and **Low Shape** (reading
   Clean) under Low Gain, and nothing overlaps.
2. **Old songs:** a song saved with v0.3 sounds exactly as before.
3. **Low Dip:** with music playing and Low Freq at 60 Hz, raise Low Dip to about 4 dB.
   The low mids thin out, around 180 Hz, without losing the bass. Then add Low Gain
   +4 to +6 dB: the bass gets bigger without getting boomy.
4. **Low Shape:** with a Low Gain boost, switch between Clean and Vintage. Vintage sounds
   slightly rounder and tighter, and switching doesn't click.

v0.3.0 was verified live: Match, → T, the Low Cut changes and both 16-bit dither modes,
including the noise-shaping spectrum measured in a real render.

## License

MIT, as for Pedal Gain Multi N. `MachineInterface.h` (© Oskari Tammelin) is not included.
It is fetched at build time.
