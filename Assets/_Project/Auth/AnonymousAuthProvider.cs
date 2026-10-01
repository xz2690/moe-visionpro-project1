using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using AuthService = Unity.Services.Authentication.AuthenticationService;

namespace MR.Auth
{
    /// <summary>
    /// Anonymous Unity Authentication plus a display name. No typing of credentials
    /// in a headset; the PlayerId is cached per device by Unity Authentication.
    /// </summary>
    public class AnonymousAuthProvider : IAuthProvider
    {
        public async Task<AuthResult> SignInAsync(string displayName)
        {
            try
            {
                await UnityServicesInitializer.EnsureInitializedAsync();

                var auth = AuthService.Instance;
                if (!auth.IsSignedIn)
                    await auth.SignInAnonymouslyAsync();

                string cleanName = SanitizeName(displayName);
                await TryUpdatePlayerNameAsync(cleanName);
                return AuthResult.Ok(auth.PlayerId, cleanName);
            }
            catch (AuthenticationException ex)
            {
                return AuthResult.Fail($"Sign-in failed: {ex.Message}");
            }
            catch (RequestFailedException ex)
            {
                return AuthResult.Fail($"Request failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return AuthResult.Fail($"Error: {ex.Message}");
            }
        }

        public void SignOut()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized &&
                AuthService.Instance.IsSignedIn)
                AuthService.Instance.SignOut();
        }

        // The cloud player name is only a convenience (dashboard / future friends list);
        // the name other players see travels in session player properties, so a failure
        // here must not block joining.
        private static async Task TryUpdatePlayerNameAsync(string name)
        {
            try
            {
                await AuthService.Instance.UpdatePlayerNameAsync(name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Auth] Could not update player name: {ex.Message}");
            }
        }

        /// <summary>Unity player names cannot contain whitespace and are limited to 50 characters.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Player";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
                if (sb.Length >= 20) break;
            }
            return sb.Length > 0 ? sb.ToString() : "Player";
        }
    }
}
