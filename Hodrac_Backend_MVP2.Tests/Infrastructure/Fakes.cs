using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Payments;

namespace Hodrac_Backend_MVP2.Tests.Infrastructure;

/// <summary>
/// In-memory Paystack for integration tests: never touches the network.
/// Account numbers starting "000" fail to resolve (Paystack 422).
/// </summary>
public class FakePaystackClient : IPaystackClient
{
    public const string ResolvedAccountName = "JANE ADA DOE";

    public static readonly IReadOnlyList<PaystackBank> Banks = new[]
    {
        new PaystackBank("Access Bank", "044", "access-bank", "NGN", "nuban"),
        new PaystackBank("Test Bank", "001", "test-bank", "NGN", "nuban"),
    };

    private int _subaccountSeq;

    public ConcurrentQueue<PaystackSubaccountRequest> CreatedSubaccounts { get; } = new();
    public ConcurrentQueue<(string Code, PaystackSubaccountUpdate Update)> UpdatedSubaccounts { get; } = new();

    public Task<IReadOnlyList<PaystackBank>> ListBanksAsync(CancellationToken ct = default) => Task.FromResult(Banks);

    public Task<PaystackResolvedAccount> ResolveAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default)
    {
        if (accountNumber.StartsWith("000", StringComparison.Ordinal))
            throw new PaystackException("Could not resolve account name. Check parameters or try again.", HttpStatusCode.UnprocessableEntity);
        return Task.FromResult(new PaystackResolvedAccount(accountNumber, ResolvedAccountName, 1));
    }

    public Task<PaystackSubaccount> CreateSubaccountAsync(PaystackSubaccountRequest request, CancellationToken ct = default)
    {
        CreatedSubaccounts.Enqueue(request);
        var code = $"ACCT_fake{Interlocked.Increment(ref _subaccountSeq):D6}";
        return Task.FromResult(new PaystackSubaccount(code, request.BusinessName, ResolvedAccountName, request.SettlementBankCode, 0m, true));
    }

    /// <summary>Subaccount codes whose update call fails (Paystack 500). Every attempt is still recorded.</summary>
    public ConcurrentDictionary<string, bool> FailUpdateForSubaccount { get; } = new(StringComparer.Ordinal);

    public Task<PaystackSubaccount> UpdateSubaccountAsync(string subaccountCode, PaystackSubaccountUpdate request, CancellationToken ct = default)
    {
        UpdatedSubaccounts.Enqueue((subaccountCode, request));
        if (FailUpdateForSubaccount.ContainsKey(subaccountCode))
            throw new PaystackException("Paystack is having a moment", HttpStatusCode.InternalServerError);
        return Task.FromResult(new PaystackSubaccount(subaccountCode, request.BusinessName ?? "", ResolvedAccountName, request.SettlementBankCode, 0m, true));
    }

    /// <summary>Every initialize request, keyed by reference.</summary>
    public ConcurrentDictionary<string, PaystackInitializeRequest> Initialized { get; } = new();

    /// <summary>
    /// Per-reference verify responses. Without one, a verified reference that was
    /// initialized comes back "success" with exactly the initialized amount,
    /// currency and subaccount; an unknown reference is a Paystack 404.
    /// </summary>
    public ConcurrentDictionary<string, Func<PaystackInitializeRequest?, PaystackTransaction>> VerifyResponses { get; } = new();

    /// <summary>Payer emails whose initialize call fails (Paystack 400).</summary>
    public ConcurrentDictionary<string, bool> FailInitializeForEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int _verifyCalls;
    public int VerifyCalls => Volatile.Read(ref _verifyCalls);

    public Task<PaystackInitializeResult> InitializeAsync(PaystackInitializeRequest request, CancellationToken ct = default)
    {
        if (FailInitializeForEmail.ContainsKey(request.Email))
            throw new PaystackException("Invalid key", HttpStatusCode.BadRequest);
        Initialized[request.Reference] = request;
        return Task.FromResult(new PaystackInitializeResult($"https://checkout.paystack.test/{request.Reference}", "access_fake", request.Reference));
    }

    public async Task<PaystackTransaction> VerifyAsync(string reference, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _verifyCalls);
        await Task.Yield();
        Initialized.TryGetValue(reference, out var init);
        if (VerifyResponses.TryGetValue(reference, out var scripted))
            return scripted(init);
        if (init is null)
            throw new PaystackException("Transaction reference not found", HttpStatusCode.NotFound);
        return Transaction(reference, "success", init.AmountKobo, init.Currency, init.SubaccountCode);
    }

    /// <summary>A verify response as the real client would parse it.</summary>
    public static PaystackTransaction Transaction(
        string reference, string status, long amountKobo, string currency = "NGN", string? subaccountCode = null,
        string channel = "card", long feesKobo = 7_850)
    {
        var paidAt = DateTime.UtcNow;
        var raw = JsonSerializer.Serialize(new
        {
            status = true,
            message = "Verification successful",
            data = new
            {
                id = 4_000_000_001L,
                status,
                reference,
                amount = amountKobo,
                currency,
                channel,
                fees = feesKobo,
                paid_at = paidAt,
                gateway_response = status == "success" ? "Successful" : "Declined",
                subaccount = subaccountCode is null ? null : new { subaccount_code = subaccountCode },
            },
        });
        return new PaystackTransaction(4_000_000_001L, status, reference, amountKobo, currency, channel,
            status == "success" ? paidAt : null, feesKobo, null, subaccountCode, null, raw);
    }

    public Task<PaystackRefund> RefundAsync(string reference, long? amountKobo = null, CancellationToken ct = default) =>
        throw new NotSupportedException("FakePaystackClient.RefundAsync is not scripted yet.");
}

/// <summary>Captures emails instead of sending them.</summary>
public class RecordingEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, List<string> BodyLines, string DeepLink);

    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public Task SendAsync(string to, string subject, List<string> bodyLines, string deepLink)
    {
        Sent.Enqueue(new SentEmail(to, subject, bodyLines, deepLink));
        return Task.CompletedTask;
    }
}
