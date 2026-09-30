using System.Drawing;
using GestureSign.Common.Input;
int checks=0;
void Check(bool condition,string name) { checks++; if(!condition) throw new Exception(name); }
var tip=DeviceStates.Tip; var range=DeviceStates.InRange; var barrel=DeviceStates.RightClickButton; var eraser=DeviceStates.Eraser;
Check(PenGesturePolicy.Normalize(barrel)==(barrel|tip),"Legacy barrel contact mode");
Check(PenGesturePolicy.Normalize(eraser)==(eraser|tip),"Legacy eraser contact mode");
Check(PenGesturePolicy.IsActive(barrel,barrel|tip|range),"Surface barrel contact");
Check(!PenGesturePolicy.IsActive(barrel,tip|range),"Ordinary writing excluded");
Check(PenGesturePolicy.IsActive(eraser,eraser|range),"Eraser HID usage");
Check(PenGesturePolicy.IsActive(eraser,DeviceStates.Invert|tip|range),"Inverted pen HID usage");
Check(PenGesturePolicy.IsActive(tip,tip),"Contact without range bit");
Check(PenGesturePolicy.IsActive(range,range),"Hover without barrel");
Check(!PenGesturePolicy.IsActive(0,tip|range),"Disabled");
Check(!PenGesturePolicy.IsActive(barrel,barrel),"Out of range");
Check(!PenGesturePolicy.IsActive(barrel,0),"Released");
TouchScreenReleaseTracker Session() {
 var t=new TouchScreenReleaseTracker(); t.Observe(1,new Point(100,100),true);t.Observe(2,new Point(200,100),true);return t;
}
var t=Session(); t.Observe(1,new Point(105,105),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(t.ShouldComplete(new[]{2}),"Moving lift with stationary anchor");t.Complete();
Check(t.WaitingForContact,"Anchor drains after completion");t.Observe(1,new Point(150,100),true);
Check(t.WaitingForContact,"Anchor cannot restart capture");t.Observe(1,new Point(),false);
Check(t.AllReleased,"Drain completes");t.Reset();Check(!t.WaitingForContact,"Next gesture available");
t=Session();t.Observe(1,new Point(150,100),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(!t.ShouldComplete(new[]{2}),"Two moving fingers staggered release");t.Observe(1,new Point(),false);
Check(t.ShouldComplete(new[]{1}),"Normal last lift");
t=Session();t.Observe(2,new Point(300,100),true);t.Observe(1,new Point(),false);
Check(!t.ShouldComplete(new[]{1}),"Anchor lift does not finish moving finger");
t=Session();t.Observe(1,new Point(150,100),true);t.Observe(1,new Point(100,100),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(!t.ShouldComplete(new[]{2}),"Returned moving finger not anchor");
t=Session();t.Observe(2,new Point(),false);Check(!t.ShouldComplete(new[]{2}),"Tap waits for all contacts");

// Repeated gestures with an uninterrupted anchor and reused or changed contact IDs.
t=Session();
for(var iteration=0;iteration<3;iteration++) {
 var movingId=iteration==2 ? 7 : 2;
 if(iteration>0) {
  Check(!t.Observe(1,new Point(110+iteration*30,100),true),"Anchor updates never rearm");
  Check(!t.TryRearm(false),"No restart without a fresh contact");
  Check(!t.ShouldComplete(new[]{2}),"No duplicate completion while waiting");
  var landed=t.Observe(movingId,new Point(200,100),true);
  Check(landed,"Returning finger is a new contact even with reused ID");
  Check(t.TryRearm(landed),"Rearm with held anchor");
 }
 t.Observe(movingId,new Point(300,100),true);
 t.Observe(movingId,new Point(),false);
 Check(t.ShouldComplete(new[]{movingId}),"Every repeated gesture completes on moving lift");
 t.Complete();
 Check(t.WaitingForContact,"Anchor remains available after every action");
}
Check(!t.Observe(1,new Point(),false),"Anchor release cannot restart");
Check(!t.ShouldComplete(new[]{1}),"Anchor release cannot repeat last action");
Check(t.AllReleased,"Last anchor release clears contacts");
t.Reset();Check(!t.WaitingForContact,"Full release permits a fresh session");

// Two anchors delivered in separate HID reports and the moving finger first.
t=new TouchScreenReleaseTracker();
t.Observe(1,new Point(100,100),true);t.Observe(3,new Point(120,100),true);
t.Observe(2,new Point(200,100),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(t.ShouldComplete(new[]{2}),"Multiple stationary anchors permit early completion");t.Complete();
var fresh=t.Observe(2,new Point(200,100),true);
t.Observe(3,new Point(125,100),true);t.Observe(1,new Point(105,100),true);
Check(t.TryRearm(fresh),"Partial frames preserve multiple anchors");
t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(t.ShouldComplete(new[]{2}),"Second gesture has fresh stroke history");t.Complete();
t.Observe(1,new Point(),false);Check(!t.TryRearm(false),"Lifting one anchor does not start capture");
fresh=t.Observe(8,new Point(200,100),true);Check(t.TryRearm(fresh),"Remaining anchor can be reused");
t.Observe(3,new Point(160,100),true);t.Observe(8,new Point(300,100),true);t.Observe(8,new Point(),false);
Check(!t.ShouldComplete(new[]{8}),"Moving anchor restores normal last-lift behavior");
t.Observe(3,new Point(),false);Check(t.ShouldComplete(new[]{3}),"Moving contacts finish on final lift");
t.Complete();Check(!t.WaitingForContact,"No anchors left after normal completion");
Console.WriteLine($"PASS: {checks} pen and touchscreen policy checks.");