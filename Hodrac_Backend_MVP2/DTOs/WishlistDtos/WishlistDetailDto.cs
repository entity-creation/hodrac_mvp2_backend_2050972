using Hodrac.DTOs;

namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record WishlistDetailDto(
    Guid WishlistId,
    string WishlistName,
    string WishlistDescription,
    string ShortStory,
    string WishlistHeroImage,
    int TotalDays,
    string PeopleType,
    decimal BasePricePerPerson,
    decimal CalculatedTotalCost,
    decimal DepositAmountRequired,
    long TotalGlobalSaveCount,
    List<string> GlobalInclusions,
    List<string> PsychologicalVibeTags,
    List<ItineraryDayDto> ItineraryDays,
    WishlistPricingSnapshotDto? ActivePricingSnapshot,
    bool IsUserOwner,
    bool IsCollaborator,
    List<WishlistCreatorAttributionDto> CreatorAttributions,
    uint Xmin,
    string UserRole               // "Owner" | "Editor" | "Viewer" | "None"
    );
}
