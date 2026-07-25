using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>What <see cref="StudentAccountProvisioner.EnsureAsync"/> did for a SIS record.</summary>
    /// <param name="User">The login account the registration is now linked to.</param>
    /// <param name="TemporaryPassword">
    /// The one-time password to email the student — set only when a new account was created; null
    /// when an account already existed (its password is never disclosed or overwritten).
    /// </param>
    /// <param name="Created">True when a brand-new account was provisioned this call.</param>
    public sealed record AccountProvisionResult(User User, string? TemporaryPassword, bool Created);

    /// <summary>
    /// Turns a SIS registration into a usable student login. Students never self-register an
    /// account: SEN-GEN provisions one from the details they already gave on the SIS, with a
    /// system-generated temporary password they must change on first sign-in
    /// (<see cref="User.MustChangePassword"/>). Idempotent — if an account already exists for the
    /// SIS email it is linked to the record, never overwritten. The caller persists the changes
    /// and emails the temporary password (only the mailbox owner receives it).
    /// </summary>
    public static class StudentAccountProvisioner
    {
        public static async Task<AccountProvisionResult> EnsureAsync(
            StudentRegistration registration,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            CancellationToken cancellationToken)
        {
            // The SIS stores the email in CAPS; login accounts are keyed on the lowercase form,
            // and every email comparison in the system is case-insensitive, so the two still match.
            var email = registration.Email.Trim().ToLowerInvariant();

            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (existing is not null)
            {
                // Link the record to the account if it isn't already; never touch its password.
                // A login claims at most one SIS record (UserId is uniquely indexed), so only link
                // when no other registration already owns this account — real submissions can't
                // share an email (rejected at the SIS), but seeded/legacy records may.
                if (registration.UserId is null)
                {
                    var claimedByAnother = await db.StudentRegistrations.AnyAsync(
                        r => r.UserId == existing.Id && r.Id != registration.Id, cancellationToken);
                    if (!claimedByAnother)
                    {
                        registration.UserId = existing.Id;
                    }
                }
                return new AccountProvisionResult(existing, null, Created: false);
            }

            var temporaryPassword = GenerateTemporaryPassword();
            var user = new User
            {
                FirstName = NameFormatter.ToProperCase(registration.FirstName),
                LastName = NameFormatter.ToProperCase(registration.LastName),
                Email = email,
                Role = UserRole.Student,
                IsActive = true,
                MustChangePassword = true,
                // Accepting the SIS terms is the same acknowledgment the account needs (FR-AUTH-02).
                TermsAcceptedAtUtc = registration.TermsAcceptedAtUtc
            };
            user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);

            db.Users.Add(user);
            registration.UserId = user.Id;

            return new AccountProvisionResult(user, temporaryPassword, Created: true);
        }

        /// <summary>
        /// A high-entropy one-time password that satisfies <see cref="PasswordPolicy"/> (letters
        /// and digits, ≥ 8 chars). 16 characters drawn uniformly (no modulo bias) from an
        /// unambiguous alphanumeric set — no 0/O/1/l/I — gives ~95 bits of entropy, so the password
        /// cannot be brute-forced in the window before the student sets their own on first sign-in.
        /// The rare draw lacking a letter or a digit is rejected and redrawn so the output is always
        /// policy-valid and always uniformly strong.
        /// </summary>
        private static string GenerateTemporaryPassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
            while (true)
            {
                var candidate = new string(RandomNumberGenerator.GetItems<char>(alphabet, 16));
                if (PasswordPolicy.IsValid(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
