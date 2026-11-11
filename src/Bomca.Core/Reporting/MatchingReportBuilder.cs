using System.Text;
using Bomca.Core.Corpus;
using Bomca.Core.Matching;
using Bomca.Core.Model;

namespace Bomca.Core.Reporting;

public static class MatchingReportBuilder
{
    private const string Css = """
        :root { --bg:#faf8f3; --ink:#241c14; --line:#dfd6c6; --accent:#8a5a2b; --muted:#6b5c49; }
        body { font-family: ui-sans-serif, system-ui, "Segoe UI", Arial; margin:0; padding:24px 32px; background:var(--bg); color:var(--ink); }
        h1 { font-size:22px; margin:0 0 6px; } h2 { font-size:16px; margin:28px 0 8px; border-bottom:1px solid var(--line); padding-bottom:4px; }
        .meta { color:var(--muted); font-size:13px; margin-bottom:16px; }
        table { border-collapse:collapse; width:100%; font-size:13px; margin-bottom:8px; }
        th,td { border-bottom:1px solid var(--line); padding:5px 8px; text-align:left; vertical-align:top; }
        th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.05em; }
        .score { font-variant-numeric:tabular-nums; font-weight:600; }
        .snippet { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size:11px; color:#3b2f24; }
        .tag { font-size:10px; padding:1px 6px; border-radius:8px; background:#efe7d8; color:#5a4a38; }
        .note { background:#fff8e8; border-left:3px solid var(--accent); padding:8px 12px; font-size:13px; margin:10px 0; }
        .empty { color:var(--muted); font-style:italic; }
        """;

    public static void Write(string outPath, MatchResult result, CorpusIndex corpus, GlyphStream stream, InventoryVersion inventory)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Match results</title><style>")
          .Append(Css).Append("</style></head><body>\n");
        sb.Append($"<h1>Decipherment search &mdash; {result.Doc}</h1>\n");
        sb.Append($"<div class=\"meta\">Corpus: {result.Edition} ({result.CorpusTokens:N0} tokens) &middot; glyphs {result.Glyphs} / classes {result.Classes} at inventory threshold {result.Threshold:0.00} &middot; candidate phrases {result.CandidatePhrases} &middot; {result.ElapsedMs:N0} ms &middot; {result.GeneratedUtc}</div>\n");

        sb.Append("<div class=\"note\">Scores are heuristics, not probabilities. Scanner A compares normalised inter-occurrence distance fingerprints; ");
        sb.Append("scanner B requires several hypotheses to agree inside one contiguous word window; scanner C refines the best windows with a bounded many-to-many alignment; ");
        sb.Append("scanner D probes semantic anchor density. A result only becomes evidence after the randomised controls in <code>significance.json</code>.</div>\n");

        sb.Append("<h2>A. Relative-distance fingerprints</h2>\n");
        if (result.Fingerprints.Count == 0)
        {
            sb.Append("<p class=\"empty\">No (glyph class, phrase) pair passed the fingerprint gate.</p>\n");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Score</th><th>Class</th><th>Occ.</th><th>Candidate phrase</th><th>Phrase occ.</th><th>Gap L1</th><th>Count penalty</th><th>Glyph gap fingerprint</th></tr></thead><tbody>\n");
            foreach (var h in result.Fingerprints.Take(40))
            {
                sb.Append($"<tr><td class=\"score\">{h.Score:0.000}</td><td>{h.ClassId}</td><td>{h.Occurrences}</td><td>{Esc(h.Phrase)}</td><td>{h.PhraseOccurrences}</td>");
                sb.Append($"<td>{h.GapDistance:0.000}</td><td>{h.CountPenalty:0.000}</td>");
                sb.Append($"<td class=\"snippet\">{string.Join(" ", h.GlyphGaps.Select(g => g.ToString("0.00")))}</td></tr>\n");
            }
            sb.Append("</tbody></table>\n");
        }

        sb.Append("<h2>B/C. Best passages (joint warp + alignment)</h2>\n");
        if (result.Passages.Count == 0)
        {
            sb.Append("<p class=\"empty\">No window reached the joint-constraint gate.</p>\n");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Combined</th><th>Warp</th><th>Align</th><th>Reference</th><th>Window</th><th>Text</th><th>Evidence</th></tr></thead><tbody>\n");
            foreach (var p in result.Passages)
            {
                sb.Append($"<tr><td class=\"score\">{p.Combined:0.000}</td><td>{p.WarpScore:0.000}</td><td>{p.AlignScore:0.000}</td>");
                sb.Append($"<td>{Esc(p.Reference)}</td><td>w{p.WordStart} +{p.WindowWords}</td>");
                sb.Append($"<td class=\"snippet\">{Esc(p.Snippet)}</td>");
                sb.Append($"<td class=\"snippet\">{string.Join("<br>", p.Evidence.Select(Esc))}</td></tr>\n");
            }
            sb.Append("</tbody></table>\n");
        }

        if (result.Anchors.Count > 0)
        {
            sb.Append("<h2>D. Semantic anchor density</h2>\n");
            sb.Append("<table><thead><tr><th>Concept</th><th>Reference</th><th>Density</th><th>Text</th><th>Enriched classes</th></tr></thead><tbody>\n");
            foreach (var a in result.Anchors.OrderByDescending(x => x.Density).Take(20))
            {
                sb.Append($"<tr><td>{Esc(a.Concept)}</td><td>{Esc(a.Reference)}</td><td class=\"score\">{a.Density:0.00000}</td>");
                sb.Append($"<td class=\"snippet\">{Esc(a.Snippet)}</td><td>{string.Join(", ", a.EnrichedClasses)}</td></tr>\n");
            }
            sb.Append("</tbody></table>\n");
        }

        if (result.Notes.Count > 0)
        {
            sb.Append("<h2>Notes</h2><ul>\n");
            foreach (var n in result.Notes) sb.Append($"<li>{Esc(n)}</li>\n");
            sb.Append("</ul>\n");
        }

        sb.Append("</body></html>\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }

    internal static string Esc(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
