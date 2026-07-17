using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();

        public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();

        public DbSet<Semester> Semesters => Set<Semester>();

        public DbSet<Building> Buildings => Set<Building>();

        public DbSet<Room> Rooms => Set<Room>();

        public DbSet<Curriculum> Curricula => Set<Curriculum>();

        public DbSet<CurriculumSchoolYear> CurriculumSchoolYears => Set<CurriculumSchoolYear>();

        public DbSet<Subject> Subjects => Set<Subject>();

        public DbSet<SubjectPrerequisite> SubjectPrerequisites => Set<SubjectPrerequisite>();

        public DbSet<FacultyProfile> FacultyProfiles => Set<FacultyProfile>();

        public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

        public DbSet<Section> Sections => Set<Section>();

        public DbSet<ClassSection> ClassSections => Set<ClassSection>();

        public DbSet<ScheduleAssignment> ScheduleAssignments => Set<ScheduleAssignment>();

        public DbSet<FacultyLoadAssignment> FacultyLoadAssignments => Set<FacultyLoadAssignment>();

        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

        public DbSet<StudentRegistration> StudentRegistrations => Set<StudentRegistration>();

        public DbSet<RegistrationDocument> RegistrationDocuments => Set<RegistrationDocument>();

        public DbSet<TermActivation> TermActivations => Set<TermActivation>();

        public DbSet<SlotRequest> SlotRequests => Set<SlotRequest>();

        public DbSet<FacultyTimePreference> FacultyTimePreferences => Set<FacultyTimePreference>();

        public DbSet<Notification> Notifications => Set<Notification>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(user =>
            {
                user.HasIndex(u => u.Email).IsUnique();
                user.Property(u => u.Email).HasMaxLength(256).IsRequired();
                user.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
                user.Property(u => u.LastName).HasMaxLength(100).IsRequired();
                user.Property(u => u.PasswordHash).IsRequired();
                user.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
                user.Property(u => u.PasswordResetTokenHash).HasMaxLength(88);
                user.Property(u => u.PendingEmail).HasMaxLength(256);
                user.Property(u => u.EmailChangeTokenHash).HasMaxLength(88);
            });

            modelBuilder.Entity<SchoolYear>(schoolYear =>
            {
                schoolYear.Property(y => y.Name).HasMaxLength(60).IsRequired();
            });

            modelBuilder.Entity<Semester>(semester =>
            {
                semester.Property(s => s.Name).HasMaxLength(120).IsRequired();
                semester.Property(s => s.Term).HasConversion<string>().HasMaxLength(20)
                    .HasDefaultValue(SemesterTerm.FirstSemester);
                // A semester is filed under a school year; keep the year even if a semester is removed,
                // and block deleting a year that still has semesters (enforced in the endpoint as a 409).
                semester.HasOne(s => s.SchoolYear)
                    .WithMany(y => y.Semesters)
                    .HasForeignKey(s => s.SchoolYearId)
                    .OnDelete(DeleteBehavior.Restrict);
                // At most one First and one Second semester per school year.
                semester.HasIndex(s => new { s.SchoolYearId, s.Term }).IsUnique();
            });

            modelBuilder.Entity<Building>(building =>
            {
                building.Property(b => b.Name).HasMaxLength(120).IsRequired();
                building.Property(b => b.Code).HasMaxLength(20);
            });

            modelBuilder.Entity<Room>(room =>
            {
                room.Property(r => r.Name).HasMaxLength(60).IsRequired();
                room.HasOne(r => r.Building)
                    .WithMany(b => b.Rooms)
                    .HasForeignKey(r => r.BuildingId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Curriculum>(curriculum =>
            {
                curriculum.Property(c => c.ProgramCode).HasMaxLength(20).IsRequired();
                curriculum.Property(c => c.ProgramName).HasMaxLength(160).IsRequired();
            });

            modelBuilder.Entity<CurriculumSchoolYear>(link =>
            {
                link.HasIndex(l => new { l.CurriculumId, l.SchoolYearId }).IsUnique();
                link.HasOne(l => l.Curriculum)
                    .WithMany(c => c.SchoolYears)
                    .HasForeignKey(l => l.CurriculumId)
                    .OnDelete(DeleteBehavior.Cascade);
                // School-year deletion is guarded in its endpoint (409 if referenced), so restrict here.
                link.HasOne(l => l.SchoolYear)
                    .WithMany()
                    .HasForeignKey(l => l.SchoolYearId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Subject>(subject =>
            {
                subject.Property(s => s.Code).HasMaxLength(20).IsRequired();
                subject.Property(s => s.Title).HasMaxLength(160).IsRequired();
                subject.Property(s => s.ProgramCode).HasMaxLength(20).IsRequired();
                // Existing rows (pre-term) default to First Semester.
                subject.Property(s => s.Term).HasConversion<string>().HasMaxLength(20)
                    .HasDefaultValue(SemesterTerm.FirstSemester);
                // A subject code is unique within its curriculum (the same code may recur across
                // curriculum versions), rather than globally.
                subject.HasIndex(s => new { s.CurriculumId, s.Code }).IsUnique();
                subject.HasOne(s => s.Curriculum)
                    .WithMany(c => c.Subjects)
                    .HasForeignKey(s => s.CurriculumId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SubjectPrerequisite>(prereq =>
            {
                prereq.HasIndex(p => new { p.SubjectId, p.PrerequisiteSubjectId }).IsUnique();
                prereq.HasOne(p => p.Subject)
                    .WithMany(s => s.Prerequisites)
                    .HasForeignKey(p => p.SubjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Only one cascade path is allowed; the "required by" side is cleaned up in the
                // delete endpoint before removing a subject.
                prereq.HasOne(p => p.PrerequisiteSubject)
                    .WithMany()
                    .HasForeignKey(p => p.PrerequisiteSubjectId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FacultyProfile>(faculty =>
            {
                faculty.Property(f => f.ProgramCode).HasMaxLength(20).IsRequired();
                faculty.Property(f => f.EmployeeId).HasMaxLength(20).HasDefaultValue(string.Empty);
                faculty.HasOne(f => f.User)
                    .WithMany()
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                faculty.HasIndex(f => f.UserId).IsUnique();
            });

            modelBuilder.Entity<Section>(section =>
            {
                // FR-ENL-03: the 40-slot cap is enforced at the database level — even a code
                // path that skips the application check cannot oversell a section.
                section.ToTable(t => t.HasCheckConstraint(
                    "CK_Sections_EnrolledCount",
                    "[EnrolledCount] >= 0 AND [EnrolledCount] <= [Capacity]"));
                section.Property(s => s.RowVersion).IsRowVersion();
                section.Property(s => s.SectionCode).HasMaxLength(60).IsRequired();
                section.Property(s => s.ProgramCode).HasMaxLength(20).IsRequired();
                section.Property(s => s.Block).HasMaxLength(10).IsRequired();
                section.Ignore(s => s.CohortKey);
                section.HasOne(s => s.Subject)
                    .WithMany()
                    .HasForeignKey(s => s.SubjectId)
                    .OnDelete(DeleteBehavior.Restrict);
                section.HasOne(s => s.Semester)
                    .WithMany()
                    .HasForeignKey(s => s.SemesterId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClassSection>(classSection =>
            {
                classSection.Property(c => c.ProgramCode).HasMaxLength(20).IsRequired();
                classSection.Property(c => c.SectionName).HasMaxLength(20).IsRequired();
                classSection.Ignore(c => c.DisplayName);
                // One class block per (semester, program, year, section) — classes are created per term.
                classSection.HasIndex(c => new { c.SemesterId, c.ProgramCode, c.YearLevel, c.SectionName }).IsUnique();
                // Semester deletion is guarded in its endpoint (409 if referenced), so restrict here.
                classSection.HasOne(c => c.Semester)
                    .WithMany()
                    .HasForeignKey(c => c.SemesterId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ScheduleAssignment>(assignment =>
            {
                assignment.HasOne(a => a.Section)
                    .WithMany()
                    .HasForeignKey(a => a.SectionId)
                    .OnDelete(DeleteBehavior.Cascade);
                assignment.HasOne(a => a.Room)
                    .WithMany()
                    .HasForeignKey(a => a.RoomId)
                    .OnDelete(DeleteBehavior.Restrict);
                assignment.HasOne(a => a.TimeSlot)
                    .WithMany()
                    .HasForeignKey(a => a.TimeSlotId)
                    .OnDelete(DeleteBehavior.Restrict);
                assignment.HasOne(a => a.FacultyProfile)
                    .WithMany()
                    .HasForeignKey(a => a.FacultyProfileId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FacultyLoadAssignment>(load =>
            {
                // A (subject, class section) pair is taught by at most one faculty member — this is
                // the exclusivity that mutes an already-assigned row in the Assign Load modal.
                load.HasIndex(l => new { l.SubjectId, l.ClassSectionId }).IsUnique();
                load.HasOne(l => l.FacultyProfile)
                    .WithMany()
                    .HasForeignKey(l => l.FacultyProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
                load.HasOne(l => l.Subject)
                    .WithMany()
                    .HasForeignKey(l => l.SubjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Class-section deletion is guarded in its endpoint (409 if referenced), so restrict here.
                load.HasOne(l => l.ClassSection)
                    .WithMany()
                    .HasForeignKey(l => l.ClassSectionId)
                    .OnDelete(DeleteBehavior.Restrict);
                // Semester deletion is guarded in its endpoint (409 if referenced), so restrict here.
                load.HasOne(l => l.Semester)
                    .WithMany()
                    .HasForeignKey(l => l.SemesterId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AuditEntry>(audit =>
            {
                audit.Property(a => a.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
                audit.Property(a => a.ActorName).HasMaxLength(201).IsRequired();
                audit.Property(a => a.ActorRole).HasMaxLength(30).IsRequired();
                audit.Property(a => a.Summary).HasMaxLength(500).IsRequired();
                audit.Property(a => a.EntityType).HasMaxLength(60);
                audit.Property(a => a.EntityId).HasMaxLength(60);
                audit.Property(a => a.IpAddress).HasMaxLength(45);
                // The trail is read newest-first and often filtered by action.
                audit.HasIndex(a => a.OccurredAtUtc);
                audit.HasIndex(a => a.Action);
            });

            modelBuilder.Entity<StudentRegistration>(reg =>
            {
                reg.Property(r => r.StudentNumber).HasMaxLength(20).IsRequired();
                reg.HasIndex(r => r.StudentNumber).IsUnique();
                reg.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                reg.Property(r => r.StudentType).HasConversion<string>().HasMaxLength(20).IsRequired();
                reg.Property(r => r.Program).HasConversion<string>().HasMaxLength(10).IsRequired();
                reg.Property(r => r.CivilStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
                reg.Property(r => r.Gender).HasConversion<string>().HasMaxLength(10).IsRequired();
                reg.Property(r => r.LastSchoolLevel).HasConversion<string>().HasMaxLength(30).IsRequired();
                reg.Property(r => r.YearGradeLastAttended).HasConversion<string>().HasMaxLength(20).IsRequired();
                reg.Property(r => r.LastTerm).HasConversion<string>().HasMaxLength(10).IsRequired();
                reg.Property(r => r.GuardianRelationship).HasConversion<string>().HasMaxLength(10).IsRequired();

                reg.Property(r => r.LastName).HasMaxLength(100).IsRequired();
                reg.Property(r => r.FirstName).HasMaxLength(100).IsRequired();
                reg.Property(r => r.MiddleName).HasMaxLength(100);
                reg.Property(r => r.Birthplace).HasMaxLength(160).IsRequired();
                reg.Property(r => r.Citizenship).HasMaxLength(60).IsRequired();
                reg.Property(r => r.Email).HasMaxLength(256).IsRequired();
                reg.Property(r => r.MobileNumber).HasMaxLength(20).IsRequired();
                reg.Property(r => r.AddressLine).HasMaxLength(200).IsRequired();
                reg.Property(r => r.Barangay).HasMaxLength(160).IsRequired();
                reg.Property(r => r.CityMunicipality).HasMaxLength(120).IsRequired();
                reg.Property(r => r.Province).HasMaxLength(120).IsRequired();
                reg.Property(r => r.ZipCode).HasMaxLength(10);
                reg.Property(r => r.SchoolName).HasMaxLength(200).IsRequired();
                reg.Property(r => r.SchoolProgram).HasMaxLength(200);
                reg.Property(r => r.SchoolYear).HasMaxLength(20);
                reg.Property(r => r.FatherName).HasMaxLength(160);
                reg.Property(r => r.FatherMobile).HasMaxLength(20);
                reg.Property(r => r.MotherName).HasMaxLength(160);
                reg.Property(r => r.MotherMobile).HasMaxLength(20);
                reg.Property(r => r.GuardianName).HasMaxLength(160).IsRequired();
                reg.Property(r => r.GuardianMobile).HasMaxLength(20).IsRequired();
                reg.Property(r => r.ReferredBy).HasMaxLength(160);
                reg.Ignore(r => r.FullName);

                reg.HasOne(r => r.Semester)
                    .WithMany()
                    .HasForeignKey(r => r.SemesterId)
                    .OnDelete(DeleteBehavior.Restrict);
                // A login account claims at most one SIS record; unlink (not delete) if the account goes.
                reg.HasIndex(r => r.UserId).IsUnique();
                reg.HasOne(r => r.User)
                    .WithMany()
                    .HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
                reg.HasMany(r => r.Documents)
                    .WithOne(d => d.StudentRegistration!)
                    .HasForeignKey(d => d.StudentRegistrationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RegistrationDocument>(doc =>
            {
                doc.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
                doc.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                doc.HasIndex(d => new { d.StudentRegistrationId, d.DocumentType }).IsUnique();
            });

            modelBuilder.Entity<FacultyTimePreference>(pref =>
            {
                pref.HasIndex(p => p.FacultyProfileId);
                pref.HasOne(p => p.FacultyProfile)
                    .WithMany()
                    .HasForeignKey(p => p.FacultyProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SlotRequest>(request =>
            {
                request.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                request.Property(r => r.RejectionReason).HasMaxLength(500);
                request.HasIndex(r => r.Status);
                // One live (requested/approved) seat request per student per section; rejected
                // or cancelled attempts do not block a retry.
                request.HasIndex(r => new { r.StudentRegistrationId, r.SectionId })
                    .IsUnique()
                    .HasFilter("[Status] IN ('Requested','Approved')");
                request.HasOne(r => r.StudentRegistration)
                    .WithMany()
                    .HasForeignKey(r => r.StudentRegistrationId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Sections referenced by enlistment records must not be silently deleted.
                request.HasOne(r => r.Section)
                    .WithMany()
                    .HasForeignKey(r => r.SectionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Notification>(notification =>
            {
                notification.Property(n => n.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
                notification.Property(n => n.Title).HasMaxLength(160).IsRequired();
                notification.Property(n => n.Body).HasMaxLength(500).IsRequired();
                notification.Property(n => n.LinkTo).HasMaxLength(200);
                // The bell reads one user's notices newest-first and counts the unread ones.
                notification.HasIndex(n => new { n.UserId, n.IsRead });
                notification.HasIndex(n => n.CreatedAtUtc);
                notification.HasOne(n => n.User)
                    .WithMany()
                    .HasForeignKey(n => n.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TermActivation>(act =>
            {
                act.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                act.Property(a => a.Remarks).HasMaxLength(500);
                act.HasIndex(a => a.Status);
                act.HasOne(a => a.StudentRegistration)
                    .WithMany()
                    .HasForeignKey(a => a.StudentRegistrationId)
                    .OnDelete(DeleteBehavior.Cascade);
                act.HasOne(a => a.Semester)
                    .WithMany()
                    .HasForeignKey(a => a.SemesterId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
