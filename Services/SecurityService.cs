using System;
using System.Security.Cryptography;
using System.Text;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    /// <summary>
    /// Service für Verschlüsselung sensibler Daten mittels Windows DPAPI.
    /// Die Verschlüsselung ist an den aktuellen Windows-Benutzer gebunden.
    /// </summary>
    public static class SecurityService
    {
        /// <summary>
        /// Verschlüsselt einen String mit DPAPI (CurrentUser Scope).
        /// </summary>
        /// <param name="plainText">Der zu verschlüsselnde Klartext.</param>
        /// <returns>Base64-kodierter verschlüsselter String, oder leerer String bei Fehler.</returns>
        public static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                Logger.Error("Encryption failed", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Entschlüsselt einen Base64-kodierten DPAPI-verschlüsselten String.
        /// </summary>
        /// <param name="encryptedText">Der verschlüsselte Base64-String.</param>
        /// <returns>Der entschlüsselte Klartext, oder leerer String bei Fehler.</returns>
        public static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                Logger.Error("Decryption failed", ex);
                return string.Empty;
            }
        }
    }
}
