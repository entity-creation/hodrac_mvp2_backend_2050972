using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hodrac_Backend_MVP2.Models
{
    public class NewsLetterEmail
    {
        public Guid EmailId { get; set; }
        [EmailAddress]
        [JsonPropertyName("userEmail")]
        public string UserEmail { get; set; }
    }
}
