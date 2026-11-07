# mmd2gltf-cs

[日本語](README.md) | **English**

> **The mmd2gltf ecosystem** — Convert an MMD model (PMX/VMD) to `.glb` once, and you can bring it into **Unity, Unreal Engine 5 or Blender** with its physics (swaying hair and cloth) and materials (toon / sphere maps) intact. MMD-specific data that glTF cannot express is kept with its original values in `extras.mmd`, and each importer reads it to rebuild the model on the engine side.
>
> ```
> PMX / VMD
>    │  convert (mmd2gltf-gui or mmd2gltf-cs)
>    ▼
> .glb + extras.mmd   ← you convert only once
>    │
>    ├─▶ Unity   … mmd2gltf-unity-physics-importer
>    ├─▶ UE5     … mmd2gltf-ue5-physics-importer
>    └─▶ Blender … mmd2gltf-blender-physics-importer
> ```
>
> | Goal | What to use |
> |---|---|
> | Convert first (Windows EXE available) | [mmd2gltf-gui](https://github.com/masaka1024/mmd2gltf-gui) — Python version. GUI / CLI |
> | Convert (C# version, built-in physics baking) | [mmd2gltf-cs](https://github.com/masaka1024/mmd2gltf-cs) — same output format as gui. Bakes swaying parts with our own engine |
> | Run it in Unity | [mmd2gltf-unity-physics-importer](https://github.com/masaka1024/mmd2gltf-unity-physics-importer) — Editor extension. Ships a Bullet-compatible engine (does not use PhysX) |
> | Run it in Unreal Engine 5 | [mmd2gltf-ue5-physics-importer](https://github.com/masaka1024/mmd2gltf-ue5-physics-importer) — C++ plugin. A C++ port of the same engine (does not use Chaos) |
> | Run it in Blender | `mmd2gltf-blender-physics-importer` (in preparation) — add-on. Wired to Blender's built-in Bullet |
> | (Library) the physics engine itself | [mmd2gltf-cs-physics](https://github.com/masaka1024/mmd2gltf-cs-physics) — the Bullet 2.75-compatible engine used by cs / Unity / UE5. Not used directly |
> | (Alternative route) export glTF/FBX from Blender's mmd_tools | [mmd-to-gltf-exporter](https://github.com/masaka1024/mmd-to-gltf-exporter) — no `extras.mmd` (physics is not rebuilt) |
>
> Unity and UE5 use the same engine, so swaying behaves identically in both. The Blender version runs on Blender's built-in Bullet, so the motion is close but not identical.
>
> **This repository**: the **C# version** (successor) of the converter. Its output has the same format as gui, and it can bake swaying physics with mmd2gltf-cs-physics.

An MMD (PMX/VMD) → glTF 2.0 converter, **C# version**.
The physics of swaying parts (hair, skirts, ribbons, etc.) is computed by our own
Bullet-compatible engine, [mmd2gltf-cs-physics](https://github.com/masaka1024/mmd2gltf-cs-physics),
and baked into the animation.

This is an unofficial, independent implementation, not affiliated with MMD/MikuMikuDance or PmxEditor.

## Download

Get the file for your platform from [Releases](https://github.com/masaka1024/mmd2gltf-cs/releases).
None of them require .NET to be installed.

| Platform | File | Contents |
|---|---|---|
| Windows (x64) | `mmd2gltf-cs-<ver>-win-x64.zip` | `mmd2gltf-gui.exe` / `mmd2gltf.exe` / `physbake.exe` |
| macOS (Apple Silicon) | `mmd2gltf-cs-<ver>-osx-arm64.dmg` | `mmd2gltf.app` and `cli/` (`mmd2gltf` / `physbake`) |
| macOS (Intel) | `mmd2gltf-cs-<ver>-osx-x64.dmg` | same as above |

On macOS, drag `mmd2gltf.app` into `Applications`.
The app is not notarized by Apple, so the first time, launch it with right-click → "Open".

## Viewing the converted .glb

You can check the result in [FacetGL](https://github.com/masaka1024/facetgl) **as it looks in MMD**,
using the materials stored in `extras.mmd` (toon, sphere map, edge).
Ordinary glTF viewers show a flat unlit model, but FacetGL rebuilds the shading from
`extras.mmd` using the same formulas as MMD.

- **Web version: <https://masaka1024.github.io/facetgl/>** — no install. Just drop the `.glb` into your browser.
  The file is read only inside the browser and is never uploaded anywhere
- **Desktop version (Windows / macOS)**: [Releases](https://github.com/masaka1024/facetgl/releases)

A `.glb` with motion plays back as is. The "MMD view" button switches between the standard glTF
shading and the MMD look for comparison, and the info panel shows what is in `extras.mmd`
(MMD materials, bones, morphs, number of rigid bodies / joints, etc.).
If you share the URL or file of a converted `.glb`, others can view it with the MMD look too.

## Why a rewrite

The earlier Python implementation, [mmd2gltf-gui](https://github.com/masaka1024/mmd2gltf-gui),
**is kept as it is, and this repository does not touch it at all**. There are two reasons for the rewrite:

1. **Replace the physics with our own engine.**
   The Python version's physics baking was a custom PBD/XPBD implementation (`bake_hair.py`, about 3,900 lines)
   that needed about 35 tuning options adjusted by hand.
   Our engine takes the PMX rigid bodies / joints as input and solves them directly,
   so the per-model knob tweaking is no longer needed at all.
   It is not a perfect reproduction of MMD / PMX Editor, though (→ [Physics fidelity](#physics-fidelity)).

2. **Stop maintaining the coordinate conventions twice.**
   The Python version kept the MMD↔glTF conventions — `(x,y,-z)` / `quat(-x,-y,z,w)` / YXZ Euler —
   separately from the engine. This duplication caused
   **a Z-mirroring bug** and **a ZYX/YXZ Euler-order bug** in the past.
   Referencing the engine directly as a library removes this layer structurally.

## Status

**Stages 1–4 are complete.** Both PMX→glTF conversion and physics baking run entirely in C#,
with no dependency on the Python version (the comparison scripts in `tools/` are kept to verify
the output against the Python version).

| Stage | What | Status |
|---|---|---|
| 1 | `physbake`: bake our engine's physics into a GLB's animation | ✅ Working |
| 2a | Texture processing (replacing PIL) | ✅ Matches the Python version |
| 2b | PMX / VMD parsers | ✅ Matches the Python version |
| 2c | FK/IK animation baking | ✅ Matches the Python version |
| 2d | glTF writer and the converter itself | ✅ Matches the Python version |
| 3 | Merge convert and physbake into one command | ✅ Working |
| 4 | GUI (Avalonia / Windows, macOS, Linux) | ✅ Working (tested on real Windows and macOS machines) |

```
src/
  Mmd2Gltf.Core/       library
    Pmx/               PMX parser
    Vmd/               VMD parser
    Animation/         FK/IK baking, MMD math
    Textures/          texture decoding and alpha handling
    Gltf/              glTF builder / GLB read & write
    Conversion/        the PMX -> glTF converter
    Physics/           physics baking with our engine
    MmdConvert.cs      MMD <-> glTF coordinate conventions (only here)
  Mmd2Gltf.Cli/        mmd2gltf CLI (convert + dumps for verification)
  Mmd2Gltf.PhysBake/   physbake CLI
  Mmd2Gltf.Gui/        GUI (Avalonia)
external/mmd-physics/  physics engine (submodule)
tools/                 scripts to compare against the Python version
```

This order puts "always have something that works" first.
Stage 1 is the only genuinely new part of this project (engine → glTF keyframes);
stage 2 had a reference implementation in the Python version, so it was ported while comparing outputs.

## Build and run

```bash
git clone --recursive https://github.com/masaka1024/mmd2gltf-cs.git
cd mmd2gltf-cs
dotnet build -c Release src/Mmd2Gltf.PhysBake
```

If you forgot `--recursive`, run `git submodule update --init`.

> Both the `mmd2gltf-cs-physics` submodule and this repository are public,
> so `clone --recursive` works without authentication.
> (If either one is ever made private again, note that a private submodule
>  makes other people's `clone --recursive` stop when fetching the submodule.)

### Release builds

Pushing a tag such as `v0.2.0` makes GitHub Actions
([`.github/workflows/release.yml`](.github/workflows/release.yml))
build the Windows zip and the macOS dmg (Apple Silicon / Intel) together
and attach them to that tag's Release (creating a draft Release if there is none).
The version comes from `<Version>` in the csproj files, so bump it before tagging.
The same build also runs on pull requests, with the outputs attached to the run.

To build locally:

```powershell
pwsh tools/make_release.ps1                # Windows: zip in artifacts/
```

```bash
tools/make_macos_app.sh osx-arm64          # macOS: dmg containing the .app in artifacts/
tools/make_macos_app.sh osx-x64
```

Both publish self-contained, so users do not need .NET installed.
The macOS build wraps the GUI in a `.app` (with icon, ad-hoc signed) and puts the CLI in `cli/`.
`make_macos_app.sh` uses sips / iconutil / hdiutil, so it only runs on macOS.

The icon is `assets/mmd2gltf.ico`: the Python version's (mmd2gltf-gui) icon with a C# badge added.
The master is `assets/mmd2gltf.png` (1024px).

### Checking that the output contains motion

```bash
pwsh tools/glbcheck.ps1 -Glb out.glb
```

Reads the accessors of the output GLB and checks whether each channel's values actually change.
Exit codes are 0 = has motion / 2 = completely static (T-pose) / 3 = barely moves,
so you can plug it directly into pre-release checks or CI.

★The converter's own log (`bone animation: N channels`) is not enough. Even when channels and
keyframes are written, the model stays in a T-pose if every value is the same. This has actually
happened: the input VMD was empty, yet the log looked normal.

### GUI

```bash
dotnet run --project src/Mmd2Gltf.Gui
```

Pick the files (drag & drop works), choose the options, convert. That's it.
Japanese / English is chosen from the OS locale at startup and can be switched at the top right.

It uses Avalonia (MIT), so the same code runs on Windows / macOS / Linux.
All dependencies are managed (ImageSharp / Pfim / the engine itself), with no native dependencies.
**Tested on real Windows and macOS machines** (2026-08-11). Linux is untested.

For distribution builds, see "Release builds" above.

### CLI

```bash
# convert + bake swaying physics in one command
mmd2gltf convert model.pmx --vmd motion.vmd --bake-physics -o model.glb
```

`--bake-physics` runs the engine during conversion, so no intermediate file is created.
To add physics to an existing GLB afterwards (such as output from the Python version), use `physbake`:

```bash
mmd2gltf convert model.pmx --vmd motion.vmd -o model.glb
physbake model.glb -o model_phys.glb
```

Run `mmd2gltf convert --help` / `physbake --help` for all options (the CLI messages are in Japanese).

Main `physbake` options:

| Option | Default | Meaning |
|---|---|---|
| `--warmup <sec>` | 2.0 | Idle run in the frame-0 pose to discard the initial transient |
| `--sim-hz <Hz>` | 30 | Internal fixed step rate |
| `--substeps <N>` | 4 | Substeps per step |
| `--no-bone-merge` | — | Disable PMX mode 2 (physics + bone position alignment) |
| `--dry-run` | — | Print statistics only, without writing the GLB |

The default 30Hz × 4 substeps gives an effective step of 1/120 s. The engine raised substeps from 2 to 4
to prevent penetration (physics 5396f63), so it solves twice as finely as the Unity side's default (1/60 × 1 substep).
The engine owns these defaults, so the values actually used are recorded in
`extras.physicsBake` of the output GLB.

## How the engine is handled (important)

The physics engine lives **only** in `external/mmd-physics` (submodule).
Do not copy its source into this repository.
The engine already exists in three places — the physics repository, the importer repository and
a Unity project — and they have to be kept in sync. Do not create a fourth.

To update the engine, advance the submodule and commit:

```bash
git -C external/mmd-physics pull origin main
git add external/mmd-physics && git commit -m "engine: follow upstream"
```

## Self-checks

Every time, `physbake` compares **the FK result of the bind pose** with
**the bone positions the engine read from the GLB through a separate path**,
and stops if they differ by more than 1e-2.
If the coordinate conventions or the bone hierarchy mapping are broken, every number after that is meaningless.
In measurements, the error is 0 on all 4 models (a test model / model A / model B / model C).

After baking, it reports the following two values in MMD units. The maximum catches momentary spikes,
so the median and p90 are shown alongside:

- **Displacement by physics** — the difference between the physics result and the FK world position. How much the physics moved things.
- **Link stretch** — `|local translation| - |rest local translation|`.
  Small when constraints are working. However, on models whose joints allow wide translation
  (e.g. model B's skirt, ±0.5) it legitimately becomes large, so do not judge by a threshold alone.

### Comparing with the Python version

`tools/` has scripts that compare the Python and C# outputs for the same input.
They were run at every stage of the port to confirm zero differences.

```bash
pwsh tools/compare_all.ps1     -Work <work folder>   # PMX / VMD parse results
pwsh tools/compare_anim.ps1    -Work <work folder>   # FK/IK baking results
pwsh tools/compare_convert.ps1 -Work <work folder>   # the whole converted GLB
```

※Save `.ps1` files as UTF-8 **with BOM**. Windows PowerShell 5.1 reads a `.ps1` without a BOM
as ANSI, which causes parse errors on Japanese paths.

Results (2026-08-11):

| Target | Coverage | Result |
|---|---|---|
| PMX parser | 13 models | 0 differences |
| VMD parser | 5 motions | 0 differences |
| FK/IK baking | 7 cases | 0 differences |
| Textures | 12 models, 248 images | 0 differences |
| Whole GLB | 14 cases | 0 differences |

The GLB comparison (`tools/glbdiff.py`) lets only three kinds of difference through, all with known causes:

- **1 ULP of float32 in accessors** — the few-ULP float64 differences left in FK/IK baking
  (measured max 1.3e-15 relative) occasionally cross a float32 rounding boundary.
  5 values on model C, max 8.6e-8 relative. That is the resolution of float32 itself.
- **baseColorFactor ~1e-4** — the fallback color of sphere-map-only materials.
  It shrinks the texture to 32x32 and averages it, and the interpolation differs between PIL and ImageSharp
  (model A, material 12/13).
- **1/255 in images** — converting 16-bit PNGs to 8-bit. PIL truncates, ImageSharp scales
  (the latter is the correct conversion).

Integer accessors (indices, JOINTS_0, etc.) are required to match exactly.

### Comparing texture processing

`tools/` has scripts that compare the Python and C# versions.

```bash
mmd2gltf texdump <model folder>... -o cs.json --png-dir png_cs
python tools/texdump.py --mmd2gltf ../mmd2gltf-gui <model folder>... -o py.json --png-dir png_py
python tools/texdiff.py py.json cs.json     # classification, statistics, average color
python tools/pngdiff.py png_py png_cs       # pixels of the converted PNGs
```

Across 12 models and 248 textures, classification (opaque/mask/blend), statistics and average color **match exactly**.
PNG pixels also match exactly for 235 of 246 images; the remaining 11 differ by at most 1/255 from the 16-bit → 8-bit
conversion (PIL truncates, ImageSharp scales; the latter is correct, so this is accepted).

### Physics fidelity

We said it "solves the PMX values as they are", but **it is not a perfect reproduction of MMD / PMX Editor**.
As far as we know, three things keep it from moving exactly as the author set it up:

- **The engine is Bullet-compatible, not Bullet itself.**
  There are some known deviations (the order of solving joints and contacts, the contact warm-start factor).
  They are listed in `docs/DESIGN.md` in the submodule.
- **On models with strong springs, a stabilizing clamp kicks in.**
  An explicit spring diverges when k·dt²/m exceeds 1, so the impulse is capped.
  6 of the 13 models used for testing were in this range (e.g. a spring constant of 100000
  on a weight of mass 0.01). To that extent, the swaying does not follow the author's values exactly.
- **PMX Editor's correction layer is not reproduced.**
  After solving the physics, PMX Editor runs post-processing steps — bone position alignment and
  an internal joint-lock calculation. We do not have those.

That said, fidelity has been measured. With model A + a long motion, deep skirt-into-body penetration,
the change in bone direction angles and the VMD statistics are all at the same level as PMX Editor's
baked result (details in the engine repository's investigation notes).
However, **this is the result for one model**, not a guarantee for every model.

### ★Physics does not reproduce frame by frame (it is chaotic)

The swaying physics **changes significantly with an input difference of 1e-7**. Measured (model A + a short motion, 61 frames):

| Comparison | Relative amplitude difference, median | Max |
|---|---|---|
| Merged path vs two-step path | 4.7% | 37% |
| **The same code path (only a 1e-7 input difference)** | **4.0%** | **40%** |

The difference between the two paths is only as large as perturbing the same code by 1e-7.
Freely swaying hair is chaotic, so different paths always drift out of phase.
**Comparing physics channels frame by frame is meaningless.**
Judge by the amplitude comparison in `tools/animcmp.py --stats`
(rotations are measured by rotation angle, not by components, because q and -q are the same pose).

FK channels (bones not driven by physics) can be required to match exactly.

```bash
python tools/animcmp.py a.glb b.glb --stats   # order-independent channel comparison
pwsh tools/check_bake.ps1 -Work <work folder>  # check that 12 models do not diverge
```

Setting the environment variable `PHYS_FK_FLOAT32=1` lowers the merged path's FK input to the same
float32 precision as physbake. This is for isolating where differences come from (the table above was measured with it).

**Always verify with multiple models.** In the past, tuning against model A alone missed four defects
in features model A happens not to use (spring constants, PMX mode 2, compound rotation, swaying curves).
Always include at least one model with springs / mode 2 / compound rotation.

## License

To be decided (planned to match the Python version).
