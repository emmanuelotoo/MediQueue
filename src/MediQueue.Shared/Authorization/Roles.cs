using Microsoft.AspNetCore.Authorization;

namespace MediQueue.Shared.Authorization;

/// <summary>
/// Staff roles. Patients never hold an account, so there is no patient role.
/// </summary>
public static class Roles
{
    /// <summary>Runs the front desk: check-in, priority, calling, transfers.</summary>
    public const string Receptionist = "Receptionist";

    /// <summary>Sees and treats patients: starts and completes consultations.</summary>
    public const string Clinician = "Clinician";

    /// <summary>Everything a receptionist can do, plus reporting.</summary>
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Receptionist, Clinician, Admin];
}

/// <summary>Authorization policy names, so controllers never spell a role wrong.</summary>
public static class Policies
{
    /// <summary>Can move patients through the queue.</summary>
    public const string ManageQueue = nameof(ManageQueue);

    /// <summary>Can start and complete consultations.</summary>
    public const string TreatPatients = nameof(TreatPatients);

    /// <summary>Can read reporting.</summary>
    public const string ViewAnalytics = nameof(ViewAnalytics);

    /// <summary>
    /// Defines what each policy means, once. The API enforces these and the
    /// Blazor client uses them to decide what to show; if only one side defined
    /// them, the client would either throw on an unknown policy or drift into
    /// offering actions the server refuses.
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ManageQueue, policy =>
            policy.RequireRole(Roles.Receptionist, Roles.Admin));

        options.AddPolicy(TreatPatients, policy =>
            policy.RequireRole(Roles.Receptionist, Roles.Clinician, Roles.Admin));

        options.AddPolicy(ViewAnalytics, policy =>
            policy.RequireRole(Roles.Admin));
    }
}
