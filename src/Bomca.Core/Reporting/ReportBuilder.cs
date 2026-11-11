using System.Text;
using Bomca.Core.Analysis;
using Bomca.Core.Corpus;
using Bomca.Core.Json;
using Bomca.Core.Matching;
using Bomca.Core.Model;
using Bomca.Core.Significance;

namespace Bomca.Core.Reporting;

/// <summary>
/// One self-contained HTML report covering data provenance, extraction quality, the inventory versions,
/// the structure tests, the search results and the statistical verdict. Images are linked relative to the
/// report location so nothing needs to be embedded.
/// </summary>
public static class ReportBuilder
{
    public sealed class Input
    {
        public List<DocumentRecord> Documents { get; set; } = new();
        public List<InventoryVersion> Inventories { get; set; } = new();
        public List<StructureAnalysis> Analyses { get; set; } = new();
        public List<MatchResult> Matches { get; set; } = new();
        public List<SignificanceReport> Significance { get; set; } = new();
        public CorpusIndex? Corpus { get; set; }
        public List<string> ClaimLedger { get; set; } = new();
        public string? GitSha { get; set; }
    }

    public static void Write(string outPath, Input input, string outRoot)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Caractors analysis report</title><style>")
          .Append(Css).Append("</style></head><body>\n");

        sb.Append("<h1>Caractors decipherment experiment</h1>\n");
        sb.Append($"<div class=\"meta\">Generated {DateTime.UtcNow:u} &middot; commit <code>{input.GitSha ?? "uncommitted"}</code>");
        if (input.Corpus is { } c) sb.Append($" &middot; corpus {c.Edition}: {c.Length:N0} tokens, {c.Verses.Count:N0} verses, {c.UnigramCounts.Count:N0} distinct words");
        sb.Append("</div>\n");

        sb.Append("<div class=\"note\"><strong>How to read this report.</strong> Nothing here is a claim of decipherment. ");
        sb.Append("The pipeline is designed so that a real match would be visible as (1) a high fingerprint and passage score, ");
        sb.Append("(2) a score far outside the shuffled-stream null, and (3) survival of a held-out split. ");
        sb.Append("Absence of a significant match is a valid outcome.</div>\n");

        // ---- documents
        sb.Append("<h2>1. Datasets</h2>\n<table><thead><tr><th>Doc</th><th>Name</th><th>Kind</th><th>Rows</th><th>Glyphs</th><th>Image</th><th>Link</th></tr></thead><tbody>\n");
        foreach (var d in input.Documents)
        {
            var textGlyphs = d.Symbols.Count(s => s.Note != "title-row");
            sb.Append($"<tr><td><strong>{d.DocCode}</strong></td><td>{Esc(d.Name)}</td><td><span class=\"tag\">{Esc(d.Kind)}</span></td>");
            sb.Append($"<td>{d.Rows.Count(r => !r.Skipped)}</td><td>{textGlyphs}</td><td>{d.ImageWidth}×{d.ImageHeight}</td>");
            sb.Append($"<td><a href=\"{d.DocCode}/contact-sheet.png\">contact sheet</a>");
            if (File.Exists(Path.Combine(outRoot, d.DocCode, "overlay.png"))) sb.Append($" &middot; <a href=\"{d.DocCode}/overlay.png\">segmentation overlay</a>");
            sb.Append($" &middot; <a href=\"{d.DocCode}/document.json\">document.json</a></td></tr>\n");
        }
        sb.Append("</tbody></table>\n");
        sb.Append("<p class=\"small\">Extracted glyphs are stored per occurrence in <code>out/&lt;DOC&gt;/crops/&lt;ID&gt;.png</code> with a moment-normalised 96×96 mask in <code>norm96/</code>. ");
        sb.Append("Every occurrence is labelled with its canonical class in <code>labels-&lt;DOC&gt;-tNNN.json</code>; each threshold produces a separate inventory file.</p>\n");

        // ---- inventory versions
        sb.Append("<h2>2. Symbol inventories at different merge thresholds</h2>\n");
        sb.Append("<table><thead><tr><th>Document</th><th>Threshold</th><th>Classes</th><th>Singletons</th><th>File</th></tr></thead><tbody>\n");
        foreach (var inv in input.Inventories.OrderBy(i => i.Doc).ThenBy(i => i.Threshold))
        {
            sb.Append($"<tr><td>{inv.Doc}</td><td class=\"score\">{inv.Threshold:0.00}</td><td>{inv.ClassCount}</td><td>{inv.SingletonCount}</td>");
            sb.Append($"<td><a href=\"{inv.Doc}/inventory-{inv.Doc}-t{(int)Math.Round(inv.Threshold * 100):000}.json\">inventory-{inv.Doc}-t{(int)Math.Round(inv.Threshold * 100):000}.json</a></td></tr>\n");
        }
        sb.Append("</tbody></table>\n");
        if (input.Inventories.Count > 0)
        {
            var doc = input.Inventories[0].Doc;
            sb.Append($"<p class=\"small\">Duplicate review sheet: <a href=\"{doc}/review.html\">review.html</a> (accept/reject pairs, export decisions JSON).</p>\n");
        }

        // ---- structure analysis
        sb.Append("<h2>3. Structure tests</h2>\n");
        foreach (var a in input.Analyses)
        {
            sb.Append($"<h3>{a.Doc} &mdash; {a.GlyphCount} glyphs, {a.ClassCount} classes</h3>\n");
            sb.Append("<table><thead><tr><th>Row</th><th>Glyphs</th><th>Median height</th><th>Mean gap</th><th>Trailing gap</th><th>Trailing gap (glyphs)</th></tr></thead><tbody>\n");
            foreach (var r in a.Rows)
                sb.Append($"<tr><td>{r.RowId}</td><td>{r.GlyphCount}</td><td>{r.MedianGlyphHeight:0.0}</td><td>{r.MeanInnerGapPx:0.0}</td><td>{r.TrailingGapPx:0.0}</td><td class=\"score\">{r.TrailingGapInGlyphs:0.00}</td></tr>\n");
            sb.Append("</tbody></table>\n");
            sb.Append("<table><thead><tr><th>Metric</th><th>Value</th><th>Reading</th></tr></thead><tbody>\n");
            sb.Append($"<tr><td>Row height trend</td><td class=\"score\">{a.RowHeightTrend:0.0} px/row (R² {a.RowHeightTrendRSquared:0.00})</td><td>Negative = glyphs shrink down the page</td></tr>\n");
            sb.Append($"<tr><td>Zipf slope</td><td class=\"score\">{a.ZipfSlope:0.00} (R² {a.ZipfRSquared:0.00})</td><td>Steep negative slopes fit logographic/morphographic systems; shallow slopes fit alphabets</td></tr>\n");
            sb.Append($"<tr><td>Repeat regularity vs chance</td><td class=\"score\">{a.RepeatRegularityVsChance:0.00}×</td><td>&gt;1.2 suggests structured repetition rather than chance</td></tr>\n");
            sb.Append($"<tr><td>Black squares</td><td class=\"score\">{a.BlackSquares.Count}</td><td>Solid square glyphs detected</td></tr>\n");
            sb.Append($"<tr><td>Comb glyphs</td><td class=\"score\">{a.CombGlyphs.Count}</td><td>Stroke count varies: {a.CombStrokeCountVaries}</td></tr>\n");
            sb.Append("</tbody></table>\n");
            if (a.BlackSquares.Count > 0)
            {
                sb.Append("<p class=\"small\">Black squares: ");
                sb.Append(string.Join(", ", a.BlackSquares.Select(s => $"{s.Id} (row {s.Row + 1}, {s.W}×{s.H}, density {s.InkDensity:0.00})")));
                sb.Append("</p>\n");
            }
            if (a.Compression.Scenarios.Count > 0)
            {
                sb.Append("<table><thead><tr><th>Plate-area scenario</th><th>sq ft</th><th>usable</th><th>symbols</th><th>words/symbol</th><th>reading</th></tr></thead><tbody>\n");
                foreach (var s in a.Compression.Scenarios)
                    sb.Append($"<tr><td>{Esc(s.Name)}</td><td>{s.InscribedSquareFeet:0}</td><td>{s.UsableFraction:0.00}</td><td>{s.EstimatedSymbols:N0}</td><td class=\"score\">{s.WordsPerSymbol:0.00}</td><td>{Esc(s.Interpretation)}</td></tr>\n");
                sb.Append("</tbody></table>\n");
                sb.Append($"<p class=\"small\">{Esc(a.Compression.Caveat)}</p>\n");
            }
            if (a.Observations.Count > 0)
            {
                sb.Append("<ul class=\"small\">\n");
                foreach (var o in a.Observations) sb.Append($"<li>{Esc(o)}</li>\n");
                sb.Append("</ul>\n");
            }
        }

        // ---- matches
        sb.Append("<h2>4. Search results</h2>\n");
        foreach (var m in input.Matches)
        {
            sb.Append($"<h3>{m.Doc}</h3>\n<p class=\"small\">Best fingerprint {m.Fingerprints.FirstOrDefault()?.Score:0.000} &middot; best joint passage {m.Passages.FirstOrDefault()?.Combined:0.000} &middot; {m.ElapsedMs:N0} ms. Full detail: <a href=\"{m.Doc}/matches.html\">matches.html</a></p>\n");
            if (m.Fingerprints.Count > 0)
            {
                sb.Append("<table><thead><tr><th>Score</th><th>Class</th><th>Occurrences</th><th>Phrase</th></tr></thead><tbody>\n");
                foreach (var h in m.Fingerprints.Take(8))
                    sb.Append($"<tr><td class=\"score\">{h.Score:0.000}</td><td>{h.ClassId}</td><td>{h.Occurrences}</td><td>{Esc(h.Phrase)}</td></tr>\n");
                sb.Append("</tbody></table>\n");
            }
            foreach (var p in m.Passages.Take(3))
                sb.Append($"<p class=\"small\"><strong>{Esc(p.Reference)}</strong> (combined {p.Combined:0.000}) &mdash; <span class=\"snippet\">{Esc(p.Snippet)}</span></p>\n");
        }
        if (input.Matches.Count == 0) sb.Append("<p class=\"empty\">No match run recorded.</p>\n");

        // ---- significance
        sb.Append("<h2>5. Statistical verdict</h2>\n");
        foreach (var s in input.Significance)
        {
            sb.Append($"<h3>{s.Doc}</h3>\n");
            sb.Append("<table><thead><tr><th>Quantity</th><th>Value</th></tr></thead><tbody>\n");
            sb.Append($"<tr><td>Observed best score</td><td class=\"score\">{s.ObservedBest:0.000}</td></tr>\n");
            sb.Append($"<tr><td>Null (shuffled) mean ± sd</td><td class=\"score\">{s.NullMean:0.000} ± {s.NullSd:0.000}</td></tr>\n");
            sb.Append($"<tr><td>Null maximum over {s.Iterations} runs</td><td class=\"score\">{s.NullMax:0.000}</td></tr>\n");
            sb.Append($"<tr><td>z score</td><td class=\"score\">{s.Z:0.0}</td></tr>\n");
            sb.Append($"<tr><td>Empirical p</td><td class=\"score\">{s.PValue:0.0000}</td></tr>\n");
            sb.Append($"<tr><td>Hypotheses surviving FDR (alpha {s.Alpha})</td><td class=\"score\">{s.SignificantHypotheses} / {s.HypothesesTested}</td></tr>\n");
            if (s.HeldOut is { } ho)
                sb.Append($"<tr><td>Held-out test</td><td class=\"score\">discovery {ho.DiscoveryScore:0.000} → test {ho.TestScore:0.000} (null {ho.TestNullMean:0.000}, p {ho.TestPValue:0.0000}) {(ho.Supported ? "supported" : "not supported")}</td></tr>\n");
            sb.Append("</tbody></table>\n");
            sb.Append("<ul class=\"small\">\n");
            foreach (var v in s.Verdict) sb.Append($"<li>{Esc(v)}</li>\n");
            sb.Append("</ul>\n");
        }
        if (input.Significance.Count == 0) sb.Append("<p class=\"empty\">No significance run recorded.</p>\n");

        // ---- claim ledger
        sb.Append("<h2>6. Claim ledger</h2>\n");
        sb.Append("<p class=\"small\">Historical claims inherited from the research conversation, with the confidence we are willing to attach to them. ");
        sb.Append("These are context, not inputs to the algorithm.</p>\n<table><thead><tr><th>Claim</th><th>Confidence</th></tr></thead><tbody>\n");
        foreach (var claim in input.ClaimLedger)
        {
            var parts = claim.Split('|', 2);
            sb.Append($"<tr><td>{Esc(parts[0].Trim())}</td><td><span class=\"tag\">{Esc(parts.Length > 1 ? parts[1].Trim() : "unrated")}</span></td></tr>\n");
        }
        sb.Append("</tbody></table>\n");

        sb.Append("</body></html>\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }

    internal static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Css = """
        :root { --bg:#faf8f3; --ink:#241c14; --line:#dfd6c6; --accent:#8a5a2b; --muted:#6b5c49; }
        body { font-family: ui-sans-serif, system-ui, "Segoe UI", Arial; margin:0; padding:28px 40px 80px; background:var(--bg); color:var(--ink); max-width:1180px; }
        h1 { font-size:26px; margin:0 0 6px; } h2 { font-size:18px; margin:34px 0 10px; border-bottom:2px solid var(--line); padding-bottom:5px; }
        h3 { font-size:14px; margin:20px 0 6px; color:#4a3b2c; }
        .meta { color:var(--muted); font-size:13px; margin-bottom:18px; }
        table { border-collapse:collapse; width:100%; font-size:13px; margin-bottom:10px; }
        th,td { border-bottom:1px solid var(--line); padding:5px 8px; text-align:left; vertical-align:top; }
        th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.05em; }
        .score { font-variant-numeric:tabular-nums; font-weight:600; }
        .snippet { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size:11px; }
        .tag { font-size:10px; padding:1px 6px; border-radius:8px; background:#efe7d8; color:#5a4a38; }
        .note { background:#fff8e8; border-left:3px solid var(--accent); padding:10px 14px; font-size:13px; margin:12px 0; }
        .small { font-size:12px; color:var(--muted); }
        .empty { color:var(--muted); font-style:italic; }
        """;
}
