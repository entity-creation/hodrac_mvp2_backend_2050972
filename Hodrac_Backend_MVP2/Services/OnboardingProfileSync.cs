using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Events;
using Hodrac_Backend_MVP2.NoSql.Models;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services
{
    // Onboarding answers live in Postgres (ApplicationUser.Onboarding*) and are
    // copied to the Mongo profile asynchronously. This finds users whose Mongo
    // copy is missing or out of date and re-publishes OnboardingCompletedEvent,
    // so the normal handler applies them (including rule deltas, once).
    public class OnboardingProfileSync
    {
        private readonly HodracDbContext _db;
        private readonly IMongoCollection<UserPsychographicProfile> _profiles;
        private readonly IBehaviorEventQueue _events;

        public OnboardingProfileSync(HodracDbContext db, IMongoDatabase mongo, IBehaviorEventQueue events)
        {
            _db = db;
            _profiles = mongo.GetCollection<UserPsychographicProfile>("user_psychographic_profiles");
            _events = events;
        }

        public async Task<int> QueueMissingAsync(CancellationToken ct = default)
        {
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.OnboardingWhoTheyTravelWith != null || u.OnboardingTripTypePreference != null
                         || u.OnboardingBudgetProfile != null || u.OnboardingPrimaryPriority != null)
                .Select(u => new
                {
                    u.Id,
                    Answers = new OnboardingAnswers
                    {
                        WhoTheyTravelWith = u.OnboardingWhoTheyTravelWith ?? "",
                        TripTypePreference = u.OnboardingTripTypePreference ?? "",
                        BudgetProfile = u.OnboardingBudgetProfile ?? "",
                        PrimaryPriority = u.OnboardingPrimaryPriority ?? "",
                    }
                })
                .ToListAsync(ct);
            if (users.Count == 0) return 0;

            var ids = users.Select(u => u.Id).ToList();
            var mongoAnswers = (await _profiles
                    .Find(Builders<UserPsychographicProfile>.Filter.In(p => p.AuthUserReferenceId, ids))
                    .Project(p => new { p.AuthUserReferenceId, p.ExplicitOnboardingAnswers })
                    .ToListAsync(ct))
                .GroupBy(p => p.AuthUserReferenceId)
                .ToDictionary(g => g.Key, g => g.First().ExplicitOnboardingAnswers);

            var queued = 0;
            foreach (var user in users)
            {
                mongoAnswers.TryGetValue(user.Id, out var current);
                if (current is not null && SameAnswers(Merge(user.Answers, current), current)) continue;

                _events.Publish(new OnboardingCompletedEvent(
                    UserId: user.Id,
                    WhoTheyTravelWith: user.Answers.WhoTheyTravelWith,
                    TripTypePreference: user.Answers.TripTypePreference,
                    BudgetProfile: user.Answers.BudgetProfile,
                    PrimaryPriority: user.Answers.PrimaryPriority,
                    OccurredAt: DateTimeOffset.UtcNow));
                queued++;
            }
            return queued;
        }

        /// <summary>Postgres answers keyed by user id (only users with at least one answer).</summary>
        public static async Task<Dictionary<string, OnboardingAnswers>> LoadAnswersAsync(
            HodracDbContext db, IReadOnlyCollection<string> userIds, CancellationToken ct)
        {
            if (userIds.Count == 0) return new();
            return await db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id)
                         && (u.OnboardingWhoTheyTravelWith != null || u.OnboardingTripTypePreference != null
                          || u.OnboardingBudgetProfile != null || u.OnboardingPrimaryPriority != null))
                .ToDictionaryAsync(u => u.Id, u => new OnboardingAnswers
                {
                    WhoTheyTravelWith = u.OnboardingWhoTheyTravelWith ?? "",
                    TripTypePreference = u.OnboardingTripTypePreference ?? "",
                    BudgetProfile = u.OnboardingBudgetProfile ?? "",
                    PrimaryPriority = u.OnboardingPrimaryPriority ?? "",
                }, ct);
        }

        /// <summary>Non-blank Postgres answers win; blanks keep the existing profile value.</summary>
        public static OnboardingAnswers Merge(OnboardingAnswers source, OnboardingAnswers? current)
        {
            static string Pick(string? a, string? b) => string.IsNullOrWhiteSpace(a) ? b ?? "" : a.Trim();
            return new OnboardingAnswers
            {
                WhoTheyTravelWith = Pick(source.WhoTheyTravelWith, current?.WhoTheyTravelWith),
                TripTypePreference = Pick(source.TripTypePreference, current?.TripTypePreference),
                BudgetProfile = Pick(source.BudgetProfile, current?.BudgetProfile),
                PrimaryPriority = Pick(source.PrimaryPriority, current?.PrimaryPriority),
            };
        }

        private static bool SameAnswers(OnboardingAnswers a, OnboardingAnswers b) =>
            a.WhoTheyTravelWith == (b.WhoTheyTravelWith ?? "")
            && a.TripTypePreference == (b.TripTypePreference ?? "")
            && a.BudgetProfile == (b.BudgetProfile ?? "")
            && a.PrimaryPriority == (b.PrimaryPriority ?? "");
    }
}
