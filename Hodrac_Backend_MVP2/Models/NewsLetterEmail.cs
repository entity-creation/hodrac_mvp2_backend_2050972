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

        /// <summary>
        /// UTC. Set by the newsletter unsubscribe link; cleared when the same
        /// address signs up again. Not part of the sign-up request or response.
        /// </summary>
        [JsonIgnore]
        public DateTime? UnsubscribedAt { get; set; }
    }
}
