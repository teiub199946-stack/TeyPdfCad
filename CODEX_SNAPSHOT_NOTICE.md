# Codex 2026-09-18 source snapshot

This branch preserves the Codex working source/docs/tools snapshot supplied by the user.

It is an **archive**, not a merge-ready branch.

Use the isolated integration PRs (#17 onward) for reviewed work. In particular:
- Web/API/worker/bridge is isolated from semantic recognition.
- Sheet semantic Core is isolated from the AutoCAD Paper Space runtime spike.
- The Paper Space viewport path has an unresolved real AutoCAD 2022 `eNotInDatabase` gate.
- VectorTextRecognizer is a scaffold and does not yet pass the real 203/212/168 acceptance case.
- Compiled files under the supplied `artifacts/` directory are intentionally excluded from this Git snapshot.
