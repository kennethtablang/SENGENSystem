namespace SENGENSystem.Server.Common.Validation
{
    /// <summary>
    /// The single password-strength rule used across account creation, self-service change, and
    /// admin reset (FR-AUTH-04/06): at least 8 characters, with both letters and digits.
    /// </summary>
    public static class PasswordPolicy
    {
        public const string Message = "Password must be at least 8 characters and contain both letters and digits.";

        public static bool IsValid(string? password) =>
            !string.IsNullOrEmpty(password)
            && password.Length >= 8
            && password.Any(char.IsLetter)
            && password.Any(char.IsDigit);
    }
}
