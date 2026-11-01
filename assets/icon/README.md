# ScrubShot icon

Original artwork for the ScrubShot app icon: a small cluster of glossy soap bubbles in the aqua/teal palette. GPL-3.0-only, like the rest of the repository.

| File | Purpose |
| --- | --- |
| `scrubshot.svg` | Source of truth. Used at 32 px and above. |
| `scrubshot-small.svg` | Simplified variant (fewer bubbles, thicker outline) used at 16, 20 and 24 px so the tray icon stays readable. |
| `scrubshot.ico` | Multi-size icon (16, 20, 24, 32, 48, 64, 256) embedded in `ScrubShot.exe` and used for the tray icon. |
| `scrubshot-<size>.png` | Rendered PNGs (16 to 512) for the README, installer and store listings. |
| `scrubshot-preview.png` | Every size on a light and a dark taskbar color, with 3x pixel zoom for the small sizes. |

## Regenerating

The PNGs, the `.ico` and the preview are generated from the two SVGs. Edit the SVGs, then run from the repository root:

```powershell
dotnet run --project tools/IconBuild -- build assets/icon/scrubshot.svg assets/icon/scrubshot-small.svg assets/icon scrubshot
```

Palette: outline `#0D4A45` (a shade of the brand outline `#0B3B38`, chosen so the rim still shows on a dark taskbar), teal `#0E9F8E`, aqua `#14B8A6`, `#2DD4BF`, light aqua `#5EEAD4`, pale `#CCFBF1`, white highlights. Fills are two-stop radial gradients for a glossy, flat-vector look.

Design notes: three bubbles stepping diagonally from large to small. The small variant keeps all three bubbles (the owner's requirement) with a thicker outline and highlights snapped to a 16-unit grid so they land on whole pixels at 16 px. A light halo behind the bubbles was tried for dark taskbars and rejected because it softened the edge on light ones. See `docs/decisions.md` D-028.
