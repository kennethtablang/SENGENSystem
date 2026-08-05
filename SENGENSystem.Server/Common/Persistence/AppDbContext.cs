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

        public DbSet<AdmissionRequirement> AdmissionRequirements => Set<AdmissionRequirement>();

        public DbSet<AdmissionRequirementProgram> AdmissionRequirementPrograms => Set<AdmissionRequirementProgram>();

        public DbSet<TransfereeEvaluation> TransfereeEvaluations => Set<TransfereeEvaluation>();

        public DbSet<TransfereeEvaluationItem> TransfereeEvaluationItems => Set<TransfereeEvaluationItem>();

        public DbSet<TermActivation> TermActivations => Set<TermActivation>();

        public DbSet<SlotRequest> SlotRequests => Set<SlotRequest>();

        public DbSet<FacultyTimePreference> FacultyTimePreferences => Set<FacultyTimePreference>();

        public DbSet<Notification> Notifications => Set<Notification>();

        public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

        public DbSet<SurveyInvitation> SurveyInvitations => Set<SurveyInvitation>();

        public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

        public DbSet<SurveyCampaign> SurveyCampaigns => Set<SurveyCampaign>();

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
                user.Property(u => u.MustChangePassword).HasDefaultValue(false);
                user.Property(u => u.PasswordResetTokenHash).HasMaxLength(88);
                user.Property(u => u.PendingEmail).HasMaxLength(256);
                user.Property(u => u.EmailChangeTokenHash).HasMaxLength(88);
                user.Property(u => u.TwoFactorCodeHash).HasMaxLength(88);
                user.Property(u => u.TwoFactorChallengeHash).HasMaxLength(88);
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
                semester.Property(s => s.EnrollmentStage).HasConversion<string>().HasMaxLength(30)
                    .HasDefaultValue(EnrollmentStage.Preparation);
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
                // Stored as text so the schema reads plainly in ad-hoc SQL; rooms that predate
                // the kind default to a lecture room and the migration promotes the old labs.
                room.Property(r => r.Kind).HasConversion<string>().HasMaxLength(30)
                    .HasDefaultValue(RoomKind.LectureRoom);
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
                subject.Property(s => s.Delivery).HasConversion<string>().HasMaxLength(30)
                    .HasDefaultValue(SubjectDelivery.LectureOnly);
                subject.Property(s => s.LabRoomKind).HasConversion<string>().HasMaxLength(30);
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

            modelBuilder.Entity<SystemSettings>(settings =>
            {
                // One row, always. The CHECK keeps a bad cap out of the table even if a caller
                // bypasses the endpoint's validation; the lower bound of 1 stops a zero cap from
                // making every section unenlistable.
                settings.ToTable(t => t.HasCheckConstraint(
                    "CK_SystemSettings_Singleton",
                    $"[Id] = {SENGENSystem.Server.Domain.SystemSettings.SingletonId} AND [SectionCapacityCap] >= 1"));
                settings.Property(s => s.Id).ValueGeneratedNever();
                // Defaults mirror the engine's built-in SoftWeights so the singleton row that
                // already exists (and any created before these columns) behaves exactly as before.
                settings.Property(s => s.WeightPreference).HasDefaultValue(0.40);
                settings.Property(s => s.WeightIdleGap).HasDefaultValue(0.35);
                settings.Property(s => s.WeightRoomFit).HasDefaultValue(0.25);
                settings.Property(s => s.GapSaturationHours).HasDefaultValue(8.0);
                // Enrollment/enlistment + engine-budget parameters default to the previous behaviour.
                settings.Property(s => s.EnlistmentOpen).HasDefaultValue(true);
                settings.Property(s => s.TermActivationOpen).HasDefaultValue(true);
                settings.Property(s => s.MaxEnlistmentUnitsPerStudent).HasDefaultValue(0);
                settings.Property(s => s.MinSectionEnrollment).HasDefaultValue(15);
                settings.Property(s => s.ScheduleTimeBudgetSeconds).HasDefaultValue(20);
                settings.Property(s => s.ScheduleMaxStepsThousands).HasDefaultValue(2000);
            });

            modelBuilder.Entity<Section>(section =>
            {
                // FR-ENL-03: the seat cap is enforced at the database level — even a code
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
                // The cohort's curriculum version. Curricula are archived, never deleted, so restrict.
                classSection.HasIndex(c => c.CurriculumId);
                classSection.HasOne(c => c.Curriculum)
                    .WithMany()
                    .HasForeignKey(c => c.CurriculumId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TimeSlot>(timeSlot =>
            {
                // A slot is an admin-configured allowable grid entry unless a scheduler/board
                // placement created it (FR-SCHED-05). New rows default to allowable at the DB too.
                timeSlot.Property(t => t.IsAllowable).HasDefaultValue(true);
            });

            modelBuilder.Entity<SurveyInvitation>(inv =>
            {
                inv.Property(i => i.RecipientName).HasMaxLength(201).IsRequired();
                inv.Property(i => i.RecipientEmail).HasMaxLength(256).IsRequired();
                inv.Property(i => i.RecipientRole).HasMaxLength(30).IsRequired();
                inv.Property(i => i.TokenHash).HasMaxLength(88).IsRequired();
                inv.Property(i => i.Note).HasMaxLength(500);
                inv.Property(i => i.InvitedBy).HasMaxLength(201);
                inv.HasIndex(i => i.TokenHash);
                // The recipients page looks an invitation up by user to show live invite status.
                inv.HasIndex(i => i.UserId).IsUnique();
                // Recipient is a user; guarded elsewhere, so restrict on delete.
                inv.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SurveyResponse>(resp =>
            {
                resp.Property(r => r.RespondentName).HasMaxLength(201).IsRequired();
                resp.Property(r => r.RespondentEmail).HasMaxLength(256).IsRequired();
                resp.Property(r => r.RespondentRole).HasMaxLength(30).IsRequired();
                resp.Property(r => r.Position).HasMaxLength(120);
                resp.Property(r => r.Sex).HasMaxLength(20);
                resp.Property(r => r.Department).HasMaxLength(120);
                resp.Property(r => r.YearsUsing).HasMaxLength(40);
                resp.Property(r => r.Suggestions).HasMaxLength(4000);
                resp.Property(r => r.FurtherComments).HasMaxLength(4000);
                // One response per invitation (the emailed link answers exactly once).
                resp.HasOne(r => r.Invitation).WithOne(i => i!.Response)
                    .HasForeignKey<SurveyResponse>(r => r.InvitationId)
                    .OnDelete(DeleteBehavior.Cascade);
                resp.HasIndex(r => r.InvitationId).IsUnique();
            });

            modelBuilder.Entity<SurveyCampaign>(campaign =>
            {
                campaign.Property(c => c.LastChangedBy).HasMaxLength(201);
                // Exactly one collection window exists; seeding it here means the dashboard and the
                // submission gate always find a row without a first-run special case.
                campaign.HasData(new SurveyCampaign
                {
                    Id = SurveyCampaign.SingletonId,
                    IsOpen = true,
                    TargetResponses = 30,
                    OpenedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    LastChangedBy = "System"
                });
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
                // The official (external) student number is optional, but no two enrollees may
                // share one. A filtered unique index enforces that while allowing many NULLs.
                reg.Property(r => r.OfficialStudentNumber).HasMaxLength(30);
                reg.HasIndex(r => r.OfficialStudentNumber)
                    .IsUnique()
                    .HasFilter("[OfficialStudentNumber] IS NOT NULL");
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
                doc.Property(d => d.RequirementCode).HasMaxLength(40).IsRequired();
                doc.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                doc.HasIndex(d => new { d.StudentRegistrationId, d.RequirementCode }).IsUnique();
            });

            modelBuilder.Entity<AdmissionRequirement>(req =>
            {
                req.Property(r => r.Code).HasMaxLength(40).IsRequired();
                req.Property(r => r.Name).HasMaxLength(150).IsRequired();
                req.Property(r => r.Description).HasMaxLength(500);
                req.HasIndex(r => r.Code).IsUnique();
                req.HasMany(r => r.Programs)
                    .WithOne(p => p.AdmissionRequirement!)
                    .HasForeignKey(p => p.AdmissionRequirementId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AdmissionRequirementProgram>(rp =>
            {
                rp.Property(p => p.Program).HasConversion<string>().HasMaxLength(10).IsRequired();
                rp.HasIndex(p => new { p.AdmissionRequirementId, p.Program }).IsUnique();
            });

            modelBuilder.Entity<TransfereeEvaluation>(evaluation =>
            {
                evaluation.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                evaluation.Property(e => e.Remarks).HasMaxLength(1000);
                // One evaluation per registration — a re-opened evaluation revises this row rather
                // than starting a competing second sheet.
                evaluation.HasIndex(e => e.StudentRegistrationId).IsUnique();
                evaluation.HasOne(e => e.StudentRegistration)
                    .WithOne()
                    .HasForeignKey<TransfereeEvaluation>(e => e.StudentRegistrationId)
                    .OnDelete(DeleteBehavior.Cascade);
                // The curriculum is pinned for provenance; retiring one must not delete evaluations.
                evaluation.HasOne(e => e.Curriculum)
                    .WithMany()
                    .HasForeignKey(e => e.CurriculumId)
                    .OnDelete(DeleteBehavior.SetNull);
                evaluation.HasMany(e => e.Items)
                    .WithOne(i => i.TransfereeEvaluation!)
                    .HasForeignKey(i => i.TransfereeEvaluationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TransfereeEvaluationItem>(item =>
            {
                item.Property(i => i.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
                item.Property(i => i.SourceSubject).HasMaxLength(150);
                item.Property(i => i.SourceGrade).HasMaxLength(20);
                item.HasIndex(i => new { i.TransfereeEvaluationId, i.SubjectId }).IsUnique();
                // Subjects are archived, never deleted, so Restrict keeps a decision from being
                // orphaned by a catalog edit.
                item.HasOne(i => i.Subject)
                    .WithMany()
                    .HasForeignKey(i => i.SubjectId)
                    .OnDelete(DeleteBehavior.Restrict);
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
                request.Property(r => r.DropReason).HasMaxLength(500);
                request.HasIndex(r => r.Status);
                // One live (requested/approved) seat request per student per section; rejected,
                // cancelled, or dropped attempts do not block a retry — a student who gives a seat
                // back must be able to ask for it again.
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
