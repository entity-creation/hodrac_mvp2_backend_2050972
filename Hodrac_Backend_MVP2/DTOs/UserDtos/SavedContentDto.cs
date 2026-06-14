using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;

namespace Hodrac_Backend_MVP2.DTOs.UserDtos
{
    public record SavedContentDto(
    List<WishlistCardDto> SavedWishlists,
    List<DestinationSummaryDto> SavedDestinations
);
}
