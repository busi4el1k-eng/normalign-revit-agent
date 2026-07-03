using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Stochează token-ul de desktop, criptat cu DPAPI (doar utilizatorul curent
    /// îl poate decripta, legat de contul Windows). Fișier:
    ///   %APPDATA%\NormalignRevitAgent\auth.dat
    ///
    /// Nimic hardcodat: token-ul se obține la runtime prin login-ul din browser.
    /// </summary>
    public static class AuthStore
    {
        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NormalignRevitAgent", "auth.dat");

        private static string? _cached;
        private static bool _loaded;

        public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

        public static string? Token
        {
            get
            {
                if (_loaded) return _cached;
                _loaded = true;
                try
                {
                    if (File.Exists(Path))
                    {
                        byte[] enc = File.ReadAllBytes(Path);
                        byte[] plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                        _cached = Encoding.UTF8.GetString(plain);
                    }
                }
                catch { _cached = null; }
                return _cached;
            }
        }

        public static void Save(string token)
        {
            _cached = token;
            _loaded = true;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                byte[] enc = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(Path, enc);
            }
            catch { /* dacă nu putem scrie, token-ul rămâne doar în memorie */ }
        }

        public static void Clear()
        {
            _cached = null;
            _loaded = true;
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
