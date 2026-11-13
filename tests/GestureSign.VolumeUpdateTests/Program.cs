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
        var resources = typeof(Checks).Assembly.GetManifestResourceNames();
        int cultures = 0;
        foreach (var resource in resources)
        {
            if (!resource.StartsWith("UI.") || !resource.EndsWith(".json")) continue;
            var culture = resource.Substring(3, resource.Length - 8);
            UpdateText.SetCulture(culture);
            var translated = UpdateText.T("Update GestureSign V2");
            if (string.IsNullOrWhiteSpace(translated) || (culture != "en-US" && culture != "en-GB" && translated == "Update GestureSign V2")) throw new Exception("Updater language fallback: " + culture);
            var status = string.Format(UpdateText.T("Windows Installer returned {0}. Detailed log: {1}"), 1603, "C:/test.log");
            if (!status.Contains("1603") || !status.Contains("C:/test.log")) throw new Exception("Lost update error placeholders: " + culture);
            cultures++;
        }
        if (cultures != 90) throw new Exception("Updater language resources missing: " + cultures);
        Console.WriteLine("PASS updater uses all 90 embedded languages and preserves error placeholders.");
        var options = UpdateOptions.Parse(new[] { "--mode", "portable", "--ready", @"C:\Temp Folder\ready", "--wait-pid", "123", "--from-temp" });
        if (options.ReadyPath != @"C:\Temp Folder\ready" || options.WaitProcessId != 123 || !options.FromTemp) throw new Exception("Update handoff arguments lost");
        Console.WriteLine($"PASS {checks} volume step/boundary checks and updater handoff options. No volume changed or update installed.");
    }
}
