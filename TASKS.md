# Caractors Decipherment — Task Checklist

## Implementation status (updated after the first full run)

All pipeline code is implemented and verified by `dotnet test` (23 tests, including a planted-signal
end-to-end test) and by a full run over the real documents. Remaining work is **manual review**, not
code.

| Phase | Status | Notes |
|---|---|---|
| 0 Repository & contracts | Done | Solution, schemas-as-records, ID convention `W-L03-07`, image SHA-256 manifest |
| 1 `extract` | Done | 7 rows / 258 glyphs (W), 3 rows / 50 glyphs (B); `overlay.png` + `contact-sheet.png` for review |
| 2 `cluster` | Done | 4,780 scored pairs; `review.html` with accept/reject + decisions export |
| 3 `corpus` | Done | 267,471 tokens, 6,605 verses, sanity gates pass, 11 targeted lookups stored |
| 4 Inventory versions | Done | 7 thresholds per document incl. auto (mean + 1.25 σ); `labels-*.json` per version |
| 5 Structure tests | Done | Size drift, trailing gaps, black square, comb glyphs, Zipf, repeat regularity, compression |
| 6 `match` | Done | 4 scanners + bijection, stability re-check, bounded many-to-many alignment |
| 7 `significance` | Done | Shuffled null, empirical p/z, BH-FDR, held-out split; verdicts auto-written |
| 8 `report` | Done | `out/report.html`, run manifests in `out/runs.json` |
| 9 Historical ledger | Done | `docs/claim-ledger.md`, two claims falsified against the corpus |
| Manual: review clusters | **Open** | `out/W/review.html` |
| Manual: label regions for C/FW | **Open** | `review/regions-C.json`, `review/regions-FW.json` |

Key measured results are in `docs/findings.md`; the honest headline is that the current (automatic,
over-fragmented) inventory produces **no significant match** (p = 0.29 vs 30 shuffled controls).

---

Derived from `LLM Conversation.txt` (ChatGPT share, 52 pp.) plus the user's stated preferences:

- **Language:** C# for everything (CLI + libraries + report generation).
- **Corpus:** Book of Mormon as JSON; edition/year is not critical because **punctuation is stripped** during analysis.
- **Fuzzy matching:** Ollama + gemma locally. No vLLM/Docker until measured to be too slow.
- **First milestone:** a CLI *verb* that extracts individual symbol images, plus fuzzy image comparison that proposes likely duplicates for manual review.

Environment already verified on this machine: .NET SDK 10.0.401 (and 9.0.307), Ollama with `gemma4:12b-it-q4_K_M`, `qwen3-vl:8b-instruct-q4_K_M` (vision), `nomic-embed-text`. No Python `cv2`, so image work stays in C#.

**Guiding principle from the conversation:** preserve each document as an independent witness to the underlying inscription. Never merge transcriptions into one "authoritative" sequence, and never collapse visually similar characters before testing whether they are variants.

---

## Phase 0 — Repository, tooling, and data contracts

- [ ] Create solution layout
  - [ ] `src/Bomca.Core` (library: ingestion, segmentation, descriptors, clustering, corpus, matching)
  - [ ] `src/Bomca.Cli` (System.CommandLine verbs)
  - [ ] `tests/Bomca.Core.Tests` (xUnit)
  - [ ] `data/raw/` (source images, committed or Git-LFS), `data/derived/` (gitignored), `out/` (gitignored)
- [ ] Move/record source images into `data/raw/` with stable names: `caractors-whitmer.jpg`, `broadside-stick-of-joseph-1..3.png`, `cowdery-characters.jpg`, `williams-characters.jpg`
- [ ] Emit `data/raw/manifest.json` with SHA-256, byte size, pixel dimensions, and provenance note (JSP / Church History Museum / 1886 Hicks photograph) for every input
- [ ] Pin NuGet deps: `System.CommandLine`, `SixLabors.ImageSharp` (+`.Drawing`), `Microsoft.ML.OnnxRuntime` (only if needed later), `OllamaSharp`, `System.Text.Json`
- [ ] Define and freeze JSON schemas (versioned `schemaVersion` field)
  - [ ] `document.schema.json` — document, witness type, rows, paper size, orientation, provenance, confidence
  - [ ] `line.schema.json` — row index, bounding box, baseline, symbol count
  - [ ] `symbol.schema.json` — **occurrence** ID, document, line, ordinal, bbox, w/h, area, stroke count, crop path, confidence, manual-review flags
  - [ ] `cluster.schema.json` — cluster ID, member occurrences, variant-of edges, score, method, review status
  - [ ] `inventory.schema.json` — canonical character ID → variant occurrences + notes
  - [ ] `corpus.schema.json` — token stream with book/chapter/verse offsets, raw + normalized forms
  - [ ] `match.schema.json` — hypothesis set, candidate passage, score, per-symbol contributions, p-value
- [ ] ID convention (locking this early prevents painful renames)
  - [ ] Occurrence: `{DOC}-L{line:00}-{ordinal:00}` → `W-L03-07`, `B-L01-12`
  - [ ] Document codes: `W` Whitmer Caractors, `B` Stick of Joseph broadside, `C` Cowdery, `FW` Frederick G. Williams, `PL` Phelps 1835 letter, `EA` Egyptian Alphabet (deferred)
  - [ ] Canonical character: `C001`, `C002`, … assigned **only** in Phase 4 after review
- [ ] Decision log file (`docs/decisions.md`): every methodological choice with date + rationale
- [ ] xUnit smoke tests for loaders/serialization so later refactors are safe

**Exit criteria:** `dotnet build` clean, `dotnet test` green, schemas committed, manifest hashes recorded.

---

## Phase 1 — VERB: `bomca extract` (symbol image extraction)

The first real deliverable. Deterministic, reviewable, re-runnable.

- [ ] `bomca extract --doc W --image data/raw/caractors-whitmer.jpg --out out/W`
  - [ ] Load + normalize: EXIF/orientation, optional rotation (`--rotate`), perspective/deskew, illumination flatten (large-radius background division), grayscale, denoise, adaptive/Otsu threshold
  - [ ] Crop to paper: detect page vs background; exclude the frayed/missing corners and the shadowed margin so they never become "symbols"
  - [ ] Mask the handwritten title `Caractors` (manual bbox in `documents.json`) so it is not segmented as characters
  - [ ] Line detection via horizontal projection profile; expose `--line-count 7` and `--line-bands` override for QA. Record each row's bbox and baseline
  - [ ] Symbol segmentation within a row: connected components → **merge components that belong to one glyph** using distance-transform/vertical-gap heuristics. Key tuning knobs: `--merge-gap-px`, `--min-area-px`, `--max-aspect`
  - [ ] Explicit handling of known hard cases (each gets a flag so we can see the failure rather than hide it)
    - [ ] Black-square symbol (recurs ~4×; may be a separator, logogram, or punctuation) — keep as its own glyph class
    - [ ] Tick-mark/combed symbols (multi-stroke): decide per class whether the *whole comb* is one symbol; record stroke count so the "is it a numeral?" test in Phase 5 is possible
    - [ ] Row 4 trailing gap (a symbol would fit) — record the gap as an entity in the output, not empty space
    - [ ] Cramped spacing / progressive size shrink down the page — log per-row median glyph height and inter-symbol gap so drift is measurable
  - [ ] Per-symbol outputs: tight bbox, padded crop PNG (fixed 96×96 canvas, aspect preserved, alpha), `symbols.json`, `rows.json`
  - [ ] QA artifacts: `overlay.png` (boxes + IDs drawn on the page) and `contact-sheet.png` (grid, ID captions) — human review target #1
- [ ] `bomca extract --doc B --image broadside-*.png` × 3 rows (same pipeline; these are clean line crops, so line detection is trivial and merge tuning differs)
- [ ] `bomca extract --doc C --image cowdery-characters.jpg` (4 labeled glyphs; only the *character* regions, not the Latin/Hebrew text)
- [ ] `bomca extract --doc FW --image williams-characters.jpg` (same 4 glyphs, independent copy — needed for the "same shapes, different strokes" test)
- [ ] Override mechanism so review is never destroyed by a re-run: `review/segmentation.csv` with `keep|drop|split|merge` rows keyed by occurrence ID, applied after auto-segmentation
- [ ] `bomca extract --report` prints counts: symbols per row, area stats, rejected-component count, and a diff vs the previous run (regression guard)

**Exit criteria:** 7 clean Caractors rows with every visible glyph boxed exactly once (verified on `overlay.png`), 3 broadside rows boxed, crops legible at 96×96, and a documented list of every glyph that required manual splitting.

---

## Phase 2 — VERB: `bomca cluster` (fuzzy duplicate matching, reviewed by hand)

- [ ] Normalization stage (applies to all descriptors)
  - [ ] Tight-crop to ink, binarize, thin/skeletonize optional, stroke-width normalization, resize to 64×64 and 96×96 variants
  - [ ] Keep both the raw crop and the normalized vector so we can always go back to pixels
- [ ] Classical descriptors (cheap, deterministic, no LLM)
  - [ ] Binary IoU after best of small rotations (±10°) and mirror check
  - [ ] Normalized cross-correlation (grayscale)
  - [ ] Hu moments / Zernike moments distance
  - [ ] Chamfer / distance-transform matching (robust to stroke thickness)
  - [ ] Contour + convexity + hole count + stroke-count features (feeds the "is the comb one glyph?" question)
  - [ ] Perceptual hash (pHash/dHash) as a coarse prefilter to avoid O(n²) heavy compares
- [ ] Combined similarity score with documented weights; output both the score **and** the component breakdown per pair
- [ ] Fuzzy (semantic) layer via Ollama, on a *description* not raw pixels unless the model is a VLM
  - [ ] Note: gemma is text-only — it cannot see PNGs. Two supported modes:
    - [ ] **Mode A (default):** classical metrics cluster; gemma adjudicates borderline pairs from structured textual descriptions (bbox, aspect, stroke count, hole count, component count, moment vector, ASCII/edge summary)
    - [ ] **Mode B (optional):** `qwen3-vl:8b-instruct-q4_K_M` (already installed) describes each crop; embed with `nomic-embed-text`; cluster embeddings
  - [ ] Cache every model response keyed by (model tag, prompt hash, crop hash) in `out/cache/ollama/` so re-runs are free
  - [ ] Batch/parallelism knob, timeout, and graceful degradation to classical-only when Ollama is down
- [ ] Clustering
  - [ ] Agglomerative (average-linkage) with `--threshold`; also emit a *ranked pair list* so the human can review top-N instead of trusting a cut
  - [ ] Distinguish edge types: `same-symbol` vs `variant-of` vs `similar-style-only`. Never auto-merge across documents into one class
  - [ ] Report cluster-size histogram and singleton count — a sane inventory is a few dozen classes, not 300
- [ ] Human review loop
  - [ ] `bomca cluster --review` emits a self-contained `review.html`: side-by-side crops, score, breakdown, click accept/reject/merge/split
  - [ ] Decisions persist to `review/clusters.json` (append-only, human-authored) and are re-applied on every run
  - [ ] Track review progress: "unreviewed pairs remaining"
- [ ] Cross-document comparison (W lines 1–3 vs B lines 1–3)
  - [ ] Symbol-by-symbol similarity table
  - [ ] Order/sequence diff: matches, substitutions, insertions, deletions, spacing changes
  - [ ] Output `align-W-B.json` + human-readable diff — this is the raw material for the "common ancestor" conclusion
- [ ] Literature-style calibration check: do W/B first-three-row similarities reproduce the qualitative claim in the conversation (mostly identical, with a handful of divergent glyphs and omissions)?

**Exit criteria:** a manually reviewed cluster table for the Caractors document; a W↔B alignment artifact; every "these are the same symbol" claim traceable to a reviewed pair, not to an algorithm threshold.

---

## Phase 3 — VERB: `bomca corpus` (Book of Mormon text)

- [ ] `bomca corpus fetch` downloads a JSON edition and records the exact source, commit/tag, and SHA-256 in `manifest.json`
  - [ ] Primary: current LDS-edition JSON (book/chapter/verse structure)
  - [ ] Secondary (cheap to add, useful for the "changes over time" footnote): 1830 edition JSON
- [ ] `bomca corpus normalize` produces two streams per edition: `raw` (with punctuation, for verse display) and `normalized` (lowercase, punctuation stripped, whitespace collapsed, typography folded)
- [ ] Preserve structural offsets so any match can be reported as *book/chapter/verse/word-index* — never as a bare character offset
- [ ] Build indexes in `out/corpus/`
  - [ ] Unigram frequency table
  - [ ] n-gram tables for n = 2…8 with occurrence positions
  - [ ] Inverted index: token → positions (for fast candidate filtering)
- [ ] Sanity targets (fail loudly if violated, since a bad corpus silently destroys every later statistic)
  - [ ] ~6,600 verses, ~270k words
  - [ ] `and it came to pass` in the 1.3k–1.45k range
  - [ ] Coverage check: every book present with expected chapter counts
- [ ] Targeted lookups the conversation specifically needs
  - [ ] Does `interpreter(s) of languages` occur in the body text? (expected: no)
  - [ ] `urim and thummim` occurrences and their punctuation/casing variants
  - [ ] Is `the book of mormon` present only as a title/heading? Locate title page and mark it a separate segment
  - [ ] `brethren, adieu` (Jacob 7:27) and `it grieveth me that I should lose this tree` (Jacob 5:13) — the two Cowdery/Williams anchor phrases; confirm 1830 and current wording differ, and that normalization still matches
- [ ] Compare 1830 vs current: token-level diff stats (this is the "were changes rare enough to ignore?" question, answered with data instead of intuition)
- [ ] `bomca corpus freq --n 4 --min-count 5` prints the ranked phrase table used for hypothesis prioritization (`and it came to pass`, `and behold`, `and it came to pass that`, …)

**Exit criteria:** reproducible corpus build; normalization drops 100% of punctuation; the four lookups above answered with counts and a stored artifact.

---

## Phase 4 — Canonical inventory (post-review)

- [ ] Promote reviewed clusters into `inventory.json`: canonical ID → variant occurrences, per-document incidence, confidence, notes
- [ ] Compute Caractors symbol statistics: class counts, Zipf slope, singleton ratio, positional distribution (line-start/line-end bias)
- [ ] Structural investigations flagged in the conversation (each produces a small artifact, not just an opinion)
  - [ ] Black square: where does it occur (line starts?), what precedes/follows it, is spacing around it anomalous? → separator hypothesis scored
  - [ ] Tick-mark/combed glyphs: does stroke count vary per instance? If it is constant, it is one character; if it varies systematically, it is a numeral system
  - [ ] Row-4 trailing gap and inter-row size drift: plot glyph height and gap vs ordinal; test "freehand copy with word-wrap" vs "traced/mechanical"
  - [ ] Repeated-glyph micro-irregularity test (the frottage question): do supposedly identical glyphs share *stroke-level* irregularities (registration/correlation of skeleton artifacts) or only overall shape? Mechanical transfer ⇒ shared micro-detail
- [ ] Compression estimate v1
  - [ ] Count symbols in the surviving Caractors fragment; estimate glyphs/sq-in from the measured bboxes and row pitches
  - [ ] Plate-area model from Dale (30–86 sq ft inscribed), 6×8 in leaves, sealed-portion scenarios (no sealed / 1/3 / 2/3 unsealed)
  - [ ] Words-per-symbol implied for the whole Book of Mormon (~270k words) under each scenario → `compression.json` with a sensitivity table, explicitly labeled low confidence
  - [ ] Use it to rank writing-system models (alphabetic / syllabic / logographic / mixed) rather than to "prove" one

---

## Phase 5 — Structure tests (before any decipherment search)

- [ ] H1: continuous passage vs character collection
  - [ ] Frequency/rank distribution vs the same-frequency randomized stream
  - [ ] Repeat-distance regularity: are inter-occurrence gaps of frequent glyphs more structured than chance?
  - [ ] n-gram repetitiveness (2–4) vs shuffled control
  - [ ] Verdict recorded as a probability/lean, not a binary
- [ ] H2: writing-system model comparison (same data, different assumptions) — logographic, syllabic, alphabetic, mixed, plus the "one glyph may have several meanings" variant
- [ ] H3: document-genealogy test on W vs B lines 1–3 — pixel-level (shared micro-detail ⇒ mechanical/tracing) vs stroke-level (independent hands) vs sequence-only
- [ ] H4: Cowdery/Williams calibration — do the four labeled glyphs, or their style, appear anywhere in W/B? (Conversation's expectation: no; record the negative result explicitly, because it constrains how much weight to put on the "God / Sons of God / angels / earth" meanings)
- [ ] Write `docs/preregistration.md` **before** Phase 6: hypotheses, search space, thresholds, and what would count as a discovery. This is the main defense against "impressive-looking coincidences".

---

## Phase 6 — VERB: `bomca match` (decipherment search)

- [ ] Input adapters: symbol stream (from inventory) → token stream; English stream (from corpus) → token stream; both pluggable so W, B, and W+B-combined can all be searched
- [ ] Scanner A — relative-distance fingerprints (the user's original idea)
  - [ ] For each glyph with k ≥ 3 occurrences: normalized gap vector `D/ΣD`
  - [ ] Same for candidate phrases at every occurrence
  - [ ] Distance metric over normalized vectors (L1 / cosine / DTW with tolerance for missing occurrences)
  - [ ] Score = glyph rank + phrase rank, refined by fit quality
- [ ] Scanner B — joint multi-glyph constraint search (the part the conversation calls the strongest feature)
  - [ ] Given m hypothesized mappings, search passages where all m patterns co-occur with consistent relative spacing
  - [ ] Score jointly (never multiply independent p-values); reject any mapping that makes another impossible
- [ ] Scanner C — sequence alignment
  - [ ] Pair-HMM or Smith-Waterman with affine gaps and a configurable emission model (1 glyph → 0..n words for logographic; 1 word → 1..n glyphs for alphabetic; both directions must be testable)
  - [ ] Accommodate word-order divergence as a first-class option, not a bug to be tuned away
- [ ] Scanner D — sliding-window semantic probe (the user's 200-word idea)
  - [ ] Anchor concepts: God, Son of God, Sons of God, angels/ministering, earth, interpreters, *Book of Mormon* (title)
  - [ ] Run over the whole text in window sizes 100/200/400; rank windows by anchor density, then check whether the glyph pattern for those anchors appears in the corresponding Caractors segment
  - [ ] Treat the Egyptian-Alphabet/Pure-Language meanings strictly as **candidate associations** with a numeric confidence, never as translations
- [ ] `bomca match --method all` writes one ranked `matches.json` + `matches.html` with per-candidate evidence (which glyphs matched, which gaps matched, which did not)
- [ ] Performance: the search must stay fast enough to iterate. Measure first; if a full sweep exceeds a few minutes, add indexing/short-circuiting/parallelism **before** considering vLLM

---

## Phase 7 — Significance and falsification

- [ ] Randomized controls: ≥1,000 shuffles of the symbol stream preserving class frequencies and length; run the same search; build null distributions for the best score
- [ ] Empirical p-value + FDR/Bonferroni correction for the number of hypotheses actually tested (log it — this number gets embarrassingly large if untracked)
- [ ] Held-out validation: split Caractors into discovery/test sections; a hypothesis must survive the test half
- [ ] Model-comparison scoreboard: every writing-system model on identical inputs, same number of trials, same correction
- [ ] Verdict document with three explicit outcomes: (a) supported match, (b) no significant match, (c) inconclusive due to data limits. All three are publishable results; only (a) claims a decipherment

---

## Phase 8 — Reporting, reproducibility, and the human-facing surface

- [ ] CLI surface, all verbs deterministic and `--json` capable
  - [ ] `extract`, `segment`(debug dumps), `cluster`, `review`, `corpus`, `inventory`, `analyze`, `match`, `significance`, `report`
- [ ] Every run writes `out/runs/{timestamp}/manifest.json`: tool version, git SHA, input hashes, model tags + prompt hashes, RNG seeds, thresholds
- [ ] `bomca report` generates one self-contained HTML: pipeline diagram, inventory contact sheet, cluster table, compression table, structure-test plots, ranked matches with evidence, significance plots, and a findings table labeled Strong / Probable / Plausible / Unproven (mirroring the conversation's hypothesis table)
- [ ] `docs/methodology.md` (what we did and why), `docs/limitations.md` (see risk register), `docs/findings.md` (append-only log with dates)
- [ ] Contingency documented: Ollama unavailable ⇒ classical-only path; missing corpus field ⇒ fail with a precise message; symbol count mismatch after re-extraction ⇒ regression diff must be reviewed before any downstream run

---

## Phase 9 — Historical / documentary cross-checks (thin slice, not a research library)

- [ ] Record the 1886 Hicks glass-plate photograph as context for the original sheet (paper reuse, folding) — cite, don't re-derive
- [ ] Note JSP conclusion that the glyphs likely descended from a Joseph Smith transcription, not a direct Whitmer plate copy (affects interpretation, not the algorithm)
- [ ] Keep the Egyptian Alphabet / Grammar and Alphabet **out of the decipherment corpus**; keep early-character comparisons as an optional `EA` dataset behind a flag, with the "repurposed meanings" caveat attached to every record
- [ ] Phelps 1835 letter (6 glyphs): optional 5th dataset for style/composition comparison only
- [ ] Maintain the claim ledger: each historical claim in `LLM Conversation.txt` marked **Strong evidence / Probable / Plausible / Unproven**, with the specific reason. This prevents the report from inheriting unverified assertions from a chat transcript

---

## Risk register (carry into every report)

- [ ] The surviving transcript may **not** be a continuous passage, so sequential matching could be structurally wrong (→ Phase 5 H1 gates Phase 6)
- [ ] If the passage is in the **lost 116 pages**, no English counterpart exists and no search can succeed (→ state this before running; a null result is then uninformative)
- [ ] Titles/headings are not body text (→ title page treated as a separate segment)
- [ ] Broadside is a **commercial engraving** of a handwriting source: more artistic ≠ more faithful (→ never weight it higher for fidelity)
- [ ] Egyptian-Alphabet meanings may be **1835 re-assignments**, not original semantics (→ candidate associations with confidence, never translations)
- [ ] Translation is not structure-preserving; word order may differ (→ relative distances and alignment, not positional equality)
- [ ] One glyph ≠ one word; one word ≠ one glyph (→ multiple emission models, all tested)
- [ ] Similar shapes may be **variants or distinct** characters (→ never collapse before testing)
- [ ] Large hypothesis spaces manufacture impressive coincidences (→ Phase 7 controls are mandatory, not optional)

---

## Milestone sequencing

1. **M1 — Extraction + fuzzy dedupe, manually reviewed** (Phases 1–2). This is the requested first step and the only thing needed to start reviewing real data.
2. **M2 — Corpus + corpus sanity** (Phase 3). Independent of M1; can be built in parallel.
3. **M3 — Reviewed inventory + structure tests** (Phases 4–5). Produces the first scientifically meaningful statements (is it continuous? what writing-system class?).
4. **M4 — Search + significance** (Phases 6–7). Only runs after M3's preregistration is committed.
5. **M5 — Report** (Phase 8). Publishable even if the answer is "no significant match".
