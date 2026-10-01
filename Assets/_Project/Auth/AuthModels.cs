using System.Threading.Tasks;

namespace MR.Auth
{
    /// <summary>
    /// Pluggable sign-in backend. Kept from the Lobby-System prototype so platform
    /// accounts (Meta / Sign in with Apple) can be added later without touching callers.
    /// </summary>
    public interface IAuthProvider
    {
        Task<AuthResult> SignInAsync(string displayName);
        void SignOut();
    }

    public class AuthResult
    {
        public bool Success;
        public string PlayerId;
        public string DisplayName;
        public string ErrorMessage;

        public static AuthResult Ok(string playerId, string displayName) =>
            new() { Success = true, PlayerId = playerId, DisplayName = displayName };

        public static AuthResult Fail(string error) =>
            new() { Success = false, ErrorMessage = error };
    }
}
