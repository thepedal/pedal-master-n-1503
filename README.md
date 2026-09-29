# Pedal Master N

Native C++ **mastering machine** for **Jeskola Buzz build 1503 (32-bit)**, built on the
Pedal Gain Multi N v1.7.0 code base. Stereo in → stereo out; put it last in the chain,
just before Master. Buzz 1503 only, installed at `C:\Program Files (x86)\Jeskola\Buzz`.
It is not intended for ReBuzz.

**Status: v0.2.2 — engine and companion GUI.** The engine (v0.1) is verified in Buzz 1503.
The GUI is new in v0.2 and not yet checked on a live install (see *Verify in Buzz 1503*).

## GUI

`Pedal Master N.GUI.dll` sits next to the machine DLL, and Buzz shows it at the top of
the parameter window. All of its controls write through the machine's parameters, so
patterns, undo and saving stay in step.

- **Header:**
  - **METERS** opens the meter window.
  - **BYPASS** lights amber when engaged.
- **Meters:**
  - Input L/R (sample peak) and output L/R (true peak), with a 3 s hold and clip lights.
  - **GR** bars for the compressor and the limiter, 0–24 dB, with a 3 s hold of the
    deepest reduction.
  - A loudness line: **M**, **S**, **I**, **LRA** and correlation.
  - Click the meters to clear holds. Click the loudness line to restart I and LRA.
- **Sections:** INPUT, EQ, STEREO, COMP and LIMITER.
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
    - Max S and max M.
    - Correlation.
  - **TARGET** cycles −23 / −16 / −14 LUFS; **RESET** restarts I, LRA and the maxima.
  - It closes with its machine or the song.

The panel and the window read separate meter slots, so neither takes peaks from the other.
Loudness integration runs from when each opens.

## Signal chain

```
In → Input → EQ → Glue compressor → Stereo (Low Mono, Width) → True-peak limiter → Out
```

Every section switches in and out with a 10 ms crossfade, and every level, frequency and
gain glides (≈10 ms), so automation and switching don't click. **Bypass** crossfades to
the dry input, delayed by the machine's latency, so A/B comparisons stay aligned.

- **Input:** trim, −24 … +24 dB in 0.1 dB steps.
- **EQ:**
  - **Low Cut:** 12 dB/oct Butterworth, 20–250 Hz, or Off.
  - **Low shelf:** 25–400 Hz.
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
| 2 | Low Cut | Off, 20–250 Hz | Off |
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

With the defaults, the machine is transparent (a delayed copy of the input, within
float rounding) until the signal reaches the −1 dBTP ceiling.

## Meter link (for the GUI)

The link works like Pedal Gain Multi N's: independent reader slots (0 = panel, 1–3 = meter
windows), so readers never steal each other's peaks. The GUI sends `int32 1, int32 slot`
through `SendGUIMessage`.

The reply (protocol v1) is `int32 version, int32 sampleRate, int32 latency`, then these
floats:

- input sample peak L/R
- input mean square L/R
- output true peak L/R (4×, BS.1770)
- output mean square L/R
- max compressor GR (dB)
- max limiter GR (dB)

Then come `int32 B, int32 dropped` and B × 100 ms K-weighted output block energies, and
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
| Transparency at defaults | delayed copy of input, error ≤ 0.0005 (−156 dBFS) |
| Bypass | delayed dry input |
| Tilt ±6 | −3.0 / 0.0 / +3.0 dB at 20 Hz / 1 kHz / 20 kHz |
| Shelves, low cut | RBJ-exact: +6 dB shelf reads +5.98 dB well below 100 Hz and +3.03 dB at 100 Hz; low cut −3.0 dB at its corner |
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
| CPU | ≈3 % of one sandbox core with everything on (64-bit sandbox build) |

## Verify in Buzz 1503 (v0.2.0)

1. **Panel:** it appears above the sliders and the parameter window widens to fit it.
   Faders and switches follow the sliders both ways.
2. **Meters:** IN, OUT, GR and the loudness line move. Clicking the meters clears the holds,
   and clicking the loudness line restarts I and LRA.
3. **Meter window:** METERS opens it, and pressing METERS again brings it to the front.
   TARGET cycles and RESET works. The window closes when the machine is deleted or a new
   song is loaded.
4. **Parameter window text:** switches read On/Off, the shelves default to exactly 100 Hz
   and 10.0 kHz, and Comp Mix reads 100%.

## License

MIT, as for Pedal Gain Multi N. `MachineInterface.h` (© Oskari Tammelin) is not included.
It is fetched at build time.
