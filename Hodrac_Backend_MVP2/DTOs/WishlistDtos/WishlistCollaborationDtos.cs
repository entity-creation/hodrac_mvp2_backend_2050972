namespace Hodrac_Backend_MVP2.DTOs.WishlistDtos
{
    public record ForkWishlistResponseDto(Guid NewWishlistId, string Message);

    public record AddCollaboratorRequestDto(string Email, string Role);  // Role: "Editor" | "Viewer"

    public record CollaboratorDto(Guid UserId, string Email, string Role, DateTimeOffset JoinedAt);
}
