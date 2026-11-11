using System.Text;
using Bomca.Core.Model;

namespace Bomca.Core.Reporting;

/// <summary>
/// Self-contained review sheet: every candidate duplicate pair with both crops, the score breakdown and
/// accept/reject controls that export a decisions file the pipeline re-applies on the next run.
/// </summary>
public static class ReviewSheetBuilder
{
    public static void WriteGlyphPairs(
        string outPath,
        string docDir,
        string docCode,
        IReadOnlyList<SymbolOccurrence> symbols,
        IReadOnlyList<GlyphPair> pairs,
        double threshold)
    {
        var byId = symbols.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>Glyph duplicate review</title>
<style>
:root { --bg:#faf8f3; --ink:#241c14; --line:#dfd6c6; --accent:#8a5a2b; }
body { font-family: ui-sans-serif, system-ui, "Segoe UI", Arial; margin: 0; padding: 24px 32px; background: var(--bg); color: var(--ink); }
h1 { font-size: 20px; margin: 0 0 4px; }
.meta { color:#6b5c49; font-size: 13px; margin-bottom: 18px; }
.toolbar { position: sticky; top: 0; background: var(--bg); padding: 10px 0; border-bottom: 1px solid var(--line); margin-bottom: 12px; z-index: 5; }
button { font: inherit; padding: 5px 12px; border: 1px solid var(--line); background: #fff; border-radius: 6px; cursor: pointer; }
button:hover { border-color: var(--accent); }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { border-bottom: 1px solid var(--line); padding: 6px 8px; text-align: left; vertical-align: middle; }
th { color:#6b5c49; font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing:.04em; }
td img { height: 72px; width: 72px; object-fit: contain; background: #fff; border: 1px solid var(--line); border-radius: 4px; }
.score { font-variant-numeric: tabular-nums; }
.bar { height: 8px; background: #e9e1d3; border-radius: 4px; overflow: hidden; min-width: 80px; }
.bar > div { height: 100%; background: var(--accent); }
tr.accepted { background: #eef7ea; }
tr.rejected { background: #fbeeee; }
.kind { font-size: 11px; padding: 2px 6px; border-radius: 10px; background: #efe7d8; }
</style></head><body>
""");
        sb.Append($"<h1>Glyph duplicate review &mdash; {docCode}</h1>\n");
        sb.Append($"<div class=\"meta\">Threshold {threshold:0.00} &middot; {pairs.Count} candidate pairs &middot; generated {DateTime.UtcNow:u}<br>");
        sb.Append("Accept = same character. Reject = different characters. Decisions export to <code>review/decisions-" + docCode + ".json</code>.</div>\n");
        sb.Append("<div class=\"toolbar\"><button onclick=\"exportDecisions()\">Download decisions JSON</button> ");
        sb.Append("<span id=\"counter\" style=\"margin-left:12px;color:#6b5c49\"></span></div>\n");
        sb.Append("<table><thead><tr><th>#</th><th>A</th><th>B</th><th>Score</th><th>Breakdown</th><th>Kind</th><th>Decision</th></tr></thead><tbody>\n");

        var i = 0;
        foreach (var p in pairs)
        {
            i++;
            var a = byId.GetValueOrDefault(p.A);
            var b = byId.GetValueOrDefault(p.B);
            var aImg = a is null ? "" : $"<img src=\"{a.CropPath}\" alt=\"{a.Id}\">";
            var bImg = b is null ? "" : $"<img src=\"{b.CropPath}\" alt=\"{b.Id}\">";
            var pct = (int)Math.Round(p.Breakdown.Combined * 100);
            sb.Append($"<tr id=\"row{i}\" data-a=\"{p.A}\" data-b=\"{p.B}\">");
            sb.Append($"<td class=\"score\">{i}</td>");
            sb.Append($"<td>{aImg}<div style=\"font-size:11px\">{p.A}</div></td>");
            sb.Append($"<td>{bImg}<div style=\"font-size:11px\">{p.B}</div></td>");
            sb.Append($"<td><div class=\"bar\"><div style=\"width:{pct}%\"></div></div><span class=\"score\">{p.Breakdown.Combined:0.000}</span></td>");
            sb.Append($"<td class=\"score\" style=\"font-size:11px;color:#6b5c49\">iou {p.Breakdown.Iou:0.00} &middot; ncc {p.Breakdown.Ncc:0.00} &middot; chamfer {p.Breakdown.Chamfer:0.00} &middot; moment {p.Breakdown.Moment:0.00} &middot; feat {p.Breakdown.Feature:0.00}");
            if (p.Breakdown.BestRotationDeg != 0 || p.Breakdown.Mirrored)
                sb.Append($" &middot; rot {p.Breakdown.BestRotationDeg:0}&deg;{(p.Breakdown.Mirrored ? " mirrored" : "")}");
            sb.Append("</td>");
            sb.Append($"<td><span class=\"kind\">{p.Kind}</span></td>");
            sb.Append($"<td><button onclick=\"setDecision({i},'accepted')\">Accept</button> <button onclick=\"setDecision({i},'rejected')\">Reject</button></td>");
            sb.Append("</tr>\n");
        }

        sb.Append("""
</tbody></table>
<script>
const decisions = {};
function setDecision(i, value) {
  const row = document.getElementById('row' + i);
  row.classList.remove('accepted','rejected');
  row.classList.add(value);
  decisions[i] = { a: row.dataset.a, b: row.dataset.b, decision: value };
  updateCounter();
}
function updateCounter() {
  const n = Object.keys(decisions).length;
  document.getElementById('counter').textContent = n + ' decided';
}
function exportDecisions() {
  const payload = { exported: new Date().toISOString(), decisions: Object.values(decisions) };
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'decisions.json';
  a.click();
}
</script>
</body></html>
""");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }
}
