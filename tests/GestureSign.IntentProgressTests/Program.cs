using GestureSign.Foundation.Intent;
using GestureSign.WinUI.Services;
int checks = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
IntentSample Sample(IntentLabel label, string session) => new() { Label=label, Session=session,
 Frames=Enumerable.Range(0,8).Select(i => new IntentFrame(i*20,new[]{new IntentPoint(1,i*10,0),new IntentPoint(2,i*10,20)})).ToArray() };
var empty=IntentTrainingProgress.FromSamples([]);
Check(empty.Scroll.MissingSamples==40 && empty.Scroll.MissingSessions==4 && !empty.Ready,"Empty progress");
var many=Enumerable.Range(0,77).Select(_=>Sample(IntentLabel.Scroll,"one")).ToList();
var p=IntentTrainingProgress.FromSamples(many);
Check(p.Scroll.Samples==77 && p.Scroll.Sessions==1 && p.Scroll.MissingSessions==3 && !p.Ready,"Many samples in one session");
Check(IntentTrainingProgress.FromSamples(many.Concat(many)).Scroll.Samples==77,"Deduplicate IDs");
Check(IntentTrainingProgress.FromSamples(many.Append(Sample(IntentLabel.Unknown,"unknown"))).Scroll.Sessions==1,"Unlabeled excluded");
Check(IntentTrainingProgress.FromSamples(new[]{Sample(IntentLabel.Gesture,"")}).Gesture.Samples==0,"Missing session excluded");
var full=new List<IntentSample>();
foreach(var label in new[]{IntentLabel.Scroll,IntentLabel.Gesture}) for(int i=0;i<40;i++) full.Add(Sample(label,"session"+(i%4)));
Check(IntentTrainingProgress.FromSamples(full).Ready,"Both labels reach threshold");
full[0].Label=IntentLabel.Unknown;
Check(IntentTrainingProgress.FromSamples(full).Scroll.MissingSamples==1,"Revoked label updates progress");
var dir=Path.Combine(Path.GetTempPath(),"GestureSign-progress-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
foreach(var sample in many) IntentFiles.Write(Path.Combine(dir,sample.Id+".json"),sample);
File.WriteAllText(Path.Combine(dir,"bad.json"),"invalid");
var invalid=Sample(IntentLabel.Scroll,"invalid"); invalid.Frames=[];IntentFiles.Write(Path.Combine(dir,invalid.Id+".json"),invalid);
var disk=IntentTrainingProgress.Read(dir);
Check(disk.Scroll.Samples==77 && disk.Scroll.Sessions==1,"Persisted valid samples only");
Check(IntentTrainingProgress.Read(dir)==disk,"Reload retains progress");
Console.WriteLine($"PASS {checks} progress checks");
if(args.Contains("--local")) Console.WriteLine(IntentTrainingProgress.Read(IntentFiles.SamplesPath));
