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
