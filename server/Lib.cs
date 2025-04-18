using SpacetimeDB;

namespace KulichaServerModule {
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

/// <summary>
    /// Table for storing location information for benefits
    /// </summary>
    [Table]
    public partial class BenefitLocation {
        [AutoInc]
        [PrimaryKey]
        public long LocationId;

        public string? Name;           // Location name or identifier
        public string? City;           // City where the benefit is available
        public string? Region;         // Region/province/state
        public string? Address;        // Street address or detailed location
        public double Latitude;        // Geographic coordinates - latitude
        public double Longitude;       // Geographic coordinates - longitude
        public double ServiceRadiusKm; // Radius of service coverage in kilometers
        public bool IsActive;          // Whether this location is currently active
        public Timestamp CreatedAt;    // When this location was created
        public Timestamp? UpdatedAt;   // When this location was last updated
    }

    /// <summary>
    /// Updated BenefitDefinition to reference BenefitLocation
    /// </summary>
    [Table]
    public partial class BenefitDefinition {
        [AutoInc]
        [PrimaryKey]
        public long BenefitId;
        public string? Name;
        public string? Description;
        public BenefitType Type { get; set; }
        public decimal Cost { get; set; }
        public string? Provider;
        public string? PolicyDetails;
        public bool IsActive;

        // Reference to location
        public long LocationId;  // Foreign key to BenefitLocation

        public Timestamp CreatedAt;
        public Timestamp? UpdatedAt;
    }

    /// <summary>
    /// Multi-location benefits mapping table
    /// This allows a single benefit to be available at multiple locations
    /// </summary>
    [Table]
    public partial class BenefitLocationMap {
        [AutoInc]
        [PrimaryKey]
        public long MapId;
        public long BenefitId;   // Reference to BenefitDefinition
        public long LocationId;  // Reference to BenefitLocation
        public Timestamp AddedAt;
        public Timestamp? RemovedAt;  // For tracking when a location is no longer offering the benefit
    }
}
