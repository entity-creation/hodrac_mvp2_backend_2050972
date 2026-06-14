using System.Text.Json.Serialization;

namespace Hodrac_Backend_MVP2.DTOs.EmbeddingDtos
{
    public record EmbeddingRequest
    {
        public string Text {get; set; }
    }

    public record EmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public double[] Embedding { get; set; }
    }
}
