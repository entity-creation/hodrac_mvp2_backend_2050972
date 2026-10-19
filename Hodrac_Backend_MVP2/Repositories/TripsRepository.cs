using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.DTOs.TripPostDtos;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Repositories
{
    public class TripsRepository : ITripsRepository
    {
        private readonly HodracDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IUserRepository _userRepo;

        public TripsRepository(HodracDbContext db, ICurrentUserService currentUser, IUserRepository userRepo)
        {
            _db = db;
            _currentUser = currentUser;
            _userRepo = userRepo;
        }
            public async Task<List<TripInterestDto>> GetAllInterests(Guid id)
        {
            var trip = await _db.TripPosts.FindAsync(id);
            
            var interests = await _db.TripInterests
                .Where(i => i.TripPostId == id && i.Status != TripInterestStatus.Withdrawn)
                .OrderBy(i => i.RequestedAt)
                .ToListAsync();

            var result = new List<TripInterestDto>();

            foreach (var interest in interests)
            {
                var user = await _userRepo.GetByIdAsync(
                    interest.RequesterUserId.ToString()
                );

                result.Add(new TripInterestDto
                {
                    Id = interest.Id,
                    TripPostId = interest.TripPostId,
                    RequesterUserId = interest.RequesterUserId,
                    Message = interest.Message,
                    Status = interest.Status.ToString(),
                    RequesterName = user?.DisplayName
                });
            }
            return result;
        }
    }
}
