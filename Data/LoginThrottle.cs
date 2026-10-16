using System;
using System.Collections.Concurrent;

namespace BillerRestoApi.Data
{
    /// <summary>
    /// In-memory brute-force limiter for /api/auth/login (per username + client IP).
    /// State is per worker process and resets on app recycle; adequate for a single-server deployment.
    /// </summary>
    public static class LoginThrottle
    {
        private const int MaxFailures = 5;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        // A syntactically valid hash of a random value, used only to equalize timing for unknown users.
        // Computed once, on first use (or by WarmUp at application start). It used to be a static field initializer,
        // so the FIRST login after every app start/recycle paid for an extra full PBKDF2 hash inside the request.
        private static readonly Lazy<string> DummyHashValue = new Lazy<string>(
            () => BillerRestoApi.Security.PasswordHasher.HashPassword(Guid.NewGuid().ToString("N")),
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static string DummyHash { get { return DummyHashValue.Value; } }

        /// <summary>Forces the dummy hash (and the PBKDF2 code path) to be ready before the first real login.</summary>
        public static void WarmUp() { var unused = DummyHash; }

        private class Entry { public int Failures; public DateTime FirstFailure; }

        private static readonly ConcurrentDictionary<string, Entry> Entries =
            new ConcurrentDictionary<string, Entry>();

        public static string Key(string username, string ip)
        {
            return (username ?? string.Empty).ToLowerInvariant() + "|" + ip;
        }

        public static bool IsLocked(string key)
        {
            Entry e, removed;
            if (!Entries.TryGetValue(key, out e)) return false;
            lock (e)
            {
                if (DateTime.UtcNow - e.FirstFailure > Window)
                {
                    Entries.TryRemove(key, out removed);
                    return false;
                }
                return e.Failures >= MaxFailures;
            }
        }

        public static void RecordFailure(string key)
        {
            if (Entries.Count > 10000) Entries.Clear(); // bound memory under abuse
            var e = Entries.GetOrAdd(key, k => new Entry { FirstFailure = DateTime.UtcNow });
            lock (e)
            {
                if (DateTime.UtcNow - e.FirstFailure > Window)
                {
                    e.Failures = 0;
                    e.FirstFailure = DateTime.UtcNow;
                }
                e.Failures++;
            }
        }

        public static void Reset(string key)
        {
            Entry removed;
            Entries.TryRemove(key, out removed);
        }
    }
}
