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
        var events=new List<(int key,int flags,int msg)>();
        using var hook=new LowLevelKeyboardHook();
        hook.KeyIntercepted+=(int msg,int vk,int scan,int flags,int time,IntPtr extra,ref bool handled)=>events.Add((ShortcutKeys.Normalize(vk,scan,flags),flags,msg));
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
        Console.WriteLine($"PASS {checks} shortcut checks, including native right Ctrl sender and injected-event exclusion.");
    }
}
