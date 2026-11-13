using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace GestureSign.Updater
{
    internal static class UpdateText
    {
        private static Dictionary<string, string> _strings = new Dictionary<string, string>();
        internal static void SetCulture(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture)) culture = CultureInfo.CurrentUICulture.Name;
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("UI." + culture + ".json") ?? assembly.GetManifestResourceStream("UI.en-US.json"))
            {
                if (stream != null)
                    _strings = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
            }
            try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); }
            catch (CultureNotFoundException) { }
        }
        internal static string T(string key)
            => _strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : key;
    }
}
