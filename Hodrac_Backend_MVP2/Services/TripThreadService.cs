using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Services
{
    public interface ITripThreadService
    {
        /// <summary>
        /// Stages (does not save) the trip's thread - created lazily with the
        /// author as a participant - and the user's participant row. The
        /// caller saves inside its own transaction (holding a lock on the trip
        /// so concurrent callers serialize).
        /// </summary>
        Task StageAddToThreadAsync(TripPost trip, Guid userId);
    }

    // Shared by TripsController (community approval, hosted -> community) and
    // PaymentService (hosted trips: added on the first successful payment).
    // Scoped: uses the request's HodracDbContext, so staged rows are saved by
    // the caller's SaveChanges.
    public class TripThreadService : ITripThreadService
    {
        private readonly HodracDbContext _db;

        public TripThreadService(HodracDbContext db) => _db = db;

        // Checks the change tracker too, so it can be called for several users
        // before one SaveChanges.
        public async Task StageAddToThreadAsync(TripPost trip, Guid userId)
        {
            var thread = _db.TripThreads.Local.FirstOrDefault(t => t.TripPostId == trip.Id)
                ?? await _db.TripThreads.FirstOrDefaultAsync(t => t.TripPostId == trip.Id);
            if (thread is null)
            {
                thread = new TripThread { TripPostId = trip.Id };
                _db.TripThreads.Add(thread);

                // Poster is always a participant of their own trip thread.
                _db.TripThreadParticipants.Add(new TripThreadParticipant
                {
                    TripThreadId = thread.Id,
                    UserId = trip.AuthorUserId
                });

                if (userId == trip.AuthorUserId) return;
            }
            else if (_db.TripThreadParticipants.Local.Any(p => p.TripThreadId == thread.Id && p.UserId == userId)
                || await _db.TripThreadParticipants
                    .AnyAsync(p => p.TripThreadId == thread.Id && p.UserId == userId))
            {
                return;
            }

            _db.TripThreadParticipants.Add(new TripThreadParticipant
            {
                TripThreadId = thread.Id,
                UserId = userId
            });
        }
    }
}
