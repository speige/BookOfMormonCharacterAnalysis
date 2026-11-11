# Decision log

| # | Decision | Why | Reversible? |
|---|---|---|---|
| 1 | C# throughout, hand-rolled CLI parser instead of System.CommandLine | User preference; no dependency on a beta parser API for 10 verbs | Yes |
| 2 | ImageSharp for imaging | Only mature .NET imaging stack; no Python/OpenCV dependency on this machine | Yes |
| 3 | Valley-based row detection (component-centre histogram) | Peak picking produced 14 rows on a 7-row page; valleys reproduce the real layout | Yes (`RowDetector`) |
| 4 | Morphological closing to group strokes into glyphs | Pairwise bbox merging split cursive characters; closing is stable and adaptive | Yes (`GlyphGrouper`) |
| 5 | `max(mean·0.72, Otsu)` ink threshold | Mean-based alone misses the low-contrast inverted broadside scans | Yes |
| 6 | Auto polarity detection | Broadside line crops are light-on-dark; hard-coding a flip would break other sources | Yes |
| 7 | Moment normalisation with **no** principal-axis rotation | The principal axis is undefined for near-isotropic glyphs and caused 90° flips between copies of the same character | Yes |
| 8 | Chamfer weighted highest (0.40) in the shape score | Thin handwriting strokes make raw IoU unstable; Chamfer tolerates 1–2 px deformation | Yes |
| 9 | Dilated IoU variant in the comparer | A one-pixel stroke shift is not a different character | Yes |
| 10 | Average-linkage clustering, unscored pairs = 0 | Prevents chained look-alikes from fusing two real classes | Yes |
| 11 | Multiple inventory versions per run, auto threshold = mean + 1.25 σ | The user wants to review several thresholds; one hard-coded cut would hide the choice | Yes |
| 12 | Per-occurrence IDs (`W-L03-07`) never collapsed | Same reasoning: duplicates are a hypothesis, not a fact | Yes |
| 13 | Book of Mormon JSON: current edition primary, 1830 optional | Punctuation is stripped, so edition drift is a second-order effect; user's call | Yes |
| 14 | Compression bound 0.8–12 words/glyph | Rejects absurd span hypotheses without needing a known translation ratio | Yes |
| 15 | Scanner A's score excluded from the significance statistic | It saturates at 1.0 on shuffled streams; using it would guarantee a false positive | Yes |
| 16 | Scanner B tests phrase counts **inside** the window | Globally sliding a 3-gap pattern over 1300 occurrences makes coincidences inevitable | Yes |
| 17 | Bijection constraint (one phrase per class) | Removes the "everything maps to the most frequent phrase" degeneracy | Yes (model choice) |
| 18 | Stability re-check under ±1 stride | Cheap internal held-out test that removes most spurious windows | Yes |
| 19 | Alignment reported as coverage, not DP score | DP normalisation collapses to 0 while the dictionary is partial | Yes |
| 20 | Ollama used for adjudication and corpus-constrained synonym widening only | User preference; and semantic meanings must not drive the statistics | Yes (`--widen`, `--llm`) |
| 21 | LLM responses cached on disk by payload hash | Reproducibility and offline re-runs | Yes |
| 22 | Raw images moved to `data/raw/**` | Provenance and provenance hashing in one place | Yes |
| 23 | `C`/`FW` excluded from matching until manual label regions exist | Auto-segmentation pulls in Latin text; mislabelled training data is worse than none | Yes |
| 24 | Synthetic test corpus with uniform phrase density inside/outside the span | A passage whose phrases appear *only* there is unrealistic and makes implied-span estimation meaningless | Yes |
| 25 | Synthetic test gives each planted phrase a distinct occurrence count | Otherwise the "planted signal" is information-theoretically indistinguishable from a coincidence and the test asserts something no algorithm could achieve | Yes |
