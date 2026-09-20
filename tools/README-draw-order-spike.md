# Spike A: DWG draw order

This spike is intentionally isolated from the production reader, writer,
`ConversionPipeline`, semantic recognizers, and `output/CURRENT`.

It creates a deterministic fixture with:

1. a red `LINE`;
2. a white solid `HATCH` covering the line; and
3. black `TEXT` over the hatch.

The xUnit test saves each variant with ACadSharp 3.7.1, reopens it with
`DwgReader`, verifies the entities and colors, and records both the reopened
insertion order and the reopened `BlockRecord.SortEntitiesTable`.

These results are **structural only**. `insertion-order-stable` and a preserved
`SortEntitiesTable` describe serialized DWG state; they do not prove AutoCAD
display order. Both findings remain provisional until an AutoCAD
save/reopen/render pass is available. The spike never infers a visual pass
from ACadSharp entity lists.

## Structural run

From the repository root:

```powershell
pwsh -File .\tools\draw-order-spike.ps1 -Mode Both
```

The default run writes only to:

```text
C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report
```

The root can be overridden explicitly with `-ArtifactRoot` or with
`TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT`. The parameter takes precedence:

```powershell
pwsh -File .\tools\draw-order-spike.ps1 `
  -Mode Both `
  -ArtifactRoot 'C:\controlled\spike-draw-order-report'
```

Expected artifacts include:

- `insertion-order.dwg`;
- `explicit-order.dwg`;
- `read-back-summary.json`;
- `report.json`; and
- `report.md`.

`InsertionOrder` runs only the insertion-order variant, and `ExplicitOrder`
runs only the variant that starts scrambled and then calls
`BlockRecord.CreateSortEntitiesTable()`, `MoveToBottom`, and `MoveToTop`.

## AutoCAD save/reopen/render

ACadSharp read-back is not a renderer substitute. If AutoCAD Core Console is
available, pass its path explicitly:

```powershell
pwsh -File .\tools\draw-order-spike.ps1 `
  -Mode Both `
  -CoreConsolePath 'C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe'
```

The script checks for `acad2022.cfg` beside Core Console and uses two separate
noninteractive processes. Phase 1 opens the copied DWG, runs `QSAVE`, and
exits. Phase 2 starts a new Core Console process with that saved DWG, invokes
`PNGOUT`, saves, and exits. It does not issue `CLOSE` followed by `OPEN`
inside one Core Console script.

The report records source and round-tripped DWG SHA-256 values, renderer file
version, command scripts, stdout/stderr, round-tripped DWG, and PNG where
available. If Core Console is unavailable, renderer version is explicitly
`null` and the concrete blocker is recorded.

If Core Console or its required configuration is unavailable, the report
records `draw-order-blocked` with the concrete blocker. It does not claim a
visual pass from ACadSharp entity order alone. A generated PNG is invocation
evidence, not a claim of human visual acceptance.
