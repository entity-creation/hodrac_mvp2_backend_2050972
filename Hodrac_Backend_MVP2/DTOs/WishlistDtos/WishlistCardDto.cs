namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record WishlistCardDto(
    Guid WishlistId,
    string WishlistName,
    string WishlistDescription,
    string ShortStory,
    string WishlistHeroImage,
    int TotalDays,
    string PeopleType,
    decimal BasePricePerPerson,
    decimal CalculatedTotalCost,
    long TotalGlobalSaveCount,
    bool IsFeatured,
    List<string> PsychologicalVibeTags,
    string PrimaryPersonaTarget
);
}
