using System.ComponentModel.DataAnnotations;

namespace MediQueue.Shared.Contracts;

public class LoginRequest
{
    [Required(ErrorMessage = "Enter your staff email.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}
