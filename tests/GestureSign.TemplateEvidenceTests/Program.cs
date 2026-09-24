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
