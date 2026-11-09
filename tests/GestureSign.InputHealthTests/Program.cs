using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;

internal static class Program
{
    [DllImport("user32.dll", SetLastError=true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices,uint count,uint size);
    [STAThread] static void Main()
    {
        Environment.SetEnvironmentVariable("GESTURESIGN_STARTUP_TEST_DATA",Path.Combine(Path.GetTempPath(),"GestureSign-HealthTest-"+Guid.NewGuid().ToString("N")));
        Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Run()
    {
        AppConfig.RegisterTouchScreen = true;
        AppConfig.RegisterTouchPad = true;
        var window = new MessageWindow();
        try
        {
            foreach (var idleSource in new[] { Devices.TouchPad, Devices.TouchScreen })
            {
                window.ResetInputState();
                for (int report = 0; report < 100; report++)
                    if (window.TrySelectTouchSource(idleSource, 0))
                        throw new Exception("Idle report acquired source ownership");
                window.ReadInputHealth(out var idle);
                if ((string)idle["RawSource"] != "None" || (long)idle["FramesDelivered"] != 0)
                    throw new Exception("Idle reports changed capture or fabricated frames");
                var nextSource = idleSource == Devices.TouchPad ? Devices.TouchScreen : Devices.TouchPad;
                if (!window.TrySelectTouchSource(nextSource, 2))
                    throw new Exception("Idle report blocked the next input device");
                if (!window.TrySelectTouchSource(nextSource, 0))
                    throw new Exception("Active-source continuation was rejected");
                if (window.TrySelectTouchSource(idleSource, 1))
                    throw new Exception("Another device stole the active source");
            }
            window.ResetInputState();
            Console.WriteLine("PASS: idle touch reports cannot lock out another device; active-source continuations preserve ownership.");
            if(window.ReadInputHealth(out var initial) || !(bool)initial["RegistrationQuerySucceeded"]) throw new Exception("Initial registrations unhealthy");
            var remove=new RAWINPUTDEVICE {usUsagePage=13,usUsage=4,dwFlags=1,hwndTarget=IntPtr.Zero};
            if(!RegisterRawInputDevices(new[]{remove},1,(uint)Marshal.SizeOf<RAWINPUTDEVICE>())) throw new Exception("Could not simulate lost registration");
            if(!window.ReadInputHealth(out var lost)) throw new Exception("Lost registration not detected");
            if((long)lost["RawMessages"] != 0) throw new Exception("Health probe fabricated input events");
            window.UpdateRegistration();
            if(window.ReadInputHealth(out _)) throw new Exception("Registration not restored");
            for(int i=0;i<5;i++) if(window.ReadInputHealth(out _)) throw new Exception("Idle probe requested another repair");
            window.DestroyWindow();
            if(window.ReadInputHealth(out _)) throw new Exception("Destroyed window caused a repair loop");
            if(!window.CreateWindow() || window.ReadInputHealth(out _)) throw new Exception("Recreated window did not restore registrations");
            Console.WriteLine("PASS: real Windows registration query, simulated loss, repair, idle stability; no input hooks or gesture execution.");
        }
        finally { window.DestroyWindow(); }
    }
}
