using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace EndoMaster.Data
{
    public sealed class DbOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "sinutronic";
        public string Username { get; set; } = "sinutronic";
        /// <summary>Plain lub "Encrypted:BASE64" (DPAPI CurrentUser).</summary>
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// Ładowanie/zapisywanie konfiguracji DB (z szyfrowaniem hasła) + budowa CS.
    /// Priorytet: appsettings.json -> appsettings.Local.json -> ENV (opcjonalnie).
    /// Dodatkowy fallback: ConnectionStrings.Postgres (dla wstecznej zgodności).
    /// </summary>
    public static class AppConfig
    {
        private static DbOptions? _cached;
        public static readonly string BaseDir = AppContext.BaseDirectory;
        public static readonly string AppSettingsPath = Path.Combine(BaseDir, "appsettings.json");
        public static readonly string AppSettingsLocalPath = Path.Combine(BaseDir, "appsettings.Local.json");

        // ---- PUBLIC API -------------------------------------------------------

        public static DbOptions LoadDbOptions()
        {
            if (_cached is not null) return Clone(_cached);

            // 1) wczytaj appsettings.json (jeśli jest)
            var opt = new DbOptions();
            TryApplyFromFile(AppSettingsPath, ref opt);

            // 2) nadpisz appsettings.Local.json (jeśli jest)
            TryApplyFromFile(AppSettingsLocalPath, ref opt);

            // 3) ENV (opcjonalnie; nazwy proste, bez prefixów, żeby łatwo ustawić w instalatorze)
            ApplyFromEnv(ref opt);

            // 4) fallback: ConnectionStrings.Postgres (gdy ktoś ma stary plik)
            if (string.IsNullOrWhiteSpace(opt.Password) && TryReadLegacyCs(AppSettingsPath, out var legacyCs))
            {
                // wyciągnij pola z CS (tylko gdy brak nowej sekcji)
                TryParseConnString(legacyCs, ref opt);
            }

            // zapis do cache
            _cached = Clone(opt);
            return opt;
        }

        public static void SaveLocalEncrypted(DbOptions optWithPlainPassword)
        {
            var toSave = Clone(optWithPlainPassword);
            if (!string.IsNullOrEmpty(toSave.Password) && !toSave.Password.StartsWith("Encrypted:", StringComparison.Ordinal))
                toSave.Password = EncryptDpapi(toSave.Password);

            Directory.CreateDirectory(BaseDir);
            var json = new JsonObject
            {
                ["Database"] = new JsonObject
                {
                    ["Host"] = toSave.Host,
                    ["Port"] = toSave.Port,
                    ["Database"] = toSave.Database,
                    ["Username"] = toSave.Username,
                    ["Password"] = toSave.Password
                }
            };
            File.WriteAllText(AppSettingsLocalPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _cached = Clone(toSave); // odśwież cache
        }

        public static string BuildConnectionString(DbOptions opt, bool maskPasswordInResult = false)
        {
            var pass = DecryptIfNeeded(opt.Password);
            var cs =
                $"Host={opt.Host};Port={opt.Port};Database={opt.Database};Username={opt.Username};Password={pass};Pooling=true";
            if (!maskPasswordInResult) return cs;
            return cs.Replace($"Password={pass}", "Password=***", StringComparison.OrdinalIgnoreCase);
        }

        public static string DecryptIfNeeded(string maybeEnc)
        {
            if (string.IsNullOrWhiteSpace(maybeEnc)) return "";
            const string prefix = "Encrypted:";
            if (!maybeEnc.StartsWith(prefix, StringComparison.Ordinal)) return maybeEnc;
            var data = Convert.FromBase64String(maybeEnc[prefix.Length..]);
            var dec = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }

        public static string EncryptDpapi(string plain)
        {
            var data = Encoding.UTF8.GetBytes(plain ?? "");
            var enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return "Encrypted:" + Convert.ToBase64String(enc);
        }

        // ---- helpers ----------------------------------------------------------

        private static void TryApplyFromFile(string path, ref DbOptions o)
        {
            try
            {
                if (!File.Exists(path)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Database", out var db))
                {
                    if (db.TryGetProperty("Host", out var v)) o.Host = v.GetString() ?? o.Host;
                    if (db.TryGetProperty("Port", out v) && v.TryGetInt32(out var p)) o.Port = p;
                    if (db.TryGetProperty("Database", out v)) o.Database = v.GetString() ?? o.Database;
                    if (db.TryGetProperty("Username", out v)) o.Username = v.GetString() ?? o.Username;
                    if (db.TryGetProperty("Password", out v)) o.Password = v.GetString() ?? o.Password;
                }
            }
            catch { /* cicho */ }
        }

        private static void ApplyFromEnv(ref DbOptions o)
        {
            string? E(string name) => Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(E("SINU_DB_HOST"))) o.Host = E("SINU_DB_HOST")!;
            if (int.TryParse(E("SINU_DB_PORT"), out var port)) o.Port = port;
            if (!string.IsNullOrWhiteSpace(E("SINU_DB_NAME"))) o.Database = E("SINU_DB_NAME")!;
            if (!string.IsNullOrWhiteSpace(E("SINU_DB_USER"))) o.Username = E("SINU_DB_USER")!;
            if (!string.IsNullOrWhiteSpace(E("SINU_DB_PASS"))) o.Password = E("SINU_DB_PASS")!;
        }

        private static bool TryReadLegacyCs(string path, out string cs)
        {
            cs = "";
            try
            {
                if (!File.Exists(path)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var csNode) &&
                    csNode.TryGetProperty("Postgres", out var v))
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) { cs = s!; return true; }
                }
            }
            catch { }
            return false;
        }

        private static void TryParseConnString(string cs, ref DbOptions o)
        {
            if (string.IsNullOrWhiteSpace(cs)) return;
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant();
                var v = kv[1].Trim();
                switch (k)
                {
                    case "host": o.Host = v; break;
                    case "port": if (int.TryParse(v, out var p)) o.Port = p; break;
                    case "database": o.Database = v; break;
                    case "username": o.Username = v; break;
                    case "user id": o.Username = v; break;
                    case "password": o.Password = v; break;
                }
            }
        }

        private static DbOptions Clone(DbOptions o) => new()
        {
            Host = o.Host,
            Port = o.Port,
            Database = o.Database,
            Username = o.Username,
            Password = o.Password
        };
    }
}
