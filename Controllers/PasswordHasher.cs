
using System;
using System.Security.Cryptography;

namespace BillerRestoApi.Security
{
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 210000;

        // PBKDF2-HMAC-SHA256 dominates the cost of a login (210,000 iterations). .NET Framework's Rfc2898DeriveBytes
        // runs the HMAC loop in managed code: measured 513 ms per hash on an 8-core dev PC against 51 ms for the
        // operating system's CNG implementation (BCryptDeriveKeyPBKDF2) with byte-identical output. The algorithm,
        // iteration count, salt, stored format and constant-time comparison are unchanged, so existing hashes keep
        // verifying. If CNG is unavailable or disagrees with the managed result on a self-check, the managed
        // implementation is used (never a weaker one).
        private static int _nativeState;   // 0 = not checked yet, 1 = native verified, -1 = use managed

        /// <summary>"native-cng" or "managed": which PBKDF2 implementation is active (diagnostics only).</summary>
        public static string CurrentMode { get { return _nativeState > 0 ? "native-cng" : _nativeState < 0 ? "managed" : "unchecked"; } }

        public static string Mode { get { return Pbkdf2Native(new byte[16], "x", 1000) != null ? "native-cng" : "managed"; } }

        private static byte[] Pbkdf2(string password, byte[] salt, int iterations)
        {
            byte[] native = Pbkdf2Native(salt, password, iterations);
            if (native != null) return native;

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }

        private static byte[] Pbkdf2Native(byte[] salt, string password, int iterations)
        {
            if (_nativeState < 0) return null;

            try
            {
                if (_nativeState == 0)
                {
                    // One-time self-check against the managed implementation (cheap: 1,000 iterations).
                    byte[] probeSalt = new byte[16];
                    byte[] a = NativeDerive("selfcheck", probeSalt, 1000);
                    byte[] b;
                    using (var p = new Rfc2898DeriveBytes("selfcheck", probeSalt, 1000, HashAlgorithmName.SHA256))
                        b = p.GetBytes(HashSize);

                    bool same = a != null && a.Length == b.Length;
                    for (int i = 0; same && i < b.Length; i++) same = a[i] == b[i];
                    _nativeState = same ? 1 : -1;
                    if (!same) return null;
                }

                return NativeDerive(password, salt, iterations);
            }
            catch (DllNotFoundException) { _nativeState = -1; return null; }
            catch (EntryPointNotFoundException) { _nativeState = -1; return null; }
            catch (Exception) { _nativeState = -1; return null; }
        }

        private static byte[] NativeDerive(string password, byte[] salt, int iterations)
        {
            IntPtr alg;
            // BCRYPT_ALG_HANDLE_HMAC_FLAG (0x8): the provider is used as the HMAC PRF.
            if (BCryptOpenAlgorithmProvider(out alg, "SHA256", null, 0x8) != 0) return null;
            byte[] pw = System.Text.Encoding.UTF8.GetBytes(password);   // same encoding Rfc2898DeriveBytes uses
            try
            {
                byte[] key = new byte[HashSize];
                int status = BCryptDeriveKeyPBKDF2(alg, pw, pw.Length, salt, salt.Length, (ulong)iterations, key, key.Length, 0);
                return status == 0 ? key : null;
            }
            finally
            {
                Array.Clear(pw, 0, pw.Length);
                BCryptCloseAlgorithmProvider(alg, 0);
            }
        }

        [System.Runtime.InteropServices.DllImport("bcrypt.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr phAlgorithm, string pszAlgId, string pszImplementation, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("bcrypt.dll")]
        private static extern int BCryptDeriveKeyPBKDF2(IntPtr hPrf, byte[] pbPassword, int cbPassword, byte[] pbSalt, int cbSalt,
            ulong cIterations, byte[] pbDerivedKey, int cbDerivedKey, uint dwFlags);

        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password is required.");

            byte[] salt = new byte[SaltSize];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] hash = Pbkdf2(password, salt, Iterations);

            return "PBKDF2-SHA256$" + Iterations + "$" +
                   Convert.ToBase64String(salt) + "$" +
                   Convert.ToBase64String(hash);
        }

        public static bool VerifyPassword(
            string password,
            string storedValue)
        {
            if (string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(storedValue))
                return false;

            try
            {
                string[] parts = storedValue.Split('$');

                if (parts.Length != 4 ||
                    parts[0] != "PBKDF2-SHA256")
                    return false;

                int iterations = int.Parse(parts[1]);

                // Reject malformed or unexpectedly expensive parameters.
                if (iterations < 100000 || iterations > 1000000)
                    return false;

                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash =
                    Convert.FromBase64String(parts[3]);

                if (salt.Length != 16 || expectedHash.Length != 32)
                    return false;

                byte[] actualHash = Pbkdf2(password, salt, iterations);

                int difference = 0;

                for (int i = 0; i < expectedHash.Length; i++)
                {
                    difference |= expectedHash[i] ^ actualHash[i];
                }

                return difference == 0;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
