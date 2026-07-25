namespace SENGENSystem.Server.Domain
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.Student;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Set when the account was created for the student from their SIS with a system-generated
        /// temporary password: the first successful sign-in is forced through a password change
        /// before anything else is allowed. Cleared once the student sets their own password.
        /// </summary>
        public bool MustChangePassword { get; set; }

        // FR-AUTH-02: terms-and-conditions acknowledgment must be persisted with its timestamp.
        public DateTime? TermsAcceptedAtUtc { get; set; }

        // Self-service password reset: only the SHA-256 hash of the emailed one-time token is
        // stored, so a database leak cannot be replayed as a reset link.
        public string? PasswordResetTokenHash { get; set; }

        public DateTime? PasswordResetExpiresUtc { get; set; }

        // Pending email change: the new address takes effect only after the confirmation link
        // sent to it is used (proves the user controls the mailbox).
        public string? PendingEmail { get; set; }

        public string? EmailChangeTokenHash { get; set; }

        public DateTime? EmailChangeExpiresUtc { get; set; }

        // ---- Two-factor authentication (opt-in email one-time code) ----

        /// <summary>When true, sign-in requires a 6-digit code emailed to <see cref="Email"/>.</summary>
        public bool TwoFactorEnabled { get; set; }

        /// <summary>
        /// SHA-256 hash of the current one-time code — never the code itself, so a leaked row cannot
        /// be replayed. Shared by the login challenge and the enable-confirmation flow (mutually
        /// exclusive states).
        /// </summary>
        public string? TwoFactorCodeHash { get; set; }

        /// <summary>
        /// SHA-256 hash of the opaque challenge handed back at login. Binds the follow-up verify
        /// call to that specific password step, so a code alone (or an email guess) can't complete it.
        /// Null for the authenticated enable-confirmation flow, which needs no challenge.
        /// </summary>
        public string? TwoFactorChallengeHash { get; set; }

        public DateTime? TwoFactorCodeExpiresUtc { get; set; }

        /// <summary>Wrong-code attempts against the current code; the challenge is voided past the cap.</summary>
        public int TwoFactorAttempts { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public string FullName => $"{FirstName} {LastName}";
    }
}
