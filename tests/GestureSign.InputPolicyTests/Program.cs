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
Check(t.Draining,"Anchor drains after completion");t.Observe(1,new Point(150,100),true);
Check(t.Draining,"Anchor cannot restart capture");t.Observe(1,new Point(),false);
Check(t.AllReleased,"Drain completes");t.Reset();Check(!t.Draining,"Next gesture available");
t=Session();t.Observe(1,new Point(150,100),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(!t.ShouldComplete(new[]{2}),"Two moving fingers staggered release");t.Observe(1,new Point(),false);
Check(t.ShouldComplete(new[]{1}),"Normal last lift");
t=Session();t.Observe(2,new Point(300,100),true);t.Observe(1,new Point(),false);
Check(!t.ShouldComplete(new[]{1}),"Anchor lift does not finish moving finger");
t=Session();t.Observe(1,new Point(150,100),true);t.Observe(1,new Point(100,100),true);t.Observe(2,new Point(300,100),true);t.Observe(2,new Point(),false);
Check(!t.ShouldComplete(new[]{2}),"Returned moving finger not anchor");
t=Session();t.Observe(2,new Point(),false);Check(!t.ShouldComplete(new[]{2}),"Tap waits for all contacts");
Console.WriteLine($"PASS: {checks} pen and touchscreen policy checks.");