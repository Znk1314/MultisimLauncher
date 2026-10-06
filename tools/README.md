# Development tools

Everything in this folder is a **developer tool**. None of it is installed with
the launcher, and none of it is needed to build or run it. They exist because
two parts of this project are generated rather than hand-drawn - the icon and
the background - and because guessing pixel positions by eye did not work.

Run any of them with the C# compiler that ships inside Windows; no SDK needed.

---

## Icon

| File | What it does |
|---|---|
| `GenIcon.cs` | **Generates `assets/app.ico`** - the application icon. This is the one you edit to change the icon. |
| `ExtractIconFrames.cs` | Pulls every icon frame out of an executable, by size. `Icon.ExtractAssociatedIcon` only returns one small frame, which is not enough to measure against. |
| `AnalyzeIcon.cs` | Reports where each run and pad in a reference icon sits, in pixels and as a fraction of the badge. |
| `TraceReference.cs` | Same idea, but reports runs and stems as spans. Used to take the geometry off the NI Multisim icon. |
| `OverlayIcons.cs` | Builds a 50/50 overlay and a difference map, and prints an **overlap score** so a change can be judged by a number. |
| `CompareIcons.cs` | Side-by-side sheet. Decodes PNG-stored ICO frames by hand, because `System.Drawing.Icon` silently falls back to a smaller frame. |

### Why the measurement tools exist

An early attempt at copying the reference icon was done by eye. Measured, it
overlapped only **25%** of the mark pixels - it looked similar and was lined up
wrong. With the tools below the overlap is about **66%**, and every run centre
matches the reference to the pixel.

The remaining difference is stroke weight and anti-aliasing, not position.

To regenerate and score the icon:

```powershell
# 1. build the tools
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$ref = 'C:\path\to\reference-frame.png'      # any 256x256 icon frame
foreach ($t in 'GenIcon','OverlayIcons','CompareIcons') {
  & $csc /nologo /target:exe /platform:anycpu /optimize+ `
    /out:"obj\$t.exe" /reference:System.dll /reference:System.Drawing.dll "tools\$t.cs"
}

# 2. regenerate the icon
.\obj\GenIcon.exe assets\app.ico

# 3. score it against the reference
.\obj\OverlayIcons.exe $ref assets\app.ico obj\overlay.png assets\icon-diff.png
.\obj\CompareIcons.exe $ref assets\app.ico assets\icon-compare.png
```

`OverlayIcons` prints the coverage and precision percentages. Treat those as the
score: a change that lowers them is a regression, however good it looks.

To get a reference frame from an installed Multisim:

```powershell
& $csc /nologo /target:exe /out:obj\ExtractIconFrames.exe `
  /reference:System.dll /reference:System.Drawing.dll tools\ExtractIconFrames.cs
.\obj\ExtractIconFrames.exe "C:\Program Files (x86)\National Instruments\Circuit Design Suite 14.3\multisim.exe" obj\frames
```

---

## Background

| File | What it does |
|---|---|
| `GenBackground.cs` | **Generates `assets/background.png`** - the circuit-board artwork behind the UI. |

Generated rather than downloaded so the project carries no third-party image
licence, and so the artwork can be re-tuned by editing the density and hues.

```powershell
& $csc /nologo /target:exe /out:obj\GenBackground.exe `
  /reference:System.dll /reference:System.Drawing.dll tools\GenBackground.cs
.\obj\GenBackground.exe assets\background.png 1920 1200
```

---

## Screenshots and window geometry

| File | What it does |
|---|---|
| `Shot.cs` | Captures the launcher's client area to a PNG, for the README. Declares DPI awareness for itself, because a non-DPI-aware host sees virtualised coordinates and captures the wrong part of the screen. |
| `WindowInfo.cs` | Reports a window's real pixel geometry and the process's DPI awareness context. Written while chasing a 200% display where the layout was being bitmap-scaled. |

---

## A note on the tools being separate programs

They are standalone executables rather than one multi-command tool because each
was written to answer one specific question during the investigation. Merging
them would be tidier, but they are not shipped and each is short enough to read
in one go.
