using System.Text;
using Bomca.Core.Corpus;
using Bomca.Core.External;

namespace Bomca.Core.Reporting;

public static class ClaimReportBuilder
{
    public static void Write(string outPath, ClaimVerificationResult result, CorpusIndex corpus)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Claim verification</title><style>")
          .Append("body{font-family:ui-sans-serif,system-ui,'Segoe UI',Arial;margin:0;padding:24px 32px;background:#faf8f3;color:#241c14;max-width:1100px}"
                + "h1{font-size:22px;margin:0 0 6px}h2{font-size:16px;margin:26px 0 8px;border-bottom:1px solid #dfd6c6;padding-bottom:4px}"
                + "table{border-collapse:collapse;width:100%;font-size:13px;margin-bottom:10px}th,td{border-bottom:1px solid #dfd6c6;padding:5px 8px;text-align:left;vertical-align:top}"
                + "th{color:#6b5c49;font-size:11px;text-transform:uppercase;letter-spacing:.05em}.score{font-variant-numeric:tabular-nums;font-weight:600}"
                + ".ok{color:#1c6b2a}.bad{color:#9b2c2c}.muted{color:#6b5c49;font-size:12px}"
                + ".note{background:#fff8e8;border-left:3px solid #8a5a2b;padding:10px 14px;font-size:13px;margin:12px 0}")
          .Append("</style></head><body>\n");
        sb.Append($"<h1>External claim verification &mdash; {result.Source}</h1>\n");
        sb.Append($"<div class=\"muted\">Document {result.Doc} &middot; generated {result.GeneratedUtc}</div>\n");

        sb.Append("<div class=\"note\"><strong>What this tests.</strong> The claim is not re-translated. Instead its "
                + "falsifiable consequences are tested against data that is independent of the claim: whether numeral-like "
                + "glyph shapes cluster in runs, whether the claimed calendar statements occur at the verses the claim "
                + "names, whether the two claimed segments differ structurally, and whether the claimed content "
                + "concentrates where the claim says it does.</div>\n");

        sb.Append("<h2>Assessment</h2>\n<p>").Append(Esc(result.OverallAssessment)).Append("</p>\n");
        sb.Append("<ul class=\"muted\">\n");
        foreach (var f in result.Findings) sb.Append($"<li>{Esc(f)}</li>\n");
        sb.Append("</ul>\n");

        sb.Append("<h2>Numeral-run test</h2>\n<table><thead><tr><th>Quantity</th><th>Observed</th><th>Chance</th></tr></thead><tbody>\n");
        sb.Append($"<tr><td>Glyphs classified numeral-like</td><td class=\"score\">{result.ObservedRuns?.Count}</td><td>-</td></tr>\n");
        sb.Append($"<tr><td>Runs</td><td class=\"score\">{result.ObservedRuns?.Runs}</td><td class=\"score\">{result.NullRuns?.Runs}</td></tr>\n");
        sb.Append($"<tr><td>Mean run length</td><td class=\"score\">{result.ObservedRuns?.MeanRun:0.00}</td><td class=\"score\">{result.NullRuns?.MeanRun:0.00}</td></tr>\n");
        sb.Append($"<tr><td>Longest run</td><td class=\"score\">{result.ObservedRuns?.MaxRun}</td><td class=\"score\">{result.NullRuns?.MaxRun}</td></tr>\n");
        sb.Append($"<tr><td>p-value</td><td class=\"score {(result.NumeralRunPValue <= 0.05 ? "ok" : "bad")}\">{result.NumeralRunPValue}</td><td>-</td></tr>\n");
        sb.Append("</tbody></table>\n");
        sb.Append("<table><thead><tr><th>Glyph</th><th>Row</th><th>Index</th><th>Score</th><th>Features</th></tr></thead><tbody>\n");
        foreach (var n in result.NumeralLikeGlyphs.Take(40))
            sb.Append($"<tr><td>{n.Id}</td><td>{n.Row + 1}</td><td>{n.GlobalIndex}</td><td class=\"score\">{n.Score:0.00}</td><td class=\"muted\">{Esc(n.Reason)}</td></tr>\n");
        sb.Append("</tbody></table>\n");

        sb.Append("<h2>Claimed calendar statements vs the corpus</h2>\n");
        sb.Append("<table><thead><tr><th>Claim</th><th>Pattern</th><th>Occurrences</th><th>Found in</th><th>Status</th></tr></thead><tbody>\n");
        foreach (var t in result.TextClaims)
            sb.Append($"<tr><td>{Esc(t.Claim)}</td><td><code>{Esc(t.Pattern)}</code></td><td class=\"score\">{t.Occurrences}</td>" +
                      $"<td class=\"muted\">{Esc(string.Join(", ", t.FoundRefs.Take(4)))}</td>" +
                      $"<td class=\"{(t.Status == "consistent" ? "ok" : "bad")}\">{Esc(t.Status)}</td></tr>\n");
        sb.Append("</tbody></table>\n");

        sb.Append("<h2>Claimed segments</h2>\n<table><thead><tr><th>Segment</th><th>Glyphs</th><th>Median height</th><th>Glyphs/row</th><th>Classes</th><th>Class overlap</th></tr></thead><tbody>\n");
        foreach (var s in result.Segments)
            sb.Append($"<tr><td>{Esc(s.Name)}</td><td class=\"score\">{s.Glyphs}</td><td class=\"score\">{s.MedianGlyphHeight:0.0}</td><td class=\"score\">{s.GlyphsPerRow:0.0}</td><td class=\"score\">{s.Classes}</td><td class=\"score\">{s.ClassOverlapWithOtherSegment:P0}</td></tr>\n");
        sb.Append("</tbody></table>\n");
        foreach (var s in result.Segments.Where(s => s.Reading.Length > 0).Take(1))
            sb.Append($"<p class=\"muted\">{Esc(s.Reading)}</p>\n");

        sb.Append("<h2>Does the claimed content concentrate where the claim says?</h2>\n");
        sb.Append("<table><thead><tr><th>Segment</th><th>Best window</th><th>Hits</th><th>Distinct</th><th>Control mean</th><th>p</th><th>Verdict</th></tr></thead><tbody>\n");
        foreach (var a in result.AnchorWindows)
            sb.Append($"<tr><td>{Esc(a.Segment)}</td><td>{Esc(a.Reference)} <span class=\"muted\">w{a.WordStart}</span><br><span class=\"muted\">{Esc(a.Snippet)}</span></td>" +
                      $"<td class=\"score\">{a.Hits}</td><td class=\"score\">{a.DistinctTerms}</td><td class=\"score\">{a.ControlMeanHits:0.0}</td>" +
                      $"<td class=\"score {(a.PValue <= 0.05 ? "ok" : "bad")}\">{a.PValue}</td><td>{Esc(a.Verdict)}</td></tr>\n");
        sb.Append("</tbody></table>\n");

        if (result.TableInconsistencies.Count > 0)
        {
            sb.Append("<h2>Internal inconsistencies in the published tables</h2><ul class=\"muted\">\n");
            foreach (var i in result.TableInconsistencies) sb.Append($"<li>{Esc(i)}</li>\n");
            sb.Append("</ul>\n");
        }

        sb.Append("</body></html>\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }


    /// <summary>Fuzzy contextual search sheet: ranked focus regions and the closest verses.</summary>
    public static void WriteFocus(string outPath, Bomca.Core.Semantic.ContextualSearchResult result,
        Dictionary<string, object> verseRanking)
    {
        const string Css = """
            body{font-family:ui-sans-serif,system-ui,'Segoe UI',Arial;margin:0;padding:24px 32px;background:#faf8f3;color:#241c14;max-width:1100px}
            h1{font-size:22px;margin:0 0 6px}
            h2{font-size:16px;margin:26px 0 8px;border-bottom:1px solid #dfd6c6;padding-bottom:4px}
            table{border-collapse:collapse;width:100%;font-size:13px;margin-bottom:10px}
            th,td{border-bottom:1px solid #dfd6c6;padding:5px 8px;text-align:left;vertical-align:top}
            th{color:#6b5c49;font-size:11px;text-transform:uppercase;letter-spacing:.05em}
            .score{font-variant-numeric:tabular-nums;font-weight:600}
            .muted{color:#6b5c49;font-size:12px}
            .note{background:#fff8e8;border-left:3px solid #8a5a2b;padding:10px 14px;font-size:13px;margin:12px 0}
            """;

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>Fuzzy contextual search</title><style>");
        sb.Append(Css);
        sb.Append("</style></head><body>");
        sb.Append("<h1>Fuzzy contextual search &mdash; ").Append(Esc(result.Source)).Append("</h1>");
        sb.Append("<div class='muted'>").Append(Esc(result.Method))
          .Append(" &middot; window ").Append(result.Window)
          .Append(" words, stride ").Append(result.Stride)
          .Append(" &middot; ").Append(Esc(result.GeneratedUtc)).Append("</div>");
        sb.Append("<div class='note'>This step does not translate anything. It locates the corpus regions where an "
                + "approximate, layman-worded paraphrase best fits, so the symbol-level search can be restricted to a "
                + "few thousand words instead of the whole book.</div>");
        sb.Append("<p><strong>").Append(result.Regions.Count).Append("</strong> focused region(s) covering <strong>")
          .Append(result.FocusCorpusWords.ToString("N0")).Append("</strong> of ")
          .Append(result.TotalCorpusWords.ToString("N0")).Append(" words &mdash; a ")
          .Append(result.SearchReductionFactor.ToString("0.0")).Append("&times; reduction.</p>");

        foreach (var seg in result.Segments)
        {
            sb.Append("<h2>").Append(Esc(seg.Name)).Append("</h2>");
            sb.Append("<p class='muted'>").Append(seg.QueryTerms).Append(" query terms, ")
              .Append(seg.ConceptPhrases).Append(" concept phrases &middot; best window score <span class='score'>")
              .Append(seg.BestWindowScore.ToString("0.000")).Append("</span> vs control <span class='score'>")
              .Append(seg.ControlMean.ToString("0.000")).Append("</span> (p = ").Append(seg.PValue).Append(")</p>");
            sb.Append("<table><thead><tr><th>Region</th><th>Words</th><th>Score</th><th>Text</th><th>Matched terms</th></tr></thead><tbody>");
            foreach (var r in seg.Windows)
            {
                sb.Append("<tr><td>").Append(Esc(r.Reference)).Append("</td><td class='score'>")
                  .Append(r.Start).Append("..").Append(r.End).Append("</td><td class='score'>")
                  .Append(r.Score.ToString("0.000")).Append("</td><td class='muted'>").Append(Esc(r.Snippet))
                  .Append("</td><td class='muted'>").Append(Esc(string.Join(", ", r.MatchedTerms))).Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<h2>Closest verses</h2>");
        foreach (var pair in verseRanking)
        {
            sb.Append("<h3>").Append(Esc(pair.Key)).Append("</h3>");
            sb.Append("<table><thead><tr><th>Verse</th><th>Score</th><th>Matched</th><th>Text</th></tr></thead><tbody>");
            if (pair.Value is System.Collections.IEnumerable rows)
            {
                foreach (var row in rows)
                {
                    var t = row.GetType();
                    var reference = t.GetProperty("reference")?.GetValue(row)?.ToString() ?? "";
                    var score = t.GetProperty("score")?.GetValue(row)?.ToString() ?? "";
                    var matched = t.GetProperty("matched")?.GetValue(row)?.ToString() ?? "";
                    var text = t.GetProperty("text")?.GetValue(row)?.ToString() ?? "";
                    sb.Append("<tr><td>").Append(Esc(reference)).Append("</td><td class='score'>").Append(score)
                      .Append("</td><td class='score'>").Append(matched).Append("</td><td class='muted'>")
                      .Append(Esc(text)).Append("</td></tr>");
                }
            }
            sb.Append("</tbody></table>");
        }

        if (result.Notes.Count > 0)
        {
            sb.Append("<ul class='muted'>");
            foreach (var n in result.Notes) sb.Append("<li>").Append(Esc(n)).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append("</body></html>");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
