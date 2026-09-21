# Spike A: DWG draw order

- Status: **insertion-order-stable**
- Scope: **structural only**
- Structural finding: **insertion-order-stable** (provisional until an AutoCAD render pass is available)
- ACadSharp package: **3.7.1**
- Insertion order stable after ACadSharp save/reopen: **True**
- Explicit SortEntitiesTable observed after reopen: **True**
- Visual proof: **not claimed**
- Artifact root: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report-final`

> `insertion-order-stable` and the `SortEntitiesTable` result describe serialized DWG structure only. They do not prove AutoCAD display order and remain provisional until AutoCAD save/reopen/render succeeds.

## Variants

- InsertionOrder: SHA-256=39dc2aa8f23732958dd3fe32cb1e80b189e4974cfb1422bc676a1c603892c288; source=[red-line, white-solid-hatch, black-text]; reopened insertion=[red-line, white-solid-hatch, black-text]; reopened sort=[]
- ExplicitOrder: SHA-256=be6b37e32082eb51617d06ece6afb7678fd1afdd34d2b70ec883509c992474b0; source=[black-text, white-solid-hatch, red-line]; reopened insertion=[black-text, white-solid-hatch, red-line]; reopened sort=[black-text, white-solid-hatch, red-line]

## AutoCAD batch/render

- Status: **completed**
- Renderer version: 24.1.51.0.0
- Blocker: none

Visual status remains pending unless a real AutoCAD Core Console invocation produces the recorded render artifact. Even then, the report records invocation evidence only and does not claim human visual acceptance.

ACadSharp save/reopen is recorded separately and is not treated as a substitute for AutoCAD rendering.
