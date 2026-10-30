using GestureSign.Shortcuts;
using ManagedWinapi;
using ManagedWinapi.Hooks;
using GestureSign.CorePlugins.HotKey;
using System.Windows.Forms;

static class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    [STAThread] static void Main()
    {
        var session = new ShortcutRecordingSession();
        var box = new object();
        Check(session.Begin(box), "Initial focus starts recorder");
        int old = session.Generation;
        Check(session.Begin(box, new[]{91}), "Preset explicitly starts a new generation");
        int selected = session.Generation;
        Check(!session.Begin(box) && session.Generation == selected, "Late GotFocus cannot reset preset generation");
        Check(session.PresetModifiers.SequenceEqual(new[]{91}), "Late GotFocus retains LWin");
        Check(!session.IsCurrent(old) && session.IsCurrent(selected), "Queued stop or commit from old recording is ignored");
        Check(ShortcutKeys.IsLockShortcut(0,session.PresetModifiers,new[]{76}), "L after late focus produces LWin+L");
        session.Stop();
        Check(!session.IsCurrent(selected) && session.PresetModifiers.Length==0, "Cancel invalidates queued work");
        Check(session.Begin(box,new[]{92}) && !session.Begin(box), "Preset before focus also survives late focus");
        Check(ShortcutKeys.IsLockShortcut(0,session.PresetModifiers,new[]{76}), "RWin preset survives focus");
        Check(session.Begin(new object()) && session.PresetModifiers.Length==0, "Different recorder never inherits preset");
        Check(ShortcutKeys.Normalize(17,29,0)==162,"Left Ctrl scan");
        Check(ShortcutKeys.Normalize(17,29,1)==163,"Right Ctrl extended");
        Check(ShortcutKeys.Normalize(18,56,1)==165,"Right Alt extended");
        Check(ShortcutKeys.Normalize(16,54,0)==161,"Right Shift scan");
        foreach (int target in new[]{160,161,162,163,164,165,91,92})
        {
            var tap=new ModifierTapState();
            Check(!tap.Process(target,true,false,target),"Wait for release");
            Check(!tap.Process(target,true,false,target),"Repeat ignored");
            Check(tap.Process(target,false,false,target),"Single modifier tap");
            Check(!tap.Process(target,false,false,target),"No duplicate release");
            tap.Process(target,true,false,target); tap.Process(67,true,false,target); tap.Process(67,false,false,target);
            Check(!tap.Process(target,false,false,target),"Combination cancels tap");
            tap.Process(67,true,false,target); tap.Process(target,true,false,target); tap.Process(67,false,false,target);
            Check(!tap.Process(target,false,false,target),"Preheld key cancels tap");
            Check(!tap.Process(target,true,true,target)&&!tap.Process(target,false,true,target),"Injected ignored");
            tap.Process(target,true,false,target); tap.Reset();
            Check(!tap.Process(target,false,false,target),"Reload cancels tap");
        }
        Check(ShortcutKeys.Matches(2,null,k=>k==163),"Legacy accepts right");
        Check(ShortcutKeys.Matches(2,null,k=>k==162),"Legacy accepts left");
        Check(!ShortcutKeys.Matches(2,new[]{163},k=>k==162),"Sided rejects left");
        Check(ShortcutKeys.Matches(2,new[]{163},k=>k==163),"Sided accepts right");
        Check(!ShortcutKeys.Matches(2,new[]{163},k=>k==163||k==160),"Extra modifier rejected");
        Check(ShortcutKeys.Resolve(2,new[]{163}).SequenceEqual(new[]{163}),"No extra left Ctrl");
        Check(ShortcutKeys.Resolve(2,null).SequenceEqual(new[]{162}),"Legacy sender unchanged");
        using var right=new Hotkey{KeyCode=65,ModifierKeys=2,ModifierKeyCodes=new[]{163}};
        using var left=new Hotkey{KeyCode=65,ModifierKeys=2,ModifierKeyCodes=new[]{162}};
        Check(!right.Equals(left),"Sided identity");
        // Native input test: observe an injected, otherwise harmless right Ctrl tap.
        var events=new List<(int key,int flags,int msg,int scan,int time)>();
        using var hook=new LowLevelKeyboardHook();
        hook.KeyIntercepted+=(int msg,int vk,int scan,int flags,int time,IntPtr extra,ref bool handled)=>
        {
            events.Add((ShortcutKeys.Normalize(vk,scan,flags),flags,msg,scan,time));
            // Stop test-generated modifiers from opening Start or activating an input method.
            if ((flags & 16) != 0 && (vk == 162 || vk == 163 || vk == 91 || vk == 92 || vk == 17 || (vk >= 37 && vk <= 40))) handled = true;
        };
        hook.StartHook();
        int fired=0;
        using var tapHotkey=new Hotkey{KeyCode=163};
        tapHotkey.HotkeyPressed+=(_,_)=>fired++;
        tapHotkey.Register();
        var plugin=new HotKeyPlugin();
        Check(plugin.Deserialize("{\"KeyCode\":[163],\"Control\":false}"),"Deserialize standalone");
        plugin.Gestured(null);
        var limit=DateTime.UtcNow.AddMilliseconds(300);
        while(DateTime.UtcNow<limit){Application.DoEvents();Thread.Sleep(5);}
        Check(events.Any(e=>e.key==163&&(e.flags&16)!=0&&e.msg==0x100),"Native right Ctrl down");
        Check(events.Any(e=>e.key==163&&(e.flags&16)!=0&&e.msg==0x101),"Native right Ctrl up");
        Check(!events.Any(e=>e.key==162),"No left Ctrl injected");
        Check(fired==0,"Injected command cannot recursively trigger hotkey");
        foreach (int code in new[] {162,163,91,92})
        {
            foreach (bool fallback in new[] {false,true})
            {
                events.Clear();
                GestureSign.Common.Plugins.IPlugin sender = fallback
                    ? (GestureSign.Common.Plugins.IPlugin)Activator.CreateInstance(typeof(GestureSign.Common.Plugins.IPlugin).Assembly.GetType("GestureSign.Common.Plugins.BuiltInHotKeyPlugin", true)!, true)!
                    : new HotKeyPlugin();
                var config = new HotKeySettings { KeyCode = new List<Keys> {(Keys)code} };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
                Check(sender.Deserialize(json), $"Deserialize {code} fallback={fallback}");
                Check(sender.Gestured(null), $"Send {code} fallback={fallback}");
                limit=DateTime.UtcNow.AddMilliseconds(120);
                while(DateTime.UtcNow<limit){Application.DoEvents();Thread.Sleep(5);}
                var keyEvents=events.Where(e=>(e.flags&16)!=0).ToArray();
                Check(keyEvents.Count(e=>e.key==code&&(e.msg==0x100||e.msg==0x104))==1, $"Exactly one down {code}");
                Check(keyEvents.Count(e=>e.key==code&&(e.msg==0x101||e.msg==0x105))==1, $"Exactly one up {code}");
                Check(keyEvents.All(e=>e.key==code), $"No other key injected for {code}");
                Check(keyEvents.All(e=>((e.flags&1)!=0)==(code!=162)), $"Correct extended flags for {code}");
                Check(fired==0, "Modifier command cannot recursively trigger its binding");
            }
        }
        foreach (var config in new[] {
            new HotKeySettings { Windows=true, KeyCode=new() {Keys.L} },
            new HotKeySettings { Windows=true, ModifierKeyCodes=new[]{91}, KeyCode=new() {Keys.L} },
            new HotKeySettings { ModifierKeyCodes=new[]{92}, KeyCode=new() {Keys.L} },
            new HotKeySettings { KeyCode=new() {Keys.LWin,Keys.L} }
        })
        foreach (bool fallback in new[]{false,true})
        {
            GestureSign.Common.Plugins.IPlugin sender = fallback
                ? (GestureSign.Common.Plugins.IPlugin)Activator.CreateInstance(typeof(GestureSign.Common.Plugins.IPlugin).Assembly.GetType("GestureSign.Common.Plugins.BuiltInHotKeyPlugin", true)!, true)!
                : new HotKeyPlugin();
            int locks=0;
            bool accepted=true;
            // Replace only this instance's native call. Never lock the user's session.
            sender.GetType().GetField("_lockWorkStation", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(sender, (Func<bool>)(()=>{locks++;return accepted;}));
            Check(sender.Deserialize(Newtonsoft.Json.JsonConvert.SerializeObject(config)), "Win+L settings accepted");
            Check(sender.Gestured(null) && locks==1, "Win+L uses lock API before sided modifier dispatch");
            accepted=false;
            Check(!sender.Gestured(null) && locks==2, "Lock API failure is reported");
        }
        Check(!ShortcutKeys.IsLockShortcut(10,null,new[]{76}), "Ctrl+Win+L is not lock");
        Check(!ShortcutKeys.IsLockShortcut(8,new[]{160},new[]{76}), "Shift+Win+L is not lock");
        Check(!ShortcutKeys.IsLockShortcut(8,null,new[]{76,65}), "Multi-key command is not lock");
        Check(!ShortcutKeys.IsLockShortcut(0,null,new[]{76}), "L alone is not lock");
        Check(!ShortcutKeys.IsLockShortcut(8,null,new[]{91}), "Standalone Win is not lock");
        Check(!ShortcutKeys.IsLockShortcut(8,null,new[]{65}), "Win+A is not lock");
        foreach (int code in new[]{37,38,39,40})
        foreach (bool modified in new[]{false,true})
        {
            events.Clear();
            var sim = new WindowsInput.InputSimulator();
            if (modified) sim.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.LCONTROL,(WindowsInput.Native.VirtualKeyCode)code);
            else sim.Keyboard.KeyPress(new[]{(WindowsInput.Native.VirtualKeyCode)code});
            limit=DateTime.UtcNow.AddMilliseconds(120);
            while(DateTime.UtcNow<limit){Application.DoEvents();Thread.Sleep(5);}
            var arrow = events.Where(e=>e.key==code&&(e.flags&16)!=0).ToArray();
            Check(arrow.Length==2 && arrow[0].msg==0x100 && arrow[1].msg==0x101,"Arrow down/up once");
            Check(arrow.All(e=>(e.flags&1)!=0),"Arrow extended flags");
            int scan = code==37?0x4b:code==38?0x48:code==39?0x4d:0x50;
            Check(arrow.All(e=>e.scan==scan),"Arrow hardware scan code");
            Check(unchecked((uint)(arrow[1].time-arrow[0].time))>=25,"Arrow held before release, not after release");
            if(modified) { var sent=events.Where(e=>(e.flags&16)!=0).ToArray(); Check(sent[0].key==162&&sent[0].msg==0x100&&sent.Last().key==162&&sent.Last().msg==0x101,"Modifier surrounds arrow hold: "+string.Join(";",sent.Where(e=>e.key==162||e.key==code).Select(e=>$"{e.key}/{e.msg:X}/{e.flags:X}"))); }
        }
        Console.WriteLine($"PASS {checks} shortcut checks, including native LCtrl/RCtrl/LWin/RWin sends through normal and fallback plugins.");
    }
}
