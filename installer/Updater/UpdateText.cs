using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace GestureSign.Updater
{
    internal static class UpdateText
    {
        internal const string CatalogResourceName = "UI.Catalogs.zip";
        private static Dictionary<string, string> _strings = new Dictionary<string, string>();
        internal static void SetCulture(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture)) culture = CultureInfo.CurrentUICulture.Name;
            var assembly = Assembly.GetExecutingAssembly();
            using (var resource = assembly.GetManifestResourceStream(CatalogResourceName))
            {
                if (resource != null)
                {
                    using (var catalog = new ZipArchive(resource, ZipArchiveMode.Read))
                    using (var stream = (catalog.GetEntry(culture + ".json") ?? catalog.GetEntry("en-US.json"))?.Open())
                    {
                        if (stream != null)
                            _strings = (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
                    }
                }
            }
            try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); }
            catch (CultureNotFoundException) { }
        }
        internal static string T(string key)
            => _strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : key;
    }
}
