namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record WishlistPricingSnapshotDto(
    int TravelersCount,
    decimal BasePricePerPerson,
    decimal OptionalActivitiesTotal,
    decimal DepositAmountRequired,
    DateTimeOffset ValidUntil,      // Frontend uses this for the countdown timer
    decimal TotalEstimate           // Computed: (BasePricePerPerson + OptionalActivitiesTotal) * TravelersCount
);
}
