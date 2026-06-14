namespace Hodrac_Backend_MVP2.DTOs.UserDtos
{
    public record UserProfileDto(
    string UserId,
    string TravelGroup,
    string BudgetProfile,
    string PrimaryPriority,
    List<string> TopTags,
    string PrimaryTravelerType,
    List<string> SecondaryTravelerTypes
);
}
