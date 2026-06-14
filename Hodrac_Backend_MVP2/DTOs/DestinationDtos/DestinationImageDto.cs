namespace Hodrac_Backend_MVP2.DTOs.DestinationDtos
{
    public record DestinationImageDto(
     string ImageUrl,
     string ThumbnailUrl,
     string Caption,
     int DisplayOrder,
     string ImageType
 );
}
