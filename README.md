# Book of Mormon Character Analysis (BOMCA)

A reproducible, testable pipeline for the "Caractors" decipherment experiment described in
`LLM Conversation.txt`. It digitises the surviving character documents into per-occurrence
symbol images, groups them into a symbol inventory at several similarity thresholds, builds a
normalised Book of Mormon corpus, runs four independent search algorithms, and subjects the
result to randomised controls, FDR correction and a held-out split.

**Nothing in this repository claims a decipherment.** The pipeline is built so that a real
correspondence would be visible, and so that a null result is a usable result.

## Quick start

```bash
dotnet build
dotnet test                                   # 23 tests incl. a planted-signal end-to-end test

dotnet run --project src/Bomca.Cli -- extract --doc all
dotnet run --project src/Bomca.Cli -- corpus fetch --edition current
dotnet run --project src/Bomca.Cli -- corpus build
dotnet run --project src/Bomca.Cli -- cluster --doc W --top-k 40
dotnet run --project src/Bomca.Cli -- inventory --doc W
dotnet run --project src/Bomca.Cli -- analyze --doc W
dotnet run --project src/Bomca.Cli -- match --doc W
dotnet run --project src/Bomca.Cli -- significance --doc W --iterations 200
dotnet run --project src/Bomca.Cli -- verify --source grover --doc W   # external claim check
dotnet run --project src/Bomca.Cli -- report          # -> out/report.html

dotnet run --project src/Bomca.Cli -- all             # the whole pipeline
```

## What is produced

| Artifact | Meaning |
|---|---|
| `out/<DOC>/crops/<ID>.png` | One image per **symbol occurrence** (padded, original tone). |
| `out/<DOC>/norm96/<ID>.png` | Moment-normalised 96×96 binary mask used for shape matching. |
| `out/<DOC>/document.json` | Full segmentation record: rows, gaps, every occurrence with geometry, Hu moments, pHash. |
| `out/<DOC>/pairs.json` | Every scored glyph pair with a score breakdown (IoU, NCC, Chamfer, Hu, features, rotation). |
| `out/<DOC>/review.html` | Manual review sheet: accept/reject each candidate duplicate, export decisions JSON. |
| `out/<DOC>/inventory-<DOC>-tNNN.json` | **One inventory per threshold** — canonical classes with their member occurrences. |
| `out/<DOC>/labels-<DOC>-tNNN.json` | Flat occurrence → class table (what the matcher consumes). |
| `out/<DOC>/analysis.json` | Structure tests: row geometry, size drift, trailing gaps, black square, comb glyphs, Zipf, compression. |
| `out/<DOC>/matches.json` / `.html` | Fingerprints, passages, alignment evidence, anchor probes. |
| `out/<DOC>/significance.json` | Null distribution, empirical p, z, FDR, held-out result, verdict. |
| `out/<DOC>/verify-grover.json` / `.html` | Verification of Jerry Grover's published translation claim (see below). |
| `out/report.html` | One self-contained report over all of the above. |
| `out/runs.json` | Append-only run manifest (git SHA, arguments, timing, status). |

## Datasets

| Code | Document | Role |
|---|---|---|
| `W` | Caractors (John Whitmer copy) | Primary: 7 text rows, ~258 glyphs |
| `B` | Stick of Joseph broadside (1844), 3 lines | Secondary: the same underlying sequence, independent copy |
| `C` | Cowdery character document | Calibration only (4 labelled glyphs) |
| `FW` | Frederick G. Williams document | Calibration only (copy of the same 4 labels) |

`C`/`FW` need manual glyph regions in `review/regions-C.json` / `review/regions-FW.json`
(`[{ "x":…, "y":…, "w":…, "h":… }]`) to isolate the four labelled characters from the surrounding
Latin text. Until then their automatic segmentation is useful for style comparison only, and they
are never used in the decipherment search.

## External claim verification

`bomca verify --source grover` tests the published translation by Jerry D. Grover Jr.
(`data/external/grover-claims.json`) without re-translating it. It checks the consequences that are
independent of the claim:

1. **Numeral-run test** (claim-free): numeral-like glyphs are identified from shape features alone
   and their run structure is compared with random glyph sets. Grover's reading requires numerals to
   form multi-glyph sequences.
2. **Positional numeral test**: his Crowley character numbering (C-1…C-230) is mapped proportionally
   onto our occurrences; are the glyphs at his numeral positions more numeral-like than chance?
3. **Calendar claims vs the corpus**: every claimed date statement ("ninety and second year",
   "thirty and fourth year", "three hundred and eighty and four years", …) is looked up in the text.
4. **Segment comparison**: his claim that the top four lines and bottom three lines are different
   texts predicts a measurable difference in glyph size and vocabulary.
5. **Anchor probes**: does the claimed content concentrate in the corpus regions he names, with a
   *real Book of Mormon excerpt* as the control (not just random words)?

Three search variants are also available because of his method: `--reverse` (he reads right-to-left),
`--split 2` (two independent texts) and `--exclude-numerals` (numerals carry dates, not words).

## Design decisions

- **Every occurrence is preserved.** Duplicates are never collapsed before testing: each threshold
  produces its own inventory file, so `t=0.60 … t=0.90` can be reviewed side by side.
- **Auto threshold.** The default inventory threshold is derived from the pair-score distribution
  (mean + 1.25 σ, clamped to 0.55…0.90) and reported in `inventory-versions.json`; the other
  thresholds remain available.
- **Punctuation is stripped** from the corpus. Edition choice therefore does not affect the
  statistics; the current edition is used and the 1830 edition is optional for change statistics.
- **Compression bounds.** A document of N glyphs is assumed to cover between 0.8 and 12 words per
  glyph. This rejects implausible span hypotheses without guessing a translation ratio.
- **Semantic help is verification-only.** Ollama/gemma is used (a) to adjudicate borderline
  duplicate pairs (`--llm`) and (b) to widen a search with corpus-constrained synonyms
  (`--widen`). It never assigns meanings that the statistics have not produced. All model
  responses are cached under `out/cache/ollama`, so runs are reproducible and offline-safe.

## Layout

```
src/Bomca.Core/       imaging, descriptors, clustering, corpus, analysis, matching, significance, reporting
src/Bomca.Cli/        bomca CLI (verbs: extract, cluster, review, corpus, inventory, analyze, match,
                      significance, report, all)
tests/Bomca.Core.Tests/ unit + planted-signal integration tests (no image files needed for search tests)
data/raw/             source images and downloaded corpus (large files gitignored)
docs/                 methodology, limitations, claim ledger, findings log, decisions
```

## Requirements

.NET 10 SDK. Optional: Ollama with a vision-capable model for `--llm` adjudication and
`nomic-embed-text` for embeddings.
