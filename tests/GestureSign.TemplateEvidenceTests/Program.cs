using System.Drawing;
using System.Text.Json;
using GestureSign.PointPatterns;
using GestureSign.Foundation.Intent;
var l = new[]{new Point(0,0),new Point(0,100),new Point(100,100)};
var line = Enumerable.Range(0,20).Select(i=>new Point(i%2, i*10)).ToArray();
void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
Check(TemplateTurnEvidence.MissingTurn([line,line],[l,l]),"Scroll jitter missed");
Check(!TemplateTurnEvidence.MissingTurn([l,l],[l,l]),"Recorded L rejected");
var scaled=l.Select(p=>new Point(p.X*3+50,p.Y*3-20)).ToArray();
Check(!TemplateTurnEvidence.MissingTurn([scaled,scaled],[l,l]),"Scale/translation changed result");
Check(!TemplateTurnEvidence.MissingTurn([line,l],[l,l]),"One finger alone triggered rejection");
Check(!TemplateTurnEvidence.MissingTurn([line,line],[line,line]),"Non-turn template rejected");
if(args.Length==2){
var sample=IntentFiles.Read<IntentSample>(args[0]);
using var doc=JsonDocument.Parse(File.ReadAllText(args[1]));
var template=doc.RootElement.EnumerateArray().First(g=>g.GetProperty("Name").GetString()==sample.Candidate).GetProperty("PointPatterns")[0].GetProperty("Points").EnumerateArray().Select(a=>a.EnumerateArray().Select(v=>{var xy=v.GetString().Split(',');return new Point(int.Parse(xy[0]),int.Parse(xy[1]));}).ToArray()).ToArray();
Point[][] Points(IntentSample s)=>s.Frames.SelectMany(f=>f.Points).Select(p=>p.Contact).Distinct().Select(id=>s.Frames.SelectMany(f=>f.Points.Where(p=>p.Contact==id)).Select(p=>new Point((int)p.X,(int)p.Y)).ToArray()).ToArray();
var captured=Points(sample);
Console.WriteLine("Template turns: "+string.Join("/",template.Select(TemplateTurnEvidence.Turn)));
Console.WriteLine("Reported scroll turns: "+string.Join("/",captured.Select(TemplateTurnEvidence.Turn)));
Check(TemplateTurnEvidence.MissingTurn(captured,template),"Reported false close was not detected");
var positives=Directory.GetFiles(Path.GetDirectoryName(args[0]),"*.json").Select(IntentFiles.Read<IntentSample>).Where(s=>s.Label==IntentLabel.Gesture).ToArray();
int rejected=positives.Count(s=>TemplateTurnEvidence.MissingTurn(Points(s),template));
Console.WriteLine($"Labeled intentional samples flagged: {rejected}/{positives.Length} (diagnostic, not held-out accuracy)");
}
Console.WriteLine("PASS template evidence checks");

var diagonal = new[] { new System.Drawing.Point(0,0), new System.Drawing.Point(40,80), new System.Drawing.Point(43,84), new System.Drawing.Point(50,84), new System.Drawing.Point(90,170) };
var deliberate = new[] { new System.Drawing.Point(0,0), new System.Drawing.Point(0,100), new System.Drawing.Point(100,100) };
foreach (var count in new[] { 1, 2, 3, 4 })
{
    if (!TemplateTurnEvidence.MissingTurn(Enumerable.Repeat(diagonal,count).ToArray(), Enumerable.Repeat(deliberate,count).ToArray())) throw new Exception("Diagonal hook mistaken for sustained L turn.");
    if (TemplateTurnEvidence.MissingTurn(Enumerable.Repeat(deliberate,count).ToArray(), Enumerable.Repeat(deliberate,count).ToArray())) throw new Exception("Deliberate L rejected.");
}
Console.WriteLine("PASS: diagonal-hook versus deliberate-L checks for 1–4 fingers.");

Check(TemplateTurnEvidence.StrongMatch([l,l], [l,l], 85, 75), "Strong threshold match was rejected.");
Check(TemplateTurnEvidence.StrongMatch([scaled,scaled], [l,l], 96, null), "Scale/translation or absent runner-up changed strong evidence.");
Check(!TemplateTurnEvidence.StrongMatch([l,l], [l,l], 84.99, 70), "Low similarity was trusted.");
Check(!TemplateTurnEvidence.StrongMatch([l,l], [l,l], 95, 85.01), "Ambiguous templates were trusted.");
Check(!TemplateTurnEvidence.StrongMatch([l,line], [l,l], 99, 60), "One straight finger was trusted.");
Check(!TemplateTurnEvidence.StrongMatch([line,line], [line,line], 99, 60), "Straight scrolling templates were trusted.");
Check(!TemplateTurnEvidence.StrongMatch([l,l,l], [l,l,l], 99, 60), "Three-finger capture used two-finger exemption.");
Check(!TemplateTurnEvidence.StrongMatch([l,l], [l,l], double.NaN, 60), "NaN similarity was trusted.");
Check(!TemplateTurnEvidence.StrongMatch([l,l], [l,l], 99, double.NaN), "NaN runner-up was trusted.");
Console.WriteLine("PASS: strong-template thresholds, ambiguity, contact count and scrolling safeguards.");

var lowerE = new Point[]
{
    new(0, 45), new(90, 45), new(95, 30), new(84, 14), new(64, 8), new(45, 14),
    new(29, 33), new(25, 55), new(33, 73), new(53, 80), new(82, 80), new(142, 80)
};
var secondFinger = lowerE.Select(p => new Point(p.X + 30, p.Y + 35)).ToArray();
var resized = new[] { lowerE, secondFinger }.Select(path => path.Select(p => new Point(p.X * 2, p.Y * 2)).ToArray()).ToArray();
Check(TemplateTurnEvidence.Turn(lowerE) < 45, "The e fixture no longer exercises the interior-turn fallback.");
Check(TemplateTurnEvidence.StrongMatch(resized, [lowerE, secondFinger], 94, 76), "A deliberate two-finger e with aligned ends was rejected.");
Check(!TemplateTurnEvidence.StrongMatch([line, line], [lowerE, secondFinger], 94, 76), "Straight scrolling gained the looping-template exemption.");
Check(!TemplateTurnEvidence.StrongMatch([lowerE, line], [lowerE, secondFinger], 94, 76), "A single curved finger gained the two-finger exemption.");
Check(!TemplateTurnEvidence.StrongMatch(resized, [lowerE, secondFinger], 94, 89), "An ambiguous e gained the strong-template exemption.");
Console.WriteLine("PASS: two-finger e with aligned ends, straight scrolling and ambiguous candidates.");
