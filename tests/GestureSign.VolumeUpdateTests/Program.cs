using System;
using GestureSign.CorePlugins.Volume;
using GestureSign.Updater;
internal static class Checks
{
    static void Main()
    {
        int checks = 0;
        for (int current = 0; current <= 100; current++)
            for (int step = -5; step <= 5; step++)
            {
                var actual = EndpointVolume.Next(current / 100f, step);
                if (Math.Abs(actual * 100 - Math.Clamp(current + step, 0, 100)) > .0001) throw new Exception("Inexact volume step");
                checks++;
            }
        var options = UpdateOptions.Parse(new[] { "--mode", "portable", "--ready", @"C:\Temp Folder\ready", "--wait-pid", "123", "--from-temp" });
        if (options.ReadyPath != @"C:\Temp Folder\ready" || options.WaitProcessId != 123 || !options.FromTemp) throw new Exception("Update handoff arguments lost");
        Console.WriteLine($"PASS {checks} volume step/boundary checks and updater handoff options. No volume changed or update installed.");
    }
}
