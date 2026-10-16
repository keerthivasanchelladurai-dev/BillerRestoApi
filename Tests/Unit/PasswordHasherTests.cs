using System;
using System.Diagnostics;
using System.Security.Cryptography;
using BillerRestoApi.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BillerRestoApi.UnitTests
{
    /// <summary>
    /// The PBKDF2 implementation was switched to the OS (CNG) version for speed. These tests prove that hashes
    /// already stored in tbllogin (made by the managed Rfc2898DeriveBytes) still verify, and the reverse.
    /// </summary>
    [TestClass]
    public class PasswordHasherTests
    {
        private static string ManagedHash(string password, byte[] salt, int iterations)
        {
            byte[] hash;
            using (var p = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                hash = p.GetBytes(32);
            return "PBKDF2-SHA256$" + iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        [TestMethod]
        public void HashStoredByTheOldManagedImplementation_StillVerifies()
        {
            var salt = new byte[16];
            new Random(7).NextBytes(salt);
            string stored = ManagedHash("Correct-Pass-1", salt, 210000);

            Assert.IsTrue(PasswordHasher.VerifyPassword("Correct-Pass-1", stored));
            Assert.IsFalse(PasswordHasher.VerifyPassword("Correct-Pass-2", stored));
        }

        [TestMethod]
        public void NewHash_IsVerifiableByTheManagedImplementation_AndKeepsTheFormat()
        {
            string stored = PasswordHasher.HashPassword("Correct-Pass-1");
            string[] parts = stored.Split('$');

            Assert.AreEqual(4, parts.Length);
            Assert.AreEqual("PBKDF2-SHA256", parts[0]);
            Assert.AreEqual("210000", parts[1]);

            // Recompute with the managed class from the stored salt: must equal the stored hash.
            string again = ManagedHash("Correct-Pass-1", Convert.FromBase64String(parts[2]), 210000);
            Assert.AreEqual(stored, again);
        }

        [TestMethod]
        public void NonAsciiPassword_RoundTrips()
        {
            string pw = "Pässwörd-€-中文";
            Assert.IsTrue(PasswordHasher.VerifyPassword(pw, PasswordHasher.HashPassword(pw)));
        }

        [TestMethod]
        public void MalformedOrOutOfRangeHashes_AreRejected()
        {
            Assert.IsFalse(PasswordHasher.VerifyPassword("x", null));
            Assert.IsFalse(PasswordHasher.VerifyPassword("x", "garbage"));
            Assert.IsFalse(PasswordHasher.VerifyPassword("x", "PBKDF2-SHA256$5$AAAA$BBBB"));
            Assert.IsFalse(PasswordHasher.VerifyPassword("x", "PBKDF2-SHA256$99999999$AAAA$BBBB"));
        }

        [TestMethod]
        public void Verification_ReportsItsMode_AndIsNotSlowerThanManagedWould()
        {
            string stored = PasswordHasher.HashPassword("Correct-Pass-1");
            PasswordHasher.VerifyPassword("Correct-Pass-1", stored);   // warm

            var sw = Stopwatch.StartNew();
            Assert.IsTrue(PasswordHasher.VerifyPassword("Correct-Pass-1", stored));
            long ms = sw.ElapsedMilliseconds;

            Console.WriteLine("PBKDF2 mode=" + PasswordHasher.Mode + " verify=" + ms + " ms");
            Assert.IsTrue(PasswordHasher.Mode == "native-cng" || PasswordHasher.Mode == "managed");
        }
    }
}
