using MediQueue.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace MediQueue.Infrastructure.Seed;

/// <summary>
/// Decides the password every seeded staff account gets. The repository is
/// public, so its documented password must never open a deployed site; and a
/// password the policy rejects must stop the deploy, not produce a live site
/// on which nobody can sign in.
/// </summary>
public static class DemoSeedPassword
{
    public const string ConfigKey = "Seed:StaffPassword";

    public static async Task<string> ResolveAsync(
        UserManager<ApplicationUser> users,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var configured = configuration[ConfigKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (isDevelopment)
            {
                return DemoData.StaffPassword;
            }

            throw new InvalidOperationException(
                "Demo data is enabled but no staff password is set. Set the Seed__StaffPassword config var.");
        }

        if (!isDevelopment && configured == DemoData.StaffPassword)
        {
            throw new InvalidOperationException(
                "Seed__StaffPassword is the development password published in the README, which is public. Choose another.");
        }

        // Identity's own validators, so this can never disagree with account creation.
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new ApplicationUser(), configured);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Seed__StaffPassword does not meet the password policy: {string.Join(" ", problems)}");
        }

        return configured;
    }
}
