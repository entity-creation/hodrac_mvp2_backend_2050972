namespace Hodrac_Backend_MVP2.DTOs.TripPostDtos
{
    public class TripInterestDto
    {
        public Guid Id { get; set; }
        public Guid TripPostId { get; set; }
        public Guid RequesterUserId { get; set; }
        public string RequesterName { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string Status { get; set; } = "";
    }
}
