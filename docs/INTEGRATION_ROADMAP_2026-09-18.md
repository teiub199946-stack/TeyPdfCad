# TeyPdfCad integration roadmap — 2026-09-18

This document records how the Codex 2026-09-18 work is preserved without mixing unrelated runtime risk into one merge.

## Preserved branches / PRs

1. PR #17 — VectorText scaffold
   - fail-closed vector glyph contracts and tests
   - real 203/212/168 fixture remains intentionally unrecognized
   - next gate: rotation-aware real-glyph recognition and runtime wiring

2. PR #18 — Web platform
   - HTTP API, bounded queue, worker lifecycle, artifact storage/retention
   - AutoCAD process bridge protocol v1 and tests
   - future website/backend foundation
   - does not claim Core Console production acceptance

3. PR #19 — AutoCAD worker host
   - plugin-side non-interactive worker commands and bridge runtime settings
   - stacked on Web platform
   - GUI runtime acceptance remains required

4. PR #20 — Sheet semantic core
   - CAD-neutral A3/page/title-block semantics
   - excludes Paper Space writer runtime risk

5. PR #21 — Sheet runtime spike
   - preserves AutoCAD SheetLayoutWriter and runtime settings
   - known eNotInDatabase viewport/frame defect
   - must remain a spike until real saved/reopened DWG acceptance passes

6. PR #22 — Developer tooling
   - AutoCAD batch/smoke/package preparation scripts

## Product architecture

Browser / client
  -> HTTP API
  -> object/artifact storage
  -> bounded job queue
  -> conversion worker
  -> TeyPdfCad conversion engine
  -> validated native DWG
  -> download

The conversion engine remains independent from the Web layer. TeyPdfCad.Core must stay CAD-neutral.

## Converter roadmap

Phase A — finish current dimension path
- real 203 / 212 / 168 vector glyph acceptance
- rotation-aware VectorTextRecognizer
- text augmentation upstream of SemanticReconstructionEngine
- native DIMENSION Measurement validation

Phase B — richer primitive document
- preserve stroke width, dash pattern, color/fill, transforms, page/bounds and provenance
- direct PDF primitive reader later replaces PDFIMPORT

Phase C — semantic CAD objects
- lineweight / linetype
- axes / centerlines
- leaders / callouts
- circles / arcs
- hatches
- repeated symbols -> blocks
- title block / sheet semantics
- tables where evidence is sufficient

Phase D — server production
- direct or licensed DWG writer path for scalable workers
- object storage
- persistent queue / durable job state
- authentication, quotas, observability and billing integration
- horizontal worker scaling

## Non-negotiable gates
- no threshold softening to make metrics green
- no expected-label changes for convenience
- preserve source provenance
- fail closed on ambiguous semantic recognition
- real AutoCAD runtime tests remain mandatory for AutoCAD-host features
- website infrastructure must not dictate or contaminate Core recognition architecture
