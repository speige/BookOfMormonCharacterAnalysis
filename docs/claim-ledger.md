# Claim ledger

Every historical claim inherited from `LLM Conversation.txt`, with the confidence this project is
willing to attach to it. These claims are *context for interpretation*, never inputs to the
algorithm: the search does not depend on any of them being true.

The report reads this file directly (`Claim | Confidence` after a leading `- `).

- John Whitmer wrote the heading "Caractors" | Strong evidence (handwriting + XRF ink analysis)
- John Whitmer probably wrote the characters themselves | Probable (same-handwriting analysis, XRF consistency)
- The characters were copied from another document, not selected from memory | Strong evidence (layout, shrinking hand, ink)
- The source of that document was a transcription associated with the plates | Supported by historians, not proven
- John Whitmer personally copied directly from the plates | Plausible, unproven
- The Caractors document is a continuous passage | Plausible; the pipeline tests this rather than assuming it
- The black square acts as a separator | Interesting hypothesis; position statistics in `analysis.json` are mixed
- The characters were mechanically transferred (frottage/tracing) | Unproven; stroke-level irregularity test available
- The Stick of Joseph broadside shares a common source with Caractors | Very likely (three near-identical lines)
- The broadside is a more faithful rendering because it is more artistic | Unproven — it is a commercial engraving of a handwriting source
- The Egyptian Alphabet documents explain the Caractors symbols | No established evidence; excluded from the corpus
- Cowdery/Williams labelled characters mean "The Book of Mormon" / "The interpreters of languages" | Historical labels, not verified translations
- The Cowdery/Williams "Hebrew" transliterations are Hebrew | No — the JSP editors state they are not recognisable Hebrew
- "the interpreters of languages" appears in the Book of Mormon text | False — 0 occurrences (verified)
- "Urim and thummim" appears in the Book of Mormon text | False — 0 occurrences (verified); the text says "interpreters"
- "The Book of Mormon" appears only as a title | Partly false — 2 occurrences (title page and Mormon 1:1)
- Nephi wrote in "the learning of the Jews and the language of the Egyptians" | Scriptural claim (1 Nephi 1:2)
- Moroni calls the script "reformed Egyptian" | Scriptural claim (Mormon 9:32)
- Joseph Smith received roughly 20 words at a time | Skousen's reading of the manuscripts; suggestive, not a constraint here
- The plates had roughly 30–86 sq ft of inscribed surface | Dale's estimate; wide error bars
- The lost 116 pages remove any possible English counterpart | A real risk: if the passage is there, no search can succeed and a null result is uninformative

## Jerry D. Grover Jr. (2017 book and talk) — verdicts from `bomca verify`

- The Caractors document is logographic | Consistent with the symbol-count argument; this pipeline tests it rather than assuming it
- The script reads right to left | Unresolved; distance-based scanners are direction-invariant, the alignment scanner scores 0.46 with `--reverse` vs 0.38 without, both within noise
- About 43 of ~230 glyphs are numerals forming sequences of 2-7 | **Not supported** by glyph shapes (numeral-run p = 0.91; positional enrichment p = 0.56)
- The top four lines and the bottom three lines are different texts | **Supported**: 1.35x size difference, 84% disjoint character sets
- King Benjamin died 479 years after Lehi left Jerusalem | Consistent with the corpus (476 + the "three years" of Mosiah 6:5 = 479)
- Christ's birth sign in the 92nd year of the judges | Present in the text (3 Nephi 1)
- Christ's coming in the 34th year, first month | Present in the text (3 Nephi 8:5)
- 384 years at the final battle | Present in the text (Mormon 6:5)
- Samuel's prophecy about 60 months before Christ | Consistent (Helaman 14:2 "five years")
- The published numeral tables reproduce his dates | **False as printed**: C-85/C-86 carry both 83 and 479, and the 436 sequence differs between chapters
