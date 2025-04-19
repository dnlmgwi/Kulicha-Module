using SpacetimeDB;
using System.Text.RegularExpressions;

namespace Server.Modules.Benefits {
using Enums;
public static partial class BenefitModule {
    // ====================== Helper Constants & Methods ======================

    private const string EmailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
    private const string UsernamePattern = @"^[a-zA-Z0-9_-]{3,20}$"; // Keep for registration validation
    private static readonly Random Random = new Random();
    private static readonly TimeDuration VerificationExpiry = new TimeDuration { Microseconds = 86400000000 / 4 }; // 6 hours expiry

    private static bool IsValidEmail(string email) => !string.IsNullOrWhiteSpace(email) && Regex.IsMatch(email, EmailPattern);
    private static bool IsValidUsername(string username) => !string.IsNullOrWhiteSpace(username) && Regex.IsMatch(username, UsernamePattern);

    private static string GenerateVerificationCode()
    {
        const string chars = "0123456789"; // Numeric codes are often easier
        return new string(Enumerable.Repeat(chars, 6).Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    // ====================== Utility: Logging ======================

    private static void LogAuthAction(ReducerContext ctx, string action, string details, Identity? targetIdentity = null)
    {
        var identityToLog = targetIdentity ?? ctx.Sender; // Log against target or sender
        Log.Info($"Auth Action by {ctx.Sender} (Target: {identityToLog}): {action} - {details}");
        ctx.Db.AuditLog.Insert(new AuditLog
        {
            Identity = identityToLog,
            Action = action,
            Details = details,
            Timestamp = ctx.Timestamp
        });
    }

    private const double EarthRadiusKm = 6371.0;

    private static bool CheckRole(ReducerContext ctx, out User? user, params UserRole[] requiredRoles)
    {
        user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            Log.Warn($"BenefitModule: Unauthenticated access attempt by {ctx.Sender}.");
            LogBenefitAction(ctx, user!.Identity.ToString(), "Unauthenticated access", $"Unauthenticated access attempt by {ctx.Sender}.");
            return false;
        }
        if (!requiredRoles.Contains(user.Role))
        {
            Log.Warn($"BenefitModule: User {user.Username} ({user.Role}) attempted action requiring roles: {string.Join(", ", requiredRoles)}.");
            LogBenefitAction(ctx, user.Identity.ToString(), "Privileged access", $"User {user.Username} ({user.Role}) attempted action requiring roles: {string.Join(", ", requiredRoles)}.");
            return false;
        }
        return true;
    }

    private static bool IsAuthenticated(ReducerContext ctx, out User? user)
    {
        user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            Log.Warn($"BenefitModule: Unauthenticated access attempt by {ctx.Sender}. Action requires login.");
            return false;
        }
        return true;
    }

    private static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var radLat1 = ToRadians(lat1);
        var radLat2 = ToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(radLat1) * Math.Cos(radLat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static void LogBenefitAction(ReducerContext ctx, string identityStr, string action, string details)
    {
        Log.Info($"Benefit Action by {identityStr}: {action} - {details}");
        ctx.Db.AuditLog.Insert(new AuditLog { Identity = ctx.Sender, Action = $"Benefit_{action}", Details = details, Timestamp = ctx.Timestamp });
    }

    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info($"Initializing Benefit Module...");
        var existingLocations = ctx.Db.BenefitLocation.Iter();

        if (!existingLocations.Any())
        {
            Log.Info("BenefitLocation table is empty. Seeding initial data based on config...");
            try
            {
                BenefitSeeder.SeedInitialData(ctx);
                Log.Info("Finished seeding initial data.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error during initial data seeding: {ex.Message} \n {ex.StackTrace}");
            }
        }
        else
        {
            Log.Info("BenefitLocation table is not empty. Skipping initial data seeding.");
        }
        Log.Info("Benefit module initialization check complete.");
    }

    [Reducer]
    public static void CreateBenefitLocation(ReducerContext ctx, string name, string city, string region, string address, double latitude, double longitude, double serviceRadiusKm, bool isActive)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors and Benefactors can create locations.");
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(city) || serviceRadiusKm <= 0)
        {
            throw new Exception("Invalid input: Name, City cannot be empty, and Service Radius must be positive.");
        }
        // Add validation for latitude (-90 to 90) and longitude (-180 to 180)
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
        {
            throw new Exception("Invalid input: Latitude must be between -90 and 90, Longitude between -180 and 180.");
        }


        var newLocation = new BenefitLocation
        {
            Name = name,
            City = city,
            Region = region,
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            ServiceRadiusKm = serviceRadiusKm,
            IsActive = isActive,
            CreatedAt = ctx.Timestamp,
            UpdatedAt = null
        };

        var inserted = ctx.Db.BenefitLocation.Insert(newLocation);
        LogBenefitAction(ctx, user!.Identity.ToString(), "LocationCreated", $"Created Location ID: {inserted.LocationId} - {inserted.Name}");
    }

    [Reducer]
    public static void UpdateBenefitLocation(ReducerContext ctx, long locationId, string name, string city, string? region, string? address, double latitude, double longitude, double serviceRadiusKm, bool isActive)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can update locations.");
        }

        var location = ctx.Db.BenefitLocation.LocationId.Find(locationId);
        if (location == null)
        {
            throw new Exception($"Benefit Location with ID {locationId} not found.");
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(city) || serviceRadiusKm <= 0)
        {
            throw new Exception("Invalid input: Name, City cannot be empty, and Service Radius must be positive.");
        }
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
        {
            throw new Exception("Invalid input: Latitude must be between -90 and 90, Longitude between -180 and 180.");
        }

        location.Name = name;
        location.City = city;
        location.Region = region ?? location.Region;
        location.Address = address ?? location.Address;
        location.Latitude = latitude;
        location.Longitude = longitude;
        location.ServiceRadiusKm = serviceRadiusKm;
        location.IsActive = isActive;
        location.UpdatedAt = ctx.Timestamp;

        ctx.Db.BenefitLocation.LocationId.Update(location);
        LogBenefitAction(ctx, user!.Identity.ToString(), "LocationUpdated", $"Updated Location ID: {location.LocationId} - {location.Name}");
    }

    [Reducer]
    public static void DeleteBenefitLocation(ReducerContext ctx, long locationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor))
        {
            throw new Exception("Permission denied: Only Auditors can delete locations.");
        }

        var location = ctx.Db.BenefitLocation.LocationId.Find(locationId);
        if (location == null)
        {
            throw new Exception($"Benefit Location with ID {locationId} not found.");
        }

        bool isPrimaryFk = ctx.Db.BenefitDefinition.Iter().Any(bd => bd.LocationId == locationId);
        if (isPrimaryFk)
        {
            throw new Exception($"Cannot delete location {locationId}: It is referenced as the primary location by one or more Benefit Definitions.");
        }

        var dependentMaps = ctx.Db.BenefitLocationMap.Iter().Where(m => m.LocationId == locationId).ToList();
        if (dependentMaps.Any())
        {
            // Safer approach: prevent deletion if mappings exist.
            // Cascade delete (deleting maps here) can be risky if not fully intended.
            throw new Exception($"Cannot delete location {locationId}: It is mapped to {dependentMaps.Count} benefits. Unmap them first.");
        }

        string locationName = location.Name; // Capture name before potential deletion
        LogBenefitAction(ctx, user!.Identity.ToString(), "LocationDeleted", $"Deleted Location ID: {locationId} - {locationName}");
        ctx.Db.BenefitLocation.LocationId.Delete(locationId);
    }


    [Reducer]
    public static void CreateBenefitDefinition(ReducerContext ctx, string name, string description, int typeInt, long cost, string provider, string policyDetails, bool isActive, long primaryLocationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can create benefit definitions.");
        }

        if (!Enum.IsDefined(typeof(BenefitType), typeInt))
        {
            throw new Exception($"Invalid BenefitType value: {typeInt}.");
        }
        var benefitType = (BenefitType)typeInt;

        if (ctx.Db.BenefitLocation.LocationId.Find(primaryLocationId) == null) // Use Pk.Find with ulong
        {
            throw new Exception($"Primary Location ID {primaryLocationId} does not exist.");
        }

        if (string.IsNullOrWhiteSpace(name) || cost < 0)
        {
            throw new Exception("Invalid input: Name cannot be empty, and Cost cannot be negative.");
        }

        var newBenefit = new BenefitDefinition
        {
            Name = name,
            Description = description,
            Type = benefitType,
            Cost = cost,
            Provider = provider,
            PolicyDetails = policyDetails,
            IsActive = isActive,
            LocationId = primaryLocationId,
            CreatedAt = ctx.Timestamp,
            UpdatedAt = null
        };

        var inserted = ctx.Db.BenefitDefinition.Insert(newBenefit);
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitCreated", $"Created Benefit ID: {inserted.BenefitId} - {inserted.Name}");

        ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
        {
            BenefitId = inserted.BenefitId,
            LocationId = primaryLocationId,
            AddedAt = ctx.Timestamp
        });
        LogBenefitAction(ctx, user.Identity.ToString(), "BenefitAutoMapped", $"Auto-mapped Benefit {inserted.BenefitId} to primary Location {primaryLocationId}");
    }

    [Reducer]
    public static void UpdateBenefitDefinition(ReducerContext ctx, long benefitId, string name, string? description, int typeInt, long cost, string? provider, string? policyDetails, bool isActive, long primaryLocationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can update benefit definitions.");
        }

        var benefit = ctx.Db.BenefitDefinition.BenefitId.Find(benefitId); // Use Pk.Find with ulong
        if (benefit == null)
        {
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        }

        if (!Enum.IsDefined(typeof(BenefitType), typeInt))
        {
            throw new Exception($"Invalid BenefitType value: {typeInt}.");
        }
        var benefitType = (BenefitType)typeInt;

        if (ctx.Db.BenefitLocation.LocationId.Find(primaryLocationId) == null) // Use Pk.Find with ulong
        {
            throw new Exception($"Primary Location ID {primaryLocationId} does not exist.");
        }

        if (string.IsNullOrWhiteSpace(name) || cost < 0)
        {
            throw new Exception("Invalid input: Name cannot be empty, and Cost cannot be negative.");
        }

        benefit.Name = name;
        benefit.Description = description ?? benefit.Description;
        benefit.Type = benefitType;
        benefit.Cost = cost; // Use decimal for cost
        benefit.Provider = provider ?? benefit.Provider;
        benefit.PolicyDetails = policyDetails ?? benefit.PolicyDetails;
        benefit.IsActive = isActive;
        benefit.LocationId = primaryLocationId;
        benefit.UpdatedAt = ctx.Timestamp;

        ctx.Db.BenefitDefinition.BenefitId.Update(benefit); // Use Pk.Update with the object
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitUpdated", $"Updated Benefit ID: {benefit.BenefitId} - {benefit.Name}");
    }

    [Reducer]
    public static void DeleteBenefitDefinition(ReducerContext ctx, long benefitId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor))
        {
            throw new Exception("Permission denied: Only Auditors can delete benefit definitions.");
        }

        var benefit = ctx.Db.BenefitDefinition.BenefitId.Find(benefitId);
        if (benefit == null)
        {
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        }

        var dependentMaps = ctx.Db.BenefitLocationMap.Iter().Where(m => m.BenefitId == benefitId).ToList();
        if (dependentMaps.Any())
        {
            Log.Info($"Deleting {dependentMaps.Count} location mappings associated with benefit {benefitId} before deleting the definition.");
            foreach (var map in dependentMaps)
            {
                ctx.Db.BenefitLocationMap.Delete(map);
            }
        }

        string benefitName = benefit.Name; // Capture name before potential deletion
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitDeleted", $"Deleted Benefit ID: {benefitId} - {benefitName}");
        ctx.Db.BenefitDefinition.BenefitId.Delete(benefitId);
    }

    [Reducer]
    public static void MapBenefitToLocation(ReducerContext ctx, long benefitId, long locationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can manage benefit mappings.");
        }

        if (ctx.Db.BenefitDefinition.BenefitId.Find(benefitId) == null) // Use Pk.Find with ulong
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        if (ctx.Db.BenefitLocation.LocationId.Find(locationId) == null) // Use Pk.Find with ulong
            throw new Exception($"Benefit Location with ID {locationId} not found.");

        bool mappingExists = ctx.Db.BenefitLocationMap.Iter()
            .Any(m => m.BenefitId == benefitId && m.LocationId == locationId);
        if (mappingExists)
        {
            Log.Warn($"Benefit {benefitId} is already mapped to Location {locationId}. Skipping.");
            return;
        }

        var newMap = new BenefitLocationMap
        {
            BenefitId = benefitId,
            LocationId = locationId,
            AddedAt = ctx.Timestamp
        };
        ctx.Db.BenefitLocationMap.Insert(newMap);
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitMapped", $"Mapped Benefit {benefitId} to Location {locationId}");
    }

    [Reducer]
    public static void UnmapBenefitFromLocation(ReducerContext ctx, long benefitId, long locationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can manage benefit mappings.");
        }

        // Find the specific map entry to delete. This iteration can be inefficient.
        // A table structure allowing direct lookup (e.g., composite key or separate PK) is better.
        var mapToDelete = ctx.Db.BenefitLocationMap.Iter()
            .FirstOrDefault(m => m.BenefitId == benefitId && m.LocationId == locationId);

        if (mapToDelete == null)
        {
            throw new Exception($"Mapping between Benefit {benefitId} and Location {locationId} not found.");
        }

        // Assuming map object itself is sufficient for deletion
        ctx.Db.BenefitLocationMap.Delete(mapToDelete);
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitUnmapped", $"Unmapped Benefit {benefitId} from Location {locationId}");
    }

    [Reducer]
    public static void QueryActiveBenefitsByType(ReducerContext ctx, int typeInt)
    {
        if (!IsAuthenticated(ctx, out var user))
        {
            throw new Exception("Authentication required.");
        }

        if (!Enum.IsDefined(typeof(BenefitType), typeInt))
        {
            throw new Exception($"Invalid BenefitType value: {typeInt}.");
        }
        var benefitType = (BenefitType)typeInt;

        var results = ctx.Db.BenefitDefinition.Iter()
            .Where(b => b.IsActive && b.Type == benefitType)
            .Select(b => new { b.BenefitId, b.Name, b.Description }) // Example projection
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsByType", $"Queried active benefits of type {benefitType}. Found {results.Count}.");
        Log.Info($"Query results (for server log demo): Found {results.Count} active {benefitType} benefits.");
        // Client retrieves data via subscription and filtering, or subscription to pre-filtered view tables.
    }

    [Reducer]
    public static void QueryActiveBenefitsAtLocation(ReducerContext ctx, long locationId)
    {
        if (!IsAuthenticated(ctx, out var user))
        {
            throw new Exception("Authentication required.");
        }

        var location = ctx.Db.BenefitLocation.LocationId.Find(locationId); // Use Pk.Find with ulong
        if (location == null || !location.IsActive)
        {
            Log.Warn($"Query attempted for non-existent or inactive location ID: {locationId}");
            // Optionally LogBenefitAction here indicating query attempt on invalid location
            return; // Exit gracefully, client subscription won't show results for this location anyway
        }

        var mappedBenefitIds = ctx.Db.BenefitLocationMap.Iter()
            .Where(m => m.LocationId == locationId)
            .Select(m => m.BenefitId)
            .ToHashSet();

        if (!mappedBenefitIds.Any())
        {
            Log.Info($"No benefits mapped to active location {locationId}.");
            // Optionally LogBenefitAction here
            return;
        }

        var results = ctx.Db.BenefitDefinition.Iter()
            .Where(b => b.IsActive && mappedBenefitIds.Contains(b.BenefitId))
            .Select(b => new { b.BenefitId, b.Name, b.Description, b.Type, b.Cost }) // Example projection
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsAtLocation", $"Queried active benefits at Location {locationId}. Found {results.Count}.");
        Log.Info($"Query results (for server log demo): Found {results.Count} active benefits at location {locationId}.");
    }

    [Reducer]
    public static void QueryActiveBenefitsNearPoint(ReducerContext ctx, double latitude, double longitude, double radiusKm)
    {
        if (!IsAuthenticated(ctx, out var user))
        {
            throw new Exception("Authentication required.");
        }

        if (radiusKm <= 0)
        {
            throw new Exception("Radius must be positive.");
        }
        if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
        {
            throw new Exception("Invalid input: Latitude must be between -90 and 90, Longitude between -180 and 180.");
        }

        var nearbyActiveLocationIds = ctx.Db.BenefitLocation.Iter()
            .Where(loc => loc.IsActive && CalculateDistanceKm(latitude, longitude, loc.Latitude, loc.Longitude) <= radiusKm)
            .Select(loc => loc.LocationId)
            .ToHashSet();

        if (!nearbyActiveLocationIds.Any())
        {
            Log.Info($"No active locations found within {radiusKm}km of ({latitude}, {longitude}).");
            // Optionally LogBenefitAction here
            return;
        }

        var mappedBenefitIds = ctx.Db.BenefitLocationMap.Iter()
            .Where(m => nearbyActiveLocationIds.Contains(m.LocationId))
            .Select(m => m.BenefitId)
            .ToHashSet();

        if (!mappedBenefitIds.Any())
        {
            Log.Info($"No benefits mapped to nearby active locations.");
            // Optionally LogBenefitAction here
            return;
        }

        var results = ctx.Db.BenefitDefinition.Iter()
            .Where(b => b.IsActive && mappedBenefitIds.Contains(b.BenefitId))
            .Select(b => new { b.BenefitId, b.Name, b.Description, b.Type, b.Cost }) // Example projection
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsNearPoint", $"Queried active benefits near ({latitude}, {longitude}), radius {radiusKm}km. Found {results.Count}.");
        Log.Info($"Query results (for server log demo): Found {results.Count} active benefits within radius.");
    }

    // ====================== Simplified Auth Flow Reducers ======================

    /// <summary>
    /// Initiates registration or login by sending a verification code to the user's email.
    /// Determines intent (register vs login) based on email existence.
    /// </summary>
    [Reducer]
    public static void RequestVerification(ReducerContext ctx, string email, bool isRegistration, string? username = null, int? roleInt = null )
    {
        Identity identity = ctx.Sender;
        email = email.ToLowerInvariant().Trim(); // Normalize email

        if (!IsValidEmail(email))
        {
            throw new Exception("Invalid email format provided.");
        }

        // Determine intent and validate accordingly
        bool isRegistrationAttempt = isRegistration;

        User? existingUserByEmail = ctx.Db.User.Iter().FirstOrDefault(u => u.Email == email);
        User? existingUserById = ctx.Db.User.Identity.Find(identity); // Check if current identity already has a user

        // Prevent logged-in users from initiating verification for a *different* email/account
        if (existingUserById != null && existingUserById.Email != email)
        {
            throw new Exception("Cannot request verification for a different email while logged in.");
        }


        string? validatedUsername = null;

        var validatedRole = (UserRole)roleInt!;

        if (isRegistrationAttempt)
        {
            // --- REGISTRATION VALIDATION ---
            if (existingUserByEmail != null)
            {
                throw new Exception($"Email '{email}' is already registered. Please login instead.");
            }
            if (string.IsNullOrWhiteSpace(username) || roleInt == null)
            {
                throw new Exception("Username and Role are required for registration.");
            }
            if (!IsValidUsername(username))
            {
                throw new Exception("Invalid username format (3-20 chars, letters, numbers, _, -).");
            }
            if (ctx.Db.User.Iter().Any(u => u.Username == username))
            {
                throw new Exception($"Username '{username}' is already taken.");
            }
            if (!Enum.IsDefined(typeof(UserRole), roleInt))
            {
                throw new Exception("Invalid user role specified.");
            }

            validatedUsername = username;
            validatedRole = (UserRole)roleInt;
            LogAuthAction(ctx, "RegistrationVerificationRequested", $"Attempting registration for {email} / {username}");
        }
        else
        {
            // --- LOGIN VALIDATION ---
            if (existingUserByEmail == null)
            {
                throw new Exception($"No account found for email '{email}'. Please register first.");
            }
            LogAuthAction(ctx, "LoginVerificationRequested", $"Attempting login for {email}");
        }

        // --- Generate and Store Code ---
        string verificationCode = GenerateVerificationCode();
        Timestamp expiresAt = ctx.Timestamp + VerificationExpiry;

        // Upsert PendingVerification: Delete existing for this identity, then insert new.
        var existingPending = ctx.Db.PendingVerification.Identity.Find(identity);
        if (existingPending != null)
        {
            ctx.Db.PendingVerification.Delete(existingPending);
        }


        ctx.Db.PendingVerification.Insert(new PendingVerification
        {
            Identity = identity,
            Email = email, // Store normalized email
            Username = validatedUsername, // Null for login attempts
            Role = validatedRole, // Null for login attempts
            VerificationCode = verificationCode,
            ExpiresAt = expiresAt
        });

        // --- Send Email (Conceptual) ---
        // EmailService.SendVerificationEmailAsync(email, verificationCode);
        Log.Info($"Auth: Verification code generated for {email}. Code: {verificationCode} (Expires: {expiresAt})"); // Log code for testing
        // Consider returning a success message, but not the code itself.
    }

    /// <summary>
    /// Verifies the code sent to the user's email to complete registration or login.
    /// </summary>
    [Reducer]
    public static void Verify(ReducerContext ctx, string verificationCode, string? deviceId)
    {
        Identity identity = ctx.Sender;

        // 1. Find Pending Verification
        var pending = ctx.Db.PendingVerification.Identity.Find(identity); // Use Pk

        if (pending == null)
        {
            throw new Exception("No pending verification found. Please request a code first.");
        }

        // 2. Check Expiry
        if (pending.ExpiresAt < ctx.Timestamp)
        {
            ctx.Db.PendingVerification.Delete(pending); // Clean up expired entry
            throw new Exception("Verification code has expired. Please request a new one.");
        }

        // 3. Check Code Match
        if (pending.VerificationCode != verificationCode)
        {
            // Consider adding rate limiting or attempts logic here in a real app
            throw new Exception("Invalid verification code.");
        }

        // 4. Verification successful - Determine action: Register or Login
        User? user = ctx.Db.User.Identity.Find(identity); // Use Pk

        if (user == null)
        {
            // --- COMPLETE REGISTRATION ---

            // Explicitly check for required registration fields from PendingVerification
            // These should have been validated and stored by RequestVerification
            if (string.IsNullOrEmpty(pending.Username) || string.IsNullOrEmpty(pending.Email))
            {
                // This indicates an inconsistent state, likely an issue in RequestVerification
                ctx.Db.PendingVerification.Delete(pending); // Clean up inconsistent state
                Log.Error($"Auth Error: Pending verification data incomplete for registration. Identity: {identity}");
                throw new Exception("Internal error during registration. Please try again or contact support.");
            }

            // Double-check if email/username got taken *after* request but *before* verify (race condition)
            // Use the non-null values checked above
            if (ctx.Db.User.Iter().Any(u => u.Email == pending.Email))
            {
                ctx.Db.PendingVerification.Delete(pending);
                throw new Exception($"Email '{pending.Email}' was registered by someone else. Please try registering again.");
            }

            if (ctx.Db.User.Iter().Any(u => u.Username == pending.Username))
            {
                ctx.Db.PendingVerification.Delete(pending);
                throw new Exception($"Username '{pending.Username}' was taken. Please try registering again with a different username.");
            }

            user = new User
            {
                Identity = identity,
                Username = pending.Username,
                Email = pending.Email,
                Role = pending.Role,
                IsEmailVerified = true,
                RegisteredAt = ctx.Timestamp
            };
            ctx.Db.User.Insert(user);
            LogAuthAction(ctx, "UserRegistrationCompleted", $"User {user.Username} ({user.Email}) completed registration.");

            // Create initial AuthSession
            ctx.Db.AuthSession.Insert(new AuthSession
            {
                Identity = identity,
                LastActiveTime = ctx.Timestamp,
                ActiveDeviceId = deviceId ?? "unknown"
            });
            LogAuthAction(ctx, "SessionCreated", $"Initial session created for {user.Username}. Device: {deviceId ?? "unknown"}");
        }
        else
        {
            // --- COMPLETE LOGIN ---

            // Sanity check: Ensure the email in the pending record matches the logged-in user's email
            // Also check if pending.Email is null, although it shouldn't be for a login verification
            if (string.IsNullOrEmpty(pending.Email) || user.Email != pending.Email)
            {
                // This implies the user somehow requested verification for one email (or no email stored?)
                // while their identity is already linked to a different verified email.
                ctx.Db.PendingVerification.Delete(pending); // Clean up
                Log.Warn($"Auth Mismatch: Verify attempted for email '{pending.Email ?? "NULL"}' but user '{user.Username}' has email '{user.Email}'. Identity: {identity}");
                throw new Exception("Verification email does not match the email associated with your account.");
            }

            LogAuthAction(ctx, "UserLogin", $"User {user.Username} ({user.Email}) logged in.");

            // Upsert AuthSession
            var session = ctx.Db.AuthSession.Identity.Find(identity); // Use Pk
            if (session != null)
            {
                session.LastActiveTime = ctx.Timestamp;
                session.ActiveDeviceId = deviceId ?? "unknown";
                ctx.Db.AuthSession.Identity.Update(session); // Use Update with object
                LogAuthAction(ctx, "SessionUpdated", $"Session updated for {user.Username}. Device: {deviceId ?? "unknown"}");
            }
            else
            {
                ctx.Db.AuthSession.Insert(new AuthSession
                {
                    Identity = identity,
                    LastActiveTime = ctx.Timestamp,
                    ActiveDeviceId = deviceId ?? "unknown"
                });
                LogAuthAction(ctx, "SessionCreated", $"New session created for {user.Username}. Device: {deviceId ?? "unknown"}");
            }
        }

        // 5. Clean up pending verification
        ctx.Db.PendingVerification.Delete(pending);

        // Success - Client state will update via subscriptions to User and AuthSession tables.
        Log.Info($"Auth: Verification successful for Identity {identity}.");
    }

    // ====================== User Management & Other Reducers (Minor Updates) ======================

    [Reducer]
    public static void GetMyProfile(ReducerContext ctx)
    {
        var user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            // User not found, implies not logged in or registered.
            // No action needed server-side, client subscription handles lack of data.
            Log.Warn($"Auth: GetMyProfile called by unauthenticated Identity {ctx.Sender}.");
            return; // Or throw if an error response is preferred client-side
        }
        // Data is available via client subscription to the User table.
        // Log the access attempt if desired.
        LogAuthAction(ctx, "GetMyProfile", $"User {user.Username} requested profile data.");
    }

    [Reducer]
    public static void UpdateProfile(ReducerContext ctx, string? newUsername, string? newEmail) // Allow updating either or both
    {
        Identity identity = ctx.Sender;
        var user = ctx.Db.User.Identity.Find(identity);

        if (user == null)
        {
            throw new Exception("Not authenticated. Please login first.");
        }

        bool changed = false;
        newEmail = newEmail?.ToLowerInvariant().Trim();

        // --- Update Username ---
        if (!string.IsNullOrWhiteSpace(newUsername) && user.Username != newUsername)
        {
            if (!IsValidUsername(newUsername))
            {
                throw new Exception("Invalid username format.");
            }
            // Check if NEW username is taken by ANY OTHER user
            if (ctx.Db.User.Iter().Any(u => u.Identity != identity && u.Username == newUsername))
            {
                throw new Exception($"Username '{newUsername}' is already taken.");
            }
            user.Username = newUsername;
            changed = true;
            LogAuthAction(ctx, "UserProfileUpdated", $"Username changed to {newUsername}.");
        }

        // --- Update Email ---
        if (!string.IsNullOrWhiteSpace(newEmail) && user.Email != newEmail)
        {
            if (!IsValidEmail(newEmail))
            {
                throw new Exception("Invalid email format.");
            }
            // Check if NEW email is taken by ANY OTHER user
            if (ctx.Db.User.Iter().Any(u => u.Identity != identity && u.Email == newEmail))
            {
                throw new Exception($"Email '{newEmail}' is already registered to another account.");
            }
            user.Email = newEmail;
            user.IsEmailVerified = false; // Require re-verification for new email
            changed = true;
            LogAuthAction(ctx, "UserProfileUpdated", $"Email changed to {newEmail}. Verification required.");
            // TODO: Trigger a new verification email process for the new email?
            // Could call RequestVerification internally, or instruct user to do so.
            // For simplicity here, we just mark as unverified.
        }


        if (changed)
        {
            ctx.Db.User.Identity.Update(user);
            Log.Info($"Auth: Profile updated for Identity {identity}.");
        }
        else
        {
            Log.Info($"Auth: UpdateProfile called for Identity {identity}, but no changes were made.");
        }
    }

    [Reducer]
    public static void ListUsersByRole(ReducerContext ctx, int roleInt)
    {
        var callingUser = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk

        if (callingUser == null)
            throw new Exception("Not authenticated.");

        // Example: Use specific roles for authorization
        if (callingUser.Role != UserRole.Auditor && callingUser.Role != UserRole.Auditor)
            throw new Exception("Access denied. Only Auditors or Auditors can list users.");

        if (!Enum.IsDefined(typeof(UserRole), roleInt))
            throw new Exception("Invalid role specified.");

        var role = (UserRole)roleInt;

        // The actual listing happens via client subscription + filtering.
        // This reducer mainly serves as an authorization gate and logs the action.
        var userList = ctx.Db.User.Iter().Where(u => u.Role == role).Select(u => u.Username).ToList(); // Example server-side logic if needed

        LogAuthAction(ctx, "UsersListedByRole", $"User {callingUser.Username} listed users with role {role}. Found: {userList.Count}");
    }

    [Reducer]
    public static void GetAuditLogs(ReducerContext ctx, Timestamp startTime, Timestamp endTime, int limit) // Use Timestamp type
    {
        var callingUser = ctx.Db.User.Identity.Find(ctx.Sender); // Use Pk

        if (callingUser == null)
            throw new Exception("Not authenticated.");

        if (callingUser.Role != UserRole.Auditor) // Only Auditors
            throw new Exception("Access denied. Only auditors can access audit logs.");

        if (limit <= 0 || limit > 1000)
        {
            // Clamp limit or throw error
            limit = Math.Clamp(limit, 1, 1000);
            Log.Warn($"Auth: Audit log limit adjusted to {limit}.");
            // Alternatively: throw new Exception("Limit must be between 1 and 1000.");
        }

        // Actual log retrieval happens via client subscription + filtering on AuditLog table.
        // This reducer logs the access attempt.
        var logsInRange = ctx.Db.AuditLog.Iter()
            .Where(log => log.Timestamp.CompareTo(startTime) >= 0 && log.Timestamp.CompareTo(endTime) <= 0)
            .OrderByDescending(log => log.Timestamp) // Example ordering
            .Take(limit)
            .ToList();


        LogAuthAction(ctx, "AuditLogsAccessed", $"Auditor {callingUser.Username} accessed audit logs from {startTime} to {endTime} (Limit: {limit}). Found: {logsInRange.Count}");
    }
}
}
