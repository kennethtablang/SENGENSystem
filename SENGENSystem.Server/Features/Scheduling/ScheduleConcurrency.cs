using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Features.Scheduling
{
    /// <summary>
    /// The one place a schedule write turns a lost race into an answer the caller can act on.
    ///
    /// <para>
    /// Four operations write <c>ScheduleAssignment</c> rows — generate, board edit, finalize, and
    /// publish — and before <c>ScheduleAssignment.RowVersion</c> existed they could interleave
    /// freely: a regenerate deletes the draft rows and re-inserts them while another request is
    /// reading or writing the same rows, and whoever saved last simply won, silently.
    /// </para>
    ///
    /// <para>
    /// The dangerous case is not the lost board edit, it is the lost <i>decision</i>. Finalize and
    /// publish are statements about a set of rows: "these are signed off", "these are now official".
    /// A regenerate landing between the read and the write means the Registrar publishes a timetable
    /// nobody finalized and nobody has seen — and, because publishing emails faculty and students, a
    /// timetable that has already been announced by the time anyone notices.
    /// </para>
    ///
    /// <para>
    /// So the answer is a <b>409 naming the other operation</b> rather than a retry. Retrying is
    /// right for the seat counter (<c>SeatRelease</c>), where the intent — one seat — survives the
    /// race intact. It is wrong here: the rows the user was looking at are not the rows now in the
    /// database, so re-applying their decision would be applying it to something else. Reload, look,
    /// decide again.
    /// </para>
    /// </summary>
    internal static class ScheduleConcurrency
    {
        /// <summary>
        /// Saves, converting a concurrency failure into a 409. Returns <c>null</c> when the save
        /// succeeded, so callers read as <c>if (await TrySaveAsync(…) is { } conflict) return conflict;</c>.
        /// </summary>
        /// <param name="operation">
        /// What the caller was doing, in the words the user would use — "publish this schedule",
        /// "move this class". It completes the sentence "Someone else changed this semester's
        /// schedule while you were about to …", which is the only part of the message worth varying.
        /// </param>
        public static async Task<IResult?> TrySaveAsync(
            AppDbContext db, string operation, CancellationToken cancellationToken)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new
                {
                    message = $"Someone else changed this semester's schedule while you were about to {operation}. "
                        + "Nothing was saved — reload the schedule and check it before trying again.",
                    reference = "schedule-concurrency"
                });
            }
        }
    }
}
