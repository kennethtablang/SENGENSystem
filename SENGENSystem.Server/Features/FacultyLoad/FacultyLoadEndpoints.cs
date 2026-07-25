using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.FacultyLoad
{
    // Vertical slice: the Academic Head (Dean) allocates subjects to faculty members for a semester,
    // per class section (student block) (FR-FAC-01). Total assigned units are validated against each
    // member's ceiling (FR-FAC-03).
    public record LoadItem(Guid SubjectId, Guid ClassSectionId);
    public record SaveLoadRequest(Guid? SemesterId, List<LoadItem>? Items);

    public static class FacultyLoadEndpoints
    {
        public static IEndpointRouteBuilder MapFacultyLoad(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/faculty-load")
                .RequireAuthorization(policy =>
                    policy.RequireRole(nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapGet("/{facultyProfileId:guid}/subjects", GetSubjectsAsync);
            group.MapPut("/{facultyProfileId:guid}", SaveAsync);
            return app;
        }

        // GET /api/faculty-load?semesterId= — faculty members with their load for the semester,
        // plus the list of semesters to choose from (the Academic Head can't read the SchoolAdmin
        // semesters endpoint, so the selector is sourced here).
        private static async Task<IResult> ListAsync(Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semesters = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { id = s.Id, name = s.Name, isActive = s.IsActive })
                .ToListAsync(ct);

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null)
            {
                return Results.Ok(new { semesterId = (Guid?)null, semesterName = (string?)null, semesters, faculty = Array.Empty<FacultyLoadDto>() });
            }

            var faculty = await db.FacultyProfiles
                .AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct);

            // Assigned (SubjectId, Units) per faculty for this semester, in one round trip.
            var loads = await db.FacultyLoadAssignments
                .Where(l => l.SemesterId == semester.Id)
                .Join(db.Subjects, l => l.SubjectId, s => s.Id, (l, s) => new { l.FacultyProfileId, s.Units })
                .ToListAsync(ct);

            var byFaculty = loads
                .GroupBy(x => x.FacultyProfileId)
                .ToDictionary(g => g.Key, g => (Units: g.Sum(x => x.Units), Count: g.Count()));

            var rows = faculty.Select(f =>
            {
                byFaculty.TryGetValue(f.Id, out var agg);
                return new FacultyLoadDto(
                    f.Id,
                    f.User?.FullName ?? "(unknown)",
                    f.ProgramCode,
                    f.MaxLoadUnits,
                    agg.Units,
                    agg.Count);
            }).ToList();

            return Results.Ok(new { semesterId = semester.Id, semesterName = semester.Name, semesters, faculty = rows });
        }

        // GET /api/faculty-load/{id}/subjects?semesterId= — modal data for one faculty member: every
        // assignable (subject × class section) row for the semester, with who currently holds it.
        private static async Task<IResult> GetSubjectsAsync(
            Guid facultyProfileId, Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var faculty = await db.FacultyProfiles.AsNoTracking().Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null) return Results.BadRequest(new { message = "No semester is available. Create and activate a semester first." });

            // Who holds each (subject, class section) this semester — a pair is exclusive to one
            // faculty. Used to mark rows taken by other members so they can't be double-assigned.
            var holders = (await db.FacultyLoadAssignments
                .Where(l => l.SemesterId == semester.Id)
                .Select(l => new
                {
                    l.SubjectId,
                    l.ClassSectionId,
                    l.FacultyProfileId,
                    l.FacultyProfile!.User!.FirstName,
                    l.FacultyProfile.User.LastName
                })
                .ToListAsync(ct))
                .ToDictionary(
                    x => (x.SubjectId, x.ClassSectionId),
                    x => (x.FacultyProfileId, Name: $"{x.FirstName} {x.LastName}"));

            var classSections = await db.ClassSections.AsNoTracking()
                .Where(c => c.SemesterId == semester.Id)
                .OrderBy(c => c.ProgramCode).ThenBy(c => c.YearLevel).ThenBy(c => c.SectionName)
                .ToListAsync(ct);

            // All subjects (archived included) — the per-section scoping below decides which apply.
            var subjects = await db.Subjects.AsNoTracking()
                .OrderBy(s => s.ProgramCode).ThenBy(s => s.YearLevel).ThenBy(s => s.Code)
                .ToListAsync(ct);

            // Expand each class section into the subjects its cohort takes. Strict curriculum scoping
            // (FR-SCHED-04): a cohort is offered exactly its curriculum's subjects for its year —
            // archived subjects included, since a cohort still on a retired catalog needs them.
            // Class sections created before per-section curricula fall back to program + year, minus
            // archived subjects (the old behaviour).
            var rows = new List<LoadRowDto>();
            var assignedUnits = 0;
            foreach (var c in classSections)
            {
                var offered = c.CurriculumId is { } curriculumId
                    ? subjects.Where(s => s.CurriculumId == curriculumId && s.YearLevel == c.YearLevel)
                    : subjects.Where(s => !s.IsArchived && s.ProgramCode == c.ProgramCode && s.YearLevel == c.YearLevel);
                foreach (var s in offered)
                {
                    holders.TryGetValue((s.Id, c.Id), out var holder);
                    var isMine = holder.FacultyProfileId == facultyProfileId && holder.FacultyProfileId != Guid.Empty;
                    if (isMine) assignedUnits += s.Units;
                    rows.Add(LoadRowDto.From(
                        s, c, isMine,
                        holder.FacultyProfileId == Guid.Empty ? null : holder.FacultyProfileId,
                        holder.FacultyProfileId == Guid.Empty ? null : holder.Name));
                }
            }

            return Results.Ok(new FacultyAssignmentsDto(
                faculty.Id, faculty.User?.FullName ?? "(unknown)", faculty.ProgramCode, faculty.MaxLoadUnits,
                assignedUnits, rows));
        }

        // PUT /api/faculty-load/{id} — reconcile a faculty member's (subject × class section) assignments
        // for a semester.
        private static async Task<IResult> SaveAsync(
            Guid facultyProfileId, SaveLoadRequest request, AppDbContext db, AuditLog audit, Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster, CancellationToken ct)
        {
            var faculty = await db.FacultyProfiles.Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            var semester = await ResolveSemesterAsync(request.SemesterId, db, ct);
            if (semester is null) return Results.BadRequest(new { message = "No semester is available. Create and activate a semester first." });

            var requested = request.Items?
                .Where(i => i.SubjectId != Guid.Empty && i.ClassSectionId != Guid.Empty)
                .Distinct()
                .ToList() ?? new List<LoadItem>();

            // Validate the requested pairs against real subjects and this semester's class sections.
            var subjectUnits = await db.Subjects
                .Where(s => requested.Select(i => i.SubjectId).Contains(s.Id))
                .Select(s => new { s.Id, s.Units })
                .ToListAsync(ct);
            var sectionIds = (await db.ClassSections
                .Where(c => c.SemesterId == semester.Id)
                .Select(c => c.Id)
                .ToListAsync(ct)).ToHashSet();

            var unitsBySubject = subjectUnits.ToDictionary(x => x.Id, x => x.Units);
            var desired = requested
                .Where(i => unitsBySubject.ContainsKey(i.SubjectId) && sectionIds.Contains(i.ClassSectionId))
                .ToHashSet();

            var totalUnits = desired.Sum(i => unitsBySubject[i.SubjectId]);
            if (totalUnits > faculty.MaxLoadUnits)
            {
                return Results.BadRequest(new
                {
                    message = $"That load is {totalUnits} units, over {faculty.User?.FullName}'s {faculty.MaxLoadUnits}-unit ceiling. Remove some subjects."
                });
            }

            // A (subject, class section) pair is exclusive: reject any pair already held by someone else.
            var takenByOthers = await db.FacultyLoadAssignments
                .Where(l => l.SemesterId == semester.Id && l.FacultyProfileId != facultyProfileId)
                .Select(l => new { l.SubjectId, l.ClassSectionId })
                .ToListAsync(ct);
            var takenSet = takenByOthers.Select(x => new LoadItem(x.SubjectId, x.ClassSectionId)).ToHashSet();
            if (desired.Any(d => takenSet.Contains(d)))
            {
                return Results.BadRequest(new { message = "One or more of these classes are already assigned to another faculty member. Refresh and try again." });
            }

            var existing = await db.FacultyLoadAssignments
                .Where(l => l.FacultyProfileId == facultyProfileId && l.SemesterId == semester.Id)
                .ToListAsync(ct);
            var existingKeys = existing.Select(e => new LoadItem(e.SubjectId, e.ClassSectionId)).ToHashSet();

            var removals = existing.Where(e => !desired.Contains(new LoadItem(e.SubjectId, e.ClassSectionId))).ToList();
            var additions = desired.Where(d => !existingKeys.Contains(d)).ToList();
            foreach (var gone in removals)
            {
                db.FacultyLoadAssignments.Remove(gone);
            }
            foreach (var add in additions)
            {
                db.FacultyLoadAssignments.Add(new FacultyLoadAssignment
                {
                    FacultyProfileId = facultyProfileId,
                    SubjectId = add.SubjectId,
                    ClassSectionId = add.ClassSectionId,
                    SemesterId = semester.Id
                });
            }

            // Tell the faculty member their allocation changed (only when it actually did).
            if (removals.Count > 0 || additions.Count > 0)
            {
                notifier.Notify(faculty.UserId, NotificationKind.FacultyLoadUpdated,
                    "Your teaching load was updated",
                    $"You are now assigned {desired.Count} class(es) totaling {totalUnits} units for {semester.Name}.",
                    "/schedule");
            }

            audit.Record(AuditAction.FacultyLoadSaved,
                $"Set {faculty.User?.FullName}'s load for {semester.Name} to {desired.Count} classes ({totalUnits} units).",
                "FacultyProfile", faculty.Id.ToString());
            await db.SaveChangesAsync(ct);
            broadcaster.Announce("faculty-load");

            return Results.Ok(new FacultyLoadDto(
                faculty.Id, faculty.User?.FullName ?? "(unknown)", faculty.ProgramCode,
                faculty.MaxLoadUnits, totalUnits, desired.Count));
        }

        private static async Task<Semester?> ResolveSemesterAsync(Guid? semesterId, AppDbContext db, CancellationToken ct) =>
            semesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct)
                    ?? await db.Semesters.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
    }
}
