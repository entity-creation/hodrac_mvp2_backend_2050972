using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Hodrac_Backend_MVP2.Tests.Hosts;

public static class HostTestHelpers
{
    public static readonly byte[] PdfBytes = BuildFile("%PDF-1.7\n%test document\n", 2048);
    public static readonly byte[] PngBytes = BuildFile("\u0089PNG\r\n\u001a\n", 512, latin1: true);

    private static byte[] BuildFile(string header, int size, bool latin1 = false)
    {
        var head = latin1 ? System.Text.Encoding.Latin1.GetBytes(header) : System.Text.Encoding.ASCII.GetBytes(header);
        var bytes = new byte[Math.Max(size, head.Length)];
        head.CopyTo(bytes, 0);
        for (var i = head.Length; i < bytes.Length; i++) bytes[i] = (byte)('a' + i % 26);
        return bytes;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> CreateProfileAsync(HttpClient client, string displayName, string? slug = null)
    {
        var response = await client.PostAsJsonAsync("/api/host/profile", new { displayName, slug });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    public static MultipartFormDataContent VerificationForm(
        byte[]? file,
        string fileName = "id.pdf",
        string contentType = "application/pdf",
        string? cacNumber = "RC 1234567",
        params string[] proofUrls)
    {
        var form = new MultipartFormDataContent();
        if (cacNumber is not null) form.Add(new StringContent(cacNumber), "cacNumber");
        foreach (var url in proofUrls.Length == 0 ? new[] { "https://instagram.com/p/past-trip" } : proofUrls)
            form.Add(new StringContent(url), "pastTripProofUrls");
        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, "idDocument", fileName);
        }
        return form;
    }

    /// <summary>An empty multipart form as a browser sends it (FormData with no entries): only the closing boundary.</summary>
    public static HttpContent EmptyVerificationForm()
    {
        var content = new StringContent("--hodrac-empty-form--\r\n");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=hodrac-empty-form");
        return content;
    }

    /// <summary>Profile + confirmed payout account + Pending verification with a PDF.</summary>
    public static async Task<Guid> CreatePendingHostAsync(HttpClient client, string displayName)
    {
        var profile = await CreateProfileAsync(client, displayName);

        var payout = await client.PostAsJsonAsync("/api/host/payout-account",
            new { bankCode = "044", accountNumber = "0123456789", confirm = true });
        payout.StatusCode.Should().Be(HttpStatusCode.OK, await payout.Content.ReadAsStringAsync());

        var verification = await client.PostAsync("/api/host/verification", VerificationForm(PdfBytes));
        verification.StatusCode.Should().Be(HttpStatusCode.OK, await verification.Content.ReadAsStringAsync());

        return profile.GetProperty("id").GetGuid();
    }
}
