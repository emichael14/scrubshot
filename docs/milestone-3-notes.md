# Milestone 3 notes: layer-2 model selection

**Status:** forward notes written 2026-11-01 from spec revision v1.1 (D-029). Not a milestone plan yet; the plan is proposed when Milestone 2 ends. Nothing here is implemented.

## What changed in the spec

Detection has two layers over the same OCR text. Layer 1 is the rule detectors. Layer 2 is a small local named-entity / PII model run through ONNX Runtime, shipped with the app and on by default, with a setting and policy (`Scrubbing.AiLayerEnabled`) to turn it off. Both layers emit hits with confidence scores; the merge step keeps the highest-confidence type per region and sensitivity profiles decide what is scrubbed. Names and addresses are no longer an add-on.

## Model requirements (from the spec)

| Requirement | Target |
| --- | --- |
| Runtime | ONNX Runtime, CPU execution provider only, no GPU |
| Reference hardware | 4-core laptop, 8 GB RAM |
| Latency budget | about 200 ms or less added per typical snip (one screen of text) |
| Size | quantized, under roughly 100 MB on disk, shipped inside installer, portable zip and MSIX |
| License | compatible with GPL-3.0-only; Apache 2.0 or MIT preferred; no usage restrictions, no attribution gates that break silent deployment, no phoning home |
| Entity types | person names, street addresses, unlabeled secrets and credentials at minimum; custom types without retraining preferred |
| Privacy | receives only the OCR text of the current capture; loaded once per process; off the UI thread |

## Selection process (must finish before Milestone 3 starts)

1. **Shortlist 2 or 3 candidates.** Families to evaluate, each to be verified for license, size after quantization and ONNX export before it is counted as a candidate:
   - A zero-shot or prompt-labeled NER model that accepts entity type names at inference time (this is the route to "API key" or "client secret" without retraining). Verify that a small, quantized variant fits the size and latency budget.
   - A compact token-classification model fine-tuned for PII (names, addresses, emails, phone numbers and similar). Fixed label set; custom types would come from layer 1 only.
   - A compact general NER model (person, location, organization) as the minimal baseline for names and addresses.
2. **Build the benchmark harness** in `tests/` against the detection fixture corpus (fake Entra app registrations, Azure portal blades, PowerShell output, emails, HR and intake forms with fake names and addresses, clean images that must produce zero hits). Report per type: precision, recall, F1; and per model: p50 and p95 latency on the reference hardware, cold-load time, on-disk size, peak working set. Include a rules-only baseline so the marginal value of layer 2 is visible.
3. **Present results to the owner** and get the decision through an explicit choice (AskUserQuestion in the build session). Record it in `docs/decisions.md`; record the candidates' licenses and the benchmark table in `docs/detection-catalog.md`.

## Design constraints to carry into the Milestone 3 plan

- `ScrubShot.Detection` gains the model runner behind the same `IDetector` interface; no UI dependency, unit testable with a stub model.
- Tokenization and span-to-word-box mapping are deterministic and tested on synthetic OCR layouts, including spans that cross word boundaries and lines.
- The model file ships as content next to the executable; its hash is checked at load so a swapped file is refused.
- Confidence thresholds per sensitivity level are settings (and therefore policy-enforceable), not constants.
- The "no sensitive data in logs" test covers the model runner: timings and counts only.
- Packaging (Milestone 6) must keep the installer and portable zip within whatever size the owner accepts; the model is the largest single asset.

## Open questions for the owner

- Acceptable total download size for the installer once the model is included.
- Whether the Relaxed sensitivity level should use layer 2 at all, or only when a rule agrees (the current spec text says only when a rule agrees).
- Whether the clipboard monitor (Milestone 4) also runs layer 2 on pasted text, with the same latency budget.
