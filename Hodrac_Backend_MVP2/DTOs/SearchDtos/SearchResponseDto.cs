using Hodrac_Backend_MVP2.DTOs.DestinationDtos;
using Hodrac_Backend_MVP2.DTOs.WishlistDtos;

namespace Hodrac_Backend_MVP2.DTOs.SearchDtos
{
    public record SearchResponseDto(
    string CanonicalPhrase,
    string ClusterId,
    List<DestinationSummaryDto> Destinations,
    List<WishlistCardDto> Wishlists
);
}
