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

## Structural run

From the repository root:

```powershell
pwsh -File .\tools\draw-order-spike.ps1 -Mode Both
```

The default run writes only to:

```text
C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report
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

The script checks for `acad2022.cfg` beside Core Console, performs a
save/close/open/save cycle, invokes `PNGOUT`, and records the command script,
stdout/stderr, round-tripped DWG, and PNG under the same report directory.
If Core Console or its required configuration is unavailable, the report
records `draw-order-blocked` with the concrete blocker. It does not claim a
visual pass from ACadSharp entity order alone.
