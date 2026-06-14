namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
     public record TransitRouteDto(
     string TransitType,
     string FromCity,
     string ToCity,
     decimal CostPerPerson,
     int DurationMinutes
 );
}
