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

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public string FullName => $"{FirstName} {LastName}";
    }
}
