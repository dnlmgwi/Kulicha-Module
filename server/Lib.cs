using SpacetimeDB;
using System.Text.RegularExpressions;

namespace StdbModule {
using Enums;
// ====================== Database Table Definitions ======================

/// <summary>
/// Table that stores user accounts and their roles
/// </summary>
[Table]
public partial class User {
    [PrimaryKey]
    public Identity Identity;
    public string? Username;
    public string? Email;
    public UserRole Role { get; init; }
    public bool IsEmailVerified;
    public Timestamp RegisteredAt;
}

/// <summary>
/// Table for storing pending registrations that need email verification
/// </summary>
[Table]
public partial class PendingVerification {
    [PrimaryKey]
    public Identity Identity;

    public string? Username;
    public string? Email;
    public UserRole Role { get; init; }
    public string? VerificationCode;

    public Timestamp ExpiresAt;
}

/// <summary>
/// Table for authentication sessions
/// </summary>
[Table]
public partial class AuthSession {
    [PrimaryKey]
    public Identity Identity;
    public Timestamp LastActiveTime;
    public string? ActiveDeviceId;
}

/// <summary>
/// Audit log for tracking important system events
/// </summary>
[Table]
public partial class AuditLog {
    [AutoInc]
    [PrimaryKey]
    public long Field;
    public Identity Identity; // User who performed the action
    public string? Action; // Type of action performed
    public string? Details; // Additional context
    public Timestamp Timestamp;
}
}
