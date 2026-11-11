# Findings log

Append-only. Newest run first. Every number here is reproducible with the commands shown.

## 2026-02-11 — fuzzy contextual search (short-circuiting the exhaustive scan)

Command: `bomca focus --source grover` then `bomca match --doc W --focus-auto`.
Artifact: `out/W/focus-grover.{json,html}`, `out/W/focus-grover-verses.json`.

Grover's translation is a paraphrase in modern English, not corpus phrasing, so exact-phrase search
cannot use it. The new `focus` step scores sliding windows by **lemma-aware, IDF-weighted coverage** of
the paraphrase's content terms, with a floor on *distinct* term coverage so one common word cannot carry
a window, plus optional corpus-constrained synonym expansion. Ancient-register forms are folded
(`came→come`, `cometh→come`, `prophesied→prophesy`, `nephites→nephite`, `destruction→destroy`) and
plural/tense suffixes are stripped.

### Where the paraphrase lands

| Segment | Best window | Score | Control | p | Second region |
|---|---|---|---|---|---|
| top four lines (Mosiah I → Benjamin's death) | **Mosiah 5:15** | 0.327 | 0.126 | 0.024 | Mosiah 7:29 |
| bottom three lines (Samuel → 384 AD) | **3 Nephi 1:23** | 0.282 | 0.132 | 0.024 | 4 Nephi 1:33 |

Both segments land in the eras the claim itself names — Mosiah for the top text, 3 Nephi / 4 Nephi for
the bottom text — at p = 0.024 against a fixed control. This is the strongest support found so far for
the *content* of his claim, and it is a different test from the earlier anchor probe (which used raw
word density); the lemma folding is what makes "passed away / died / ascended to eternity" count as the
same evidence.

### What the short-circuit buys

| Search | Regions | Words scanned | Scanner B time | Best passage |
|---|---|---|---|---|
| unrestricted | — | 267,471 | 4,617 ms | 0.377 |
| `--focus-auto` | 4 | 3,100 (86×) | **201 ms** | **0.445** |
| `--focus-auto --reverse` | 4 | 3,100 | 188 ms | 0.826 (warp, no alignment) |

Restricting to 3,100 words makes the window scan **23× faster** and produces a *better* candidate list,
because windows outside the focus can no longer win by coincidence. That is exactly the intended effect:
search the plausible few thousand words exhaustively instead of the whole book superficially.

### Honest caveats

- Both focus scores are modest in absolute terms (0.28–0.33). The regions are plausible, not identified.
- The control is a fixed stride sample plus the paraphrase's own terms shuffled; it is not a matched
  semantic control, so p = 0.024 should be read as "better than this crude baseline", not as a calibrated
  significance value.
- 86× reduction is bounded by `--regions-per-segment 2`; raising it buys safety, not certainty.
- The `focus` ranking is corpus-side only. It says where the paraphrase *fits*, which is exactly the
  hypothesis the symbol search then has to confirm or reject.

---

## 2026-02-11 — external claim verification (Jerry D. Grover Jr.)

Source: *Translation of the "Caractors" Document, Revised and Updated* (book) and the 2017 Book of
Mormon Conference talk. Command: `bomca verify --source grover --doc W`.

### What the claim asserts

- The script is **logographic**; reads **right to left**; about 230 characters in two texts.
- **Numerals** are a large share of the glyphs (~43 of ~230 by his own numbering) drawn from Egyptian
  hieratic/Demotic, Mesoamerican (dot/bar, vigesimal), and Sumerian proto-cuneiform sources.
- Top four lines: a chronological summary from the 19th regnal year of Mosiah I to the death of king
  Benjamin (**479 years after Lehi's departure**). Bottom three lines: a prophetic-calendar synopsis
  from Samuel the Lamanite (≈60½ months before Christ) to **384 years** after Christ's coming.

### Result 1 — text/chronology side: 6 of 7 claims match the corpus

| Claim | Pattern | Status |
|---|---|---|
| Sign of Christ's birth in the 92nd year of the judges | `ninety and second year` | present (3 Nephi 1) |
| Christ comes in the 34th year, first month | `thirty and fourth year` | present (3 Nephi 8:5) |
| Final battle in the 384th year | `three hundred and eighty and four years` | present (Mormon 6:5) |
| Reckoning changed nine years after the sign | `nine years` | present (3 Nephi 2) |
| Samuel's sign five years before Christ (≈60 months) | `five years` | present (Helaman 14:2) |
| Benjamin dies three years after Mosiah begins (476 + 3 = 479) | `three years` | present (Mosiah 6:4–5) |
| 24 Jaredite plates brought and translated | `twenty four plates` | present (Mosiah 8, 21; Ether 1) |

Every date he claims to read is a date the Book of Mormon itself contains or implies. His 479 is
exactly the conventional chronology: 600 − 121 = 479 for Benjamin's death. This is genuine internal
consistency — but it is *also* what a translator working from the chronology would produce, which he
acknowledges. It cannot, on its own, distinguish reading from reconstruction.

### Result 2 — glyph side: his numeral prediction is not supported

- **Numeral-run test (claim-free).** Only 24 of 258 extracted glyphs score as numeral-like from shape
  features alone, and they are *scattered*: mean run length 1.04 vs 1.10 for random glyph sets,
  longest run 2 (p = 0.91, z = −0.88). His reading requires runs of 2–7 consecutive numerals.
- **Positional numeral test.** Mapping his Crowley ids (C-1…C-230, split 1–99 / 100–230) proportionally
  onto our 258 occurrences gives 43 claimed numeral positions. Their mean numeral-likeness is 0.041
  vs 0.044 at random positions (p = 0.56, z = −0.17). No enrichment at the claimed places.
- **Caveat.** Both tests depend on my segmentation (258 glyphs for a document he numbers at ~230) and
  on a deliberately crude, transparent numeral classifier. The negative result is evidence against the
  strong form of the claim, not a refutation: his numerals include forms (dots *inside* glyphs,
  curvy variants) that no shape heuristic can be expected to catch from 96×96 crops.

### Result 3 — his two-text structure is supported

| Segment | Glyphs | Median glyph height | Classes | Class overlap with other segment |
|---|---|---|---|---|
| top four lines | 109 | 111 px | 77 | 16 % |
| bottom three lines | 107 | 82 px | 88 | 14 % |

The bottom three lines are written **1.35× smaller**, use a largely different character set, and match
the independent measurement in `analysis.json` (glyph height falls 10.3 px per row, R² 0.55). His
observation that the document contains two different texts in two different hands/sizes is supported.

### Result 4 — claimed content does not concentrate where the claim says (except partly)

Anchor probes with a *real-excerpt* control:

| Segment | Best window | Hits | Control mean | p | Verdict |
|---|---|---|---|---|---|
| top four lines (Mosiah-era claim) | Alma 8:1 | 33 | 12.6 | 0.03 | concentrated, but **outside** the named era |
| bottom three lines (3 Nephi / Mormon-era claim) | 3 Nephi 2:4 | 34 | 12.6 | 0.02 | concentrated **inside** the named era |

One of the two lands where he says. Both peak scores are also only ~2.5× the control mean, and the
whole translation is written in Book-of-Mormon vocabulary, so the probe mostly confirms that his text
is BoM-flavoured rather than that it is the right passage.

### Result 5 — his published tables contain internal contradictions

- The same two glyphs (C-85, C-86) carry two different values: **83** (chapter 11) and **479**
  (chapter 4), where chapter 11 assigns 479 to C-88…C-92.
- The 436 sequence is printed as `C-74, C-73, C-72, C-70, C-72, C-71, C-72` in chapter 4 (C-72 three
  times, C-69/C-68 missing) but `C-74, C-73, C-72, C-71, C-70, C-69, C-68` in chapter 11.

A reader cannot reproduce the two dates that anchor his whole chronology from the published tables as
printed. This does not make the translation wrong, but it means the claim is not yet self-consistent
enough to be independently checked character by character.

### Result 6 — search variants derived from his method

| Variant | Best passage score | Top window |
|---|---|---|
| baseline | 0.377 | Omni 1:17 |
| `--reverse` (right-to-left) | 0.464 | Helaman 11:11 |
| `--split 2` (two texts) | 0.420 | 1 Nephi 22:12 |
| `--exclude-numerals` | 0.422 | 3 Nephi 19:8 |
| `--reverse --split 2` | 0.380 | Alma 46:13 |

All variants sit inside the noise band established by the shuffle control (observed 0.837 vs null
0.829 ± 0.010, p = 0.29 with alignment disabled, which is the statistic the harness uses). Right-to-left
reading gives the highest score, but not significantly so. Notably, the baseline now ranks **Omni 1:17**
first — the verse that introduces the Mosiah-era migrations — which is at least in the era Grover's top
segment describes, though the score is indistinguishable from noise.

### Overall assessment of the claim

- **Structurally plausible**: the two-text split, the size difference, the direction and the
  logographic density argument are all consistent with the physical evidence.
- **Chronologically coherent**: every date he reads is in the Book of Mormon or implied by it.
- **Not independently supported at the glyph level**: the numerals he claims are not distinguishable
  as numerals in the images, in runs or in position.
- **Not reproducible from the published tables**: two of the three tables that carry his key dates
  contradict each other.
- The strongest statement this pipeline can make is that his claim survives the *textual* consistency
  test and fails the *glyph-shape* test, in a document where my segmentation is still ~12 % over
  fragmented. A cleaner inventory plus his per-character scan images would be needed to do better.

---

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
