using System;
using Microsoft.Win32;

namespace SilverWolfLauncher.Services
{
    public class LanguageService
    {
        // Honkai Star Rail usually stores language settings in the registry:
        // HKEY_CURRENT_USER\Software\Cognosphere\Star Rail
        // Key: LanguageSettings_h...
        // However, Firefly Launcher patches the game files directly (ExcelLanguage).
        // Modifying Unity Asset bundles directly in C# requires a library like AssetsTools.NET.
        // We will implement a simplified Registry-based switch first, 
        // which works for the official client and some private server clients.

        private const string RegistryKeyPath = @"Software\Cognosphere\Star Rail";

        public enum GameLanguage
        {
            EN, CN, JP, KR
        }

        public bool SetGameLanguage(GameLanguage textLanguage, GameLanguage voiceLanguage)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                    {
                        // Registry key doesn't exist, which might mean the game hasn't been launched yet or it's a beta client with a different path.
                        // Let's create a mockup or try an alternative path if needed.
                        return false;
                    }

                    string textLangCode = GetLangCode(textLanguage);
                    string voiceLangCode = GetLangCode(voiceLanguage);

                    // Typical JSON structure for HSR language settings:
                    // {"TextLanguage":"en","VoiceLanguage":"en"}
                    string jsonSettings = $"{{\"TextLanguage\":\"{textLangCode}\",\"VoiceLanguage\":\"{voiceLangCode}\"}}";

                    // Find the LanguageSettings key
                    foreach (string valueName in key.GetValueNames())
                    {
                        if (valueName.StartsWith("LanguageSettings_h"))
                        {
                            // It's stored as a byte array (UTF-8 string with null terminator)
                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonSettings);
                            byte[] valueToSave = new byte[bytes.Length + 1];
                            Array.Copy(bytes, valueToSave, bytes.Length);
                            valueToSave[bytes.Length] = 0; // Null terminator

                            key.SetValue(valueName, valueToSave, RegistryValueKind.Binary);
                            return true;
                        }
                    }

                    // If not found, create it (mocking the hash part)
                    byte[] newBytes = System.Text.Encoding.UTF8.GetBytes(jsonSettings);
                    byte[] newValue = new byte[newBytes.Length + 1];
                    Array.Copy(newBytes, newValue, newBytes.Length);
                    newValue[newBytes.Length] = 0;
                    key.SetValue("LanguageSettings_h12345678", newValue, RegistryValueKind.Binary);

                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string GetLangCode(GameLanguage lang)
        {
            switch (lang)
            {
                case GameLanguage.EN: return "en";
                case GameLanguage.CN: return "zh-cn";
                case GameLanguage.JP: return "ja";
                case GameLanguage.KR: return "ko";
                default: return "en";
            }
        }
    }
}
