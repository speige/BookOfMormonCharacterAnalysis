# Methodology

This document records what the pipeline does, why each choice was made, and what it cannot do.
It follows the five-part structure of the research conversation: provenance, physical evidence,
translation process, linguistic hypotheses, computational experiment.

## 1. Digitisation

1. **Load and invert if needed.** Some sources are light-on-dark scans (the broadside line crops).
   Polarity is detected from the flattened image mean and confirmed by an ink-coverage test.
2. **Flatten illumination** with a large-radius box blur (radius 101 px) so paper tone and uneven
   lighting do not become "ink".
3. **Threshold** at `max(mean × 0.72, Otsu)`. The mean-based cut preserves the tuned behaviour on the
   Caractors page; Otsu is required for low-contrast or inverted sources.
4. **Reject artefacts**: specks below the minimum area, page-edge shadow bands (faint-ink test on the
   bottom 20 % of the page), and — on full pages only — components touching the frame.
5. **Find rows** by valleys in a smoothed histogram of connected-component centres. Valleys are far
   more reliable than peaks here: a handwritten row is a broad hump, and peak picking splits one row
   into two (this was verified against the real page: peak picking produced 14 rows, valley detection
   produces the correct 7 plus the title).
6. **Group strokes into glyphs** by morphological closing (radius = 5 % of median glyph height)
   followed by connected-component labelling. Each original component is assigned to the fused
   component it overlaps most; bounding boxes are recomputed from the original ink so crops stay
   tight. Over-wide or over-tall groups are split as a last resort.
7. **Per-glyph features**: ink area, density, aspect, stroke width (2A/P), holes (Euler-style
   background regions), estimated stroke count (median horizontal ink runs), seven log-scaled Hu
   moments, and a 64-bit dHash.

The topmost band is recorded as the handwritten title and excluded from analysis (`--no-skip-title`
keeps it).

## 2. Shape comparison and the inventory

Two masks are compared as follows:

| Component | Meaning | Weight |
|---|---|---|
| IoU | overlap after aspect-preserving moment normalisation, best of ±8°/±16° and mirror; also tested one pixel dilated, because a one-pixel stroke shift is not a different character | 0.25 |
| NCC | normalised cross-correlation of the binary coverage | 0.15 |
| Chamfer | symmetric mean distance-transform distance, mapped through an exponential | 0.40 |
| Hu moments | log-scaled invariant moment distance | 0.10 |
| Features | aspect, density, stroke width, hole and stroke counts | 0.10 |

Candidates come from a cheap prefilter (pHash Hamming + feature agreement, top *k* per glyph) so the
comparison is never an O(n²) sweep. The combined score is multiplied by `min(1, 0.4 + IoU)` so that
moment/feature agreement alone can never rescue shapes that do not overlap.

Clustering is **average-linkage agglomerative** over the scored pair graph; pairs that were never
scored count as zero similarity, which prevents a chain of weak look-alikes from fusing two real
classes. Every threshold in `--thresholds` produces its own inventory file. The default threshold is
`mean + 1.25 σ` of the observed pair scores, clamped to `[0.55, 0.90]`.

Known limitations (measured): the surviving inventory is over-fragmented — 258 glyphs cluster into
~180 classes at the auto threshold, i.e. most classes are singletons. The review sheet exists to fix
this by hand; until then the glyph stream used by the search is noisier than it should be.

## 3. Corpus

`corpus fetch` downloads a machine-readable edition and records its URL and SHA-256. Normalisation:
lowercase, typographic quotes folded, apostrophes removed (`it's` → `its`), all punctuation dropped,
whitespace collapsed. Verse, chapter and book offsets are preserved so any hit can be reported as a
reference. Sanity gates (verse count, word count, `and it came to pass` count) fail the build loudly,
because a bad corpus silently destroys every statistic downstream.

Verified lookups on the current edition:

| Phrase | Count |
|---|---|
| and it came to pass | 1122 |
| and it came to pass that | 1042 |
| interpreters | 6 |
| interpreters of languages | 0 |
| urim and thummim | 0 |
| the book of mormon | 2 (title page, Mormon 1:1) |
| brethren adieu | 1 (Jacob 7:27) |
| it grieveth me that I should lose this tree | 4 (Jacob 5:7, 11, 13 …) |

## 4. Search

### Scanner A — relative-distance fingerprints

For a glyph class with *k* occurrences and a phrase with *m* occurrences, both gap vectors are
normalised to sum to one and compared by L1 distance (bounded by 2). Windows of *k* consecutive
phrase occurrences are tested, which is correct because the occurrences inside a contiguous passage
are a consecutive run of the phrase's global occurrence list. Scores are multiplied by a phrase
specificity factor (longer = more specific) and by a compression-plausibility factor that rejects
implied document spans outside 0.8–12 words per glyph.

Scanner A saturates: with ~1600 candidate phrases and ~50 windows per phrase, some phrase will match
any pattern of three or more gaps. Its output is therefore a *candidate list*, never evidence.

### Scanner B — multi-constraint window search

For each candidate window and span, phrase occurrences *inside the window* are bucketed by count.
A class with *k* occurrences can only match a phrase that occurs exactly *k* times in that window, and
the normalised positions must agree within L1 ≤ 0.28. Fitting pairs are assigned greedily to keep the
model a bijection (one phrase per class), and a window only counts if at least three classes fit.
The window is then re-scored after shifting by one stride: a genuine alignment survives a small
shift, a coincidence does not.

The span is scanned over a geometric ladder, so the unknown symbol-to-word ratio is recovered from
the data rather than assumed.

### Scanner C — bounded many-to-many alignment

A dynamic program aligns the glyph stream to a window where one glyph may emit 0–10 words
(logographic), 1–3 glyphs may spell one word (alphabetic), and affine gaps absorb unexplained
material on either side. Dictionary phrases score 1.0 for an exact span, 0.85 for a phrase prefix or
suffix (the phrase boundary inside the document is unknown) and 0.6 for a contained span. The
reported alignment score is *coverage*: the share of the window's words explained by an exact
glyph-to-phrase match. This stays meaningful while most glyphs still have no dictionary entry.

### Scanner D — semantic anchor probing

Anchor concepts (god, lord, son, angel, earth, interpreter, book, behold, came to pass) are located by
density in sliding windows; each concept's windows are then checked for glyph classes enriched near
the hits. With `--widen`, `gemma` (via Ollama) proposes near-synonyms **restricted to words that
already exist in the corpus**, so the search space stays finite and every expansion is auditable.
Pure-Language/Egyptian-Alphabet meanings are treated as candidate associations with a confidence,
never as translations.

## 5. Statistics

- **Null model**: the glyph stream is shuffled while preserving class frequencies and length. The
  same engine runs on each shuffle (typically 30–1000 iterations).
- **Statistic**: the best multi-constraint passage score. Scanner A's saturated single-pattern score
  is deliberately excluded — it reaches 1.0 on shuffled streams too.
- **p-value**: `(#{null ≥ observed} + 1) / (N + 1)`.
- **FDR**: Benjamini–Hochberg across the reported hypotheses, with each hypothesis's p-value taken
  from the null *best-score* distribution (conservative).
- **Held-out**: the top hypothesis that has at least three occurrences in both halves of the glyph
  stream is re-scored on the untouched half against a shuffle of that same half.

## 6. Structure tests

| Test | Rationale |
|---|---|
| Row height trend | A negative slope down the page matches a copyist compressing to fit the sheet |
| Trailing gap per row | Measures the "row ends early although a glyph would fit" observation in glyph widths |
| Black square | Solid-square glyphs detected by aspect/density/area, with line-start statistics |
| Comb glyphs | Stroke-count variance decides between "one character" and "numeral/tally" |
| Zipf slope | Steep negative slopes fit morphographic systems; shallow slopes fit alphabets |
| Repeat regularity | Normalised-gap dispersion of repeated classes vs a shuffled stream |
| Compression | Symbols per square inch from measured boxes, extrapolated over Dale's 30–86 sq ft scenarios |

These produce *leans*, not conclusions; the compression inputs are historical estimates with wide
error bars and are labelled as such in the report.
