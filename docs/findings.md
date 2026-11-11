# Findings log

Append-only. Newest run first. Every number here is reproducible with the commands shown.

## 2026-02-11 — first full pipeline run

Commands: `extract --doc all`, `corpus fetch/build`, `cluster -–doc W --top-k 40`,
`inventory --doc W`, `analyze --doc W`, `match --doc W`, `significance --doc W --iterations 30`,
`report`.

### Digitisation

| Document | Rows | Glyphs | Notes |
|---|---|---|---|
| W Caractors | 7 text + 1 title | 258 | Title band excluded; 151 components rejected (too short / specks / shadow) |
| B Stick of Joseph | 3 | 50 | Line crops are inverted scans; polarity auto-detected |
| C Cowdery | 7 (auto) | 35 | Latin text included — needs manual regions for the 4 labelled glyphs |
| FW Williams | 9 (auto) | 300 | Same; calibration only |

### Structure tests (W, threshold 0.70, 180 classes)

- **Glyph height shrinks down the page**: slope −10.3 px per row (R² 0.55). This independently
  reproduces the "copyist compressing to fit the sheet" observation.
- **Trailing gap**: one row ends 4.7 glyph-widths short of the block margin, i.e. the writer broke
  the line with room to spare — consistent with the conversation's row-4 observation (row numbering
  differs because the title band is row 1 in the artifacts).
- **Black squares**: 2 solid-square glyphs (aspect 1.0, density 0.85, area ≥ 500). One sits at a line
  start, so a separator role is *not* supported by position.
- **Comb glyphs**: 12 comb/tick candidates; stroke counts are constant within the detected classes,
  so "one character" is favoured over "numeral" *given the current inventory* (this is only as good
  as the clustering).
- **Zipf**: slope −0.43 (R² 0.81) over 180 classes. Too shallow to be informative — it is dominated by
  singleton classes, i.e. by segmentation fragmentation rather than by the writing system.
- **Compression** (11.9 symbols/in², 267,471 corpus words):
  | Scenario | Est. symbols | Words/symbol | Reading |
  |---|---|---|---|
  | 30 sq ft, sealed excluded | 51,408 | 5.2 | logographic/compressed |
  | 58 sq ft, ⅔ sealed | 32,798 | 8.2 | logographic/compressed |
  | 86 sq ft, all inscribed | 147,370 | 1.8 | mixed/syllabic |
  The model favours a symbol-per-word-or-phrase system under the tighter plate-area scenarios and a
  denser system under the generous one. Inputs are historical estimates; treat as a lean.

### Search

- Scanner A: 300 hypotheses, best fingerprint score 1.000 — saturated, as expected; no evidential value.
- Scanner B: 20 passages survive the multi-constraint gate; best 0.837 (span 2000 words).
- Scanner C: alignment coverage up to 0.042 on the best window.
- Scanner D: 24 anchor-dense windows; top concept windows are Alma 3 and 3 Nephi 19.

### Statistics (30 shuffles)

| Quantity | Value |
|---|---|
| Observed best | 0.837 |
| Null mean ± sd | 0.829 ± 0.010 |
| Null max | 0.855 |
| Empirical p | 0.290 |
| z | 0.77 |
| Hypotheses surviving FDR (α=0.01) | 0 |
| Held-out | discovery 0.000 → test 0.000 (no usable cross-half hypothesis) |

**Verdict: no significant match.** The observed score is inside the shuffled-stream distribution.
This is a genuine negative result for the current inventory, not a failure of the harness: the same
harness detects a planted signal with p ≤ 0.05 in the synthetic end-to-end test.

### Why it is negative (ranked by expected impact)

1. **Inventory fragmentation** (180 classes for 258 glyphs) destroys the repetition structure the
   search depends on. Manual review of `out/W/review.html` is the single highest-value next action.
2. Scanner A's saturation means the hypothesis set handed to B is dominated by chance matches against
   frequent phrases; B then has to find structure among noisy constraints.
3. The document may not be a continuous passage at all (untested historical claim).
4. The corresponding passage may be in the lost 116 pages, in which case no search can succeed.

### Next actions (in order)

1. Merge classes by hand in `review.html`; re-run `inventory`, `analyze`, `match`, `significance`.
2. Author `review/regions-C.json` and `review/regions-FW.json` to isolate the four labelled glyphs and
   use them as calibration anchors.
3. Re-run significance with ≥200 shuffles once the inventory is stable.
4. Compare the W and B inventories directly (line 1–3 alignment) to separate copying errors from
   character variants.
