using Microsoft.AspNetCore.Identity;

namespace MediQueue.Infrastructure.Identity;

/// <summary>
/// A member of staff. Patients are never Identity users: they check in
/// anonymously and are tracked by ticket code.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Home department for clinicians, which scopes their workstation. Null for
    /// receptionists and admins, who work across the whole outpatient floor.
    /// </summary>
    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; } = true;
}
