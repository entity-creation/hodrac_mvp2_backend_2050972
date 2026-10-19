using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Services
{
    // Identity's default token provider uses one lifespan for every token type.
    // Email confirmation gets its own provider so its links can live longer
    // than password-reset links (see Program.cs for both lifespans).
    public class EmailConfirmationTokenProvider<TUser> : DataProtectorTokenProvider<TUser>
        where TUser : class
    {
        public const string ProviderName = "EmailConfirmation";

        public EmailConfirmationTokenProvider(
            IDataProtectionProvider dataProtectionProvider,
            IOptions<EmailConfirmationTokenProviderOptions> options,
            ILogger<DataProtectorTokenProvider<TUser>> logger)
            : base(dataProtectionProvider, options, logger)
        {
        }
    }

    public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
    {
        public EmailConfirmationTokenProviderOptions()
        {
            Name = EmailConfirmationTokenProvider<object>.ProviderName;
        }
    }
}
