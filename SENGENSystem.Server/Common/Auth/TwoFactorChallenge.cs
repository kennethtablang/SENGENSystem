using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>
    /// Shared mechanics for the emailed two-factor one-time code (FR-AUTH). The raw code and the
    /// raw challenge travel only to the user (code by email, challenge in the login response);
    /// the database keeps only their SHA-256 hashes, so a leaked row can't be replayed. Used by
    /// both the login challenge and the Profile enable-confirmation flow.
    /// </summary>
    public static class TwoFactorChallenge
    {
        /// <summary>Minutes a freshly issued code stays valid.</summary>
        public const int CodeMinutes = 10;

        /// <summary>Wrong guesses allowed against one code before the challenge is voided.</summary>
        public const int MaxAttempts = 5;

        public readonly record struct Issued(string Token, string Code);

        /// <summary>
        /// Stamps a new code and login challenge onto <paramref name="user"/> (caller saves). Returns
        /// the raw token to hand back in the login response and the raw code to email.
        /// </summary>
        public static Issued Issue(User user)
        {
            var token = OneTimeToken.Generate();
            var code = OneTimeToken.GenerateNumericCode();
            user.TwoFactorChallengeHash = OneTimeToken.Hash(token);
            user.TwoFactorCodeHash = OneTimeToken.Hash(code);
            user.TwoFactorCodeExpiresUtc = DateTime.UtcNow.AddMinutes(CodeMinutes);
            user.TwoFactorAttempts = 0;
            return new Issued(token, code);
        }

        /// <summary>
        /// Stamps a new code with no login challenge — the authenticated enable-confirmation flow,
        /// where the caller is already signed in. Returns the raw code to email.
        /// </summary>
        public static string IssueCodeOnly(User user)
        {
            var code = OneTimeToken.GenerateNumericCode();
            user.TwoFactorChallengeHash = null;
            user.TwoFactorCodeHash = OneTimeToken.Hash(code);
            user.TwoFactorCodeExpiresUtc = DateTime.UtcNow.AddMinutes(CodeMinutes);
            user.TwoFactorAttempts = 0;
            return code;
        }

        /// <summary>
        /// Regenerates just the code (keeping the existing login challenge) so a resend refreshes the
        /// emailed code without asking for the password again. Returns the raw code to email.
        /// </summary>
        public static string RefreshCode(User user)
        {
            var code = OneTimeToken.GenerateNumericCode();
            user.TwoFactorCodeHash = OneTimeToken.Hash(code);
            user.TwoFactorCodeExpiresUtc = DateTime.UtcNow.AddMinutes(CodeMinutes);
            user.TwoFactorAttempts = 0;
            return code;
        }

        /// <summary>Clears every pending-code field once a challenge is consumed, voided, or cancelled.</summary>
        public static void Clear(User user)
        {
            user.TwoFactorChallengeHash = null;
            user.TwoFactorCodeHash = null;
            user.TwoFactorCodeExpiresUtc = null;
            user.TwoFactorAttempts = 0;
        }

        /// <summary>
        /// Whether <paramref name="code"/> matches the user's live, unexpired code within the attempt
        /// cap. On a wrong-but-live code, increments the attempt counter (caller saves).
        /// </summary>
        public static VerifyResult VerifyCode(User user, string? code)
        {
            if (user.TwoFactorCodeHash is null || user.TwoFactorCodeExpiresUtc is not { } expiry
                || expiry <= DateTime.UtcNow)
            {
                return VerifyResult.Expired;
            }
            if (user.TwoFactorAttempts >= MaxAttempts)
            {
                return VerifyResult.TooManyAttempts;
            }
            if (string.IsNullOrWhiteSpace(code) || OneTimeToken.Hash(code.Trim()) != user.TwoFactorCodeHash)
            {
                user.TwoFactorAttempts++;
                return user.TwoFactorAttempts >= MaxAttempts ? VerifyResult.TooManyAttempts : VerifyResult.WrongCode;
            }
            return VerifyResult.Ok;
        }

        public enum VerifyResult { Ok, WrongCode, Expired, TooManyAttempts }
    }
}
