# Limitations

1. **The glyph inventory is over-fragmented.** 258 detected glyphs collapse into ~180 classes at the
   automatic threshold, so most classes are singletons. Shape similarity for thin handwritten strokes
   at 96×96 is dominated by Chamfer/dilation tolerance, and true occurrences of one character often
   score ~0.7–0.85 while unrelated glyphs score ~0.45–0.6. Manual review (`review.html`) is required
   to consolidate classes; until that is done the symbol stream fed to the search is noisy.
   *Consequence*: fingerprints are computed over classes that partially under-merge, which weakens
   Scanner A and B.
2. **Segmentation is imperfect.** Some ornate characters are split into two boxes and a few
   multi-stroke glyphs are merged across a word gap. Per-glyph `confidence` records when merging was
   needed, and `overlay.png` shows every box for review, but the crops are not ground truth.
3. **Scanner A saturates.** With ~1600 candidate phrases, a short gap pattern will match some phrase
   in almost any stream, in real and shuffled data alike. Its score is a prioritisation device only.
4. **The statistic is a heuristic.** The multi-constraint score is not a likelihood ratio; the null
   distribution is empirical. A p-value here means "no better than a shuffled stream", nothing more.
5. **Span scanning is coarse.** Only 8–11 geometric span candidates are evaluated with a stride of
   span/8. A true span between candidates may be under-scored by the projection test; the alignment
   step partially compensates.
6. **The synthetic test is easier than reality.** The planted-signal test uses distinct occurrence
   counts per phrase, which makes the true pairing identifiable. Real characters may share occurrence
   counts with several phrases, in which case the search cannot distinguish them.
7. **Cowdery/Williams labels need manual regions.** Those four glyphs are the only surviving
   character-to-meaning pairs, but they sit inside pages of Latin handwriting; until
   `review/regions-C.json` is authored, their automatic segmentation mixes in the surrounding text.
8. **The 1830 corpus has no verse structure.** Its pages are one chapter each with paragraph-level
   verses, so cross-edition statistics are positional and approximate.
9. **Compression estimates are illustrative.** Plate area, sealed fraction, symbol density and the
   words-per-symbol ratio each carry wide error bars; the model is for ranking hypotheses, not proof.
10. **No result in this repository constitutes a translation.** The only defensible outputs are
    (a) a reviewed symbol inventory, (b) structural statistics, (c) ranked candidate passages with
    explicit uncertainty, and (d) an honest null result where the controls do not separate.
