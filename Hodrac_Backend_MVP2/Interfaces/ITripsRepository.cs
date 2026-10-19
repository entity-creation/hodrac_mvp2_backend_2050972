using Hodrac_Backend_MVP2.DTOs.TripPostDtos;

namespace Hodrac_Backend_MVP2.Interfaces
{
    public interface ITripsRepository
    {
        Task<List<TripInterestDto>> GetAllInterests(Guid id);
    }
}
