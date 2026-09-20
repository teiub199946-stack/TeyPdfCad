# Spike B: Native text fitting

This spike is isolated from production reader/writer code, `ConversionPipeline`,
and `output/CURRENT`. It creates the same source word in two orientations:
horizontal and rotated 90 degrees.

For each orientation it writes three native `TEXT` candidates:

1. default `TEXT`;
2. `TextHorizontalAlignment.Fit` with insertion point at the expected
   baseline start and `AlignmentPoint` at the expected baseline end; and
3. a candidate with `WidthFactor` calibrated from a deterministic natural-width
   reference written and reopened through ACadSharp.

The fixture metadata, including expected baseline start/end, is written before
any DWG is created. The focused xUnit test saves and reopens every candidate
through ACadSharp and asserts value, height, rotation, insertion point,
alignment, alignment point where applicable, and `WidthFactor` for the
calibrated candidate.

## Structural run

From the repository root:

```powershell
pwsh -File .\tools\text-fit-spike.ps1
```

Artifacts are written only below:

```text
C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-text-fit-report
```

Use `-ArtifactRoot` or `TEYPDFCAD_TEXT_FIT_ARTIFACT_ROOT` to override the
directory. The script records fixture metadata, every candidate DWG SHA-256,
read-back metrics, the focused test log, `report.json`, and `report.md`.

ACadSharp does not provide rendered glyph extents. If its structural
`TextEntity.GetBoundingBox()` natural-width reference is unavailable, the
calibrated candidate preserves the deterministic reference `WidthFactor` and
the report explicitly marks visual calibration as pending; no
`AdvanceWidthPoints` value is used directly as `WidthFactor`.

## Optional AutoCAD render

The script supports separate Core Console save/reopen and render processes:

```powershell
pwsh -File .\tools\text-fit-spike.ps1 `
  -Mode Both `
  -CoreConsolePath 'C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe'
```

This mode is intentionally not run by the normal structural command. If Core
Console is unavailable or not requested, `rendererVersion` is `null` or the
concrete blocker is recorded, and no visual claim is made. A produced PNG is
invocation evidence only; human visual acceptance remains pending.
