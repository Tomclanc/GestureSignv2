using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GestureSign.Common.Configuration
{
    public static class FileManager
    {
        #region Constructors

        static FileManager()
        {
        }

        #endregion

        #region Public Methods

        public static bool SaveObject(object serializableObject, string filePath, bool typeName = false, bool throwException = false)
        {
            try
            {
                string backup = null;
                if (File.Exists(filePath))
                {
                    backup = BackupFile(filePath);
                    WaitFile(filePath);
                }

                // Open json file
                using (StreamWriter sWrite = new StreamWriter(filePath))
                {
                    JsonSerializer serializer = new JsonSerializer
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        DefaultValueHandling = DefaultValueHandling.Ignore
                    };
                    if (typeName)
                    {
                        serializer.TypeNameHandling = TypeNameHandling.Objects;
                        serializer.TypeNameAssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;
                    }
                    serializer.Serialize(sWrite, serializableObject);
                }
                //  File.WriteAllText(filePath, JsonConvert.SerializeObject(SerializableObject));

                if (File.Exists(backup))
                    File.Delete(backup);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogAndNotice(new Exceptions.FileWriteException(ex));
                if (throwException)
                    throw;
                return false;
            }
        }

        public static T LoadObject<T>(string filePath, bool backup, bool typeName = false, bool throwException = false)
        {
            try
            {
                if (!File.Exists(filePath)) return default(T);

                WaitFile(filePath);

                string json = NormalizeLegacyHotkeys(File.ReadAllText(filePath));
                return JsonConvert.DeserializeObject<T>(json, typeName
                    ? new JsonSerializerSettings()
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        Converters = new List<JsonConverter>() { new ActionConverter(), new CommandConverter() }
                    }
                    : new JsonSerializerSettings());
            }
            catch (Exception e)
            {
                Logging.LogAndNotice(e);
                if (backup)
                    BackupFile(filePath);
                if (throwException)
                    throw;
                return default(T);
            }
        }

        public static void WaitFile(string filePath)
        {
            int count = 0;
            while (IsFileLocked(filePath) && count != 10)
            {
                count++;
                Thread.Sleep(50);
            }
        }

        private static string NormalizeLegacyHotkeys(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.IndexOf("\"Hotkey\"", StringComparison.OrdinalIgnoreCase) < 0)
                return json;

            try
            {
                JToken root = JToken.Parse(json);
                NormalizeHotkeyTokens(root);
                return root.ToString(Formatting.None);
            }
            catch
            {
                return json;
            }
        }

        private static void NormalizeHotkeyTokens(JToken token)
        {
            if (token is JObject obj)
            {
                if (obj["Hotkey"] is JObject hotkey)
                {
                    if (hotkey["KeyCode"] is JArray keys && keys.Count > 0)
                        hotkey["KeyCode"] = keys[0];

                    if (hotkey["ModifierKeys"] == null)
                    {
                        int modifiers = 0;
                        if (hotkey.Value<bool?>("Alt") == true)
                            modifiers |= 1;
                        if (hotkey.Value<bool?>("Control") == true)
                            modifiers |= 2;
                        if (hotkey.Value<bool?>("Shift") == true)
                            modifiers |= 4;
                        if (hotkey.Value<bool?>("Windows") == true)
                            modifiers |= 8;
                        if (modifiers != 0)
                            hotkey["ModifierKeys"] = modifiers;
                    }
                }

                foreach (JProperty property in obj.Properties())
                    NormalizeHotkeyTokens(property.Value);
                return;
            }

            if (token is JArray array)
            {
                foreach (JToken item in array)
                    NormalizeHotkeyTokens(item);
            }
        }

        private static string BackupFile(string filePath)
        {
            try
            {
                var backupDirectory = new DirectoryInfo(AppConfig.BackupPath);
                if (!backupDirectory.Exists)
                    backupDirectory.Create();
                string backupFileName = Path.Combine(backupDirectory.FullName, DateTime.Now.ToString("yyMMddHHmmss") + Path.GetExtension(filePath));
                File.Copy(filePath, backupFileName, false);
                return backupFileName;
            }
            catch (Exception e)
            {
                Logging.LogException(e);
                return null;
            }
        }

        private static bool IsFileLocked(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException exception)
            {
                var errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(exception) & 65535;
                return errorCode == 32 || errorCode == 33;
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion
    }
}
