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

        public DbSet<Semester> Semesters => Set<Semester>();

        public DbSet<Room> Rooms => Set<Room>();

        public DbSet<Subject> Subjects => Set<Subject>();

        public DbSet<FacultyProfile> FacultyProfiles => Set<FacultyProfile>();

        public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();

        public DbSet<Section> Sections => Set<Section>();

        public DbSet<ScheduleAssignment> ScheduleAssignments => Set<ScheduleAssignment>();

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
            });

            modelBuilder.Entity<Semester>(semester =>
            {
                semester.Property(s => s.Name).HasMaxLength(120).IsRequired();
            });

            modelBuilder.Entity<Room>(room =>
            {
                room.Property(r => r.Name).HasMaxLength(60).IsRequired();
            });

            modelBuilder.Entity<Subject>(subject =>
            {
                subject.Property(s => s.Code).HasMaxLength(20).IsRequired();
                subject.Property(s => s.Title).HasMaxLength(160).IsRequired();
                subject.Property(s => s.ProgramCode).HasMaxLength(20).IsRequired();
                subject.HasIndex(s => s.Code).IsUnique();
            });

            modelBuilder.Entity<FacultyProfile>(faculty =>
            {
                faculty.Property(f => f.ProgramCode).HasMaxLength(20).IsRequired();
                faculty.HasOne(f => f.User)
                    .WithMany()
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                faculty.HasIndex(f => f.UserId).IsUnique();
            });

            modelBuilder.Entity<Section>(section =>
            {
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
        }
    }
}
