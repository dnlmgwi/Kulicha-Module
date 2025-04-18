namespace StdbModule.Modules.Benefits {
using Enums;
using SpacetimeDB;

public static partial class BenefitModule {
    private const double EarthRadiusKm = 6371.0; // For distance calculation

    /// <summary>
    /// Checks if the calling user has one of the required roles.
    /// </summary>
    private static bool CheckRole(ReducerContext ctx, out User? user, params UserRole[] requiredRoles)
    {
        user = ctx.Db.User.Identity.Find(ctx.Sender);
        if (user == null)
        {
            Log.Warn($"BenefitModule: Unauthenticated access attempt by {ctx.Sender}.");
            return false; // Not authenticated
        }
        if (!requiredRoles.Contains(user.Role))
        {
            Log.Warn($"BenefitModule: User {user.Username} ({user.Role}) attempted action requiring roles: {string.Join(", ", requiredRoles)}.");
            return false; // Incorrect role
        }
        return true;
    }

    /// Convenience overload for checking if simply authenticated (any role)
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

    // --- Distance Calculation ---

    /// <summary>
    /// Calculates the distance between two lat/lon points using the Haversine formula.
    /// </summary>
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

    // --- Logging (Optional - could reuse AuthModule's LogInfo if desired) ---
    private static void LogBenefitAction(ReducerContext ctx, string identityStr, string action, string details)
    {
        // Using SpacetimeDB's built-in Log for simplicity here
        Log.Info($"Benefit Action by {identityStr}: {action} - {details}");
        // Alternatively, integrate with AuthModule's AuditLog if available and appropriate
        ctx.Db.AuditLog.Insert(new AuditLog { Identity = ctx.Sender, Action = $"Benefit_{action}", Details = details, Timestamp = ctx.Timestamp });
    }

    /// <summary>
    /// Initialize the database with proper hex validation
    /// </summary>
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
                // Call the seeding function
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
    
    // ====================== Benefit Location Reducers (CRUD) ======================

    [Reducer]
    public static void CreateBenefitLocation(ReducerContext ctx, string name, string city, string region, string address, double latitude, double longitude, double serviceRadiusKm, bool isActive)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor)) // Example roles
        {
            throw new Exception("Permission denied: Only Auditors and Benefactors can create locations.");
        }

        // Basic Input Validation (add more as needed)
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(city) || serviceRadiusKm <= 0)
        {
            throw new Exception("Invalid input: Name, City cannot be empty, and Service Radius must be positive.");
        }
        // Add validation for lat/lon ranges if necessary

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
        // No return needed, success implies completion. Client might re-query.
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

        // Basic Input Validation
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(city) || serviceRadiusKm <= 0)
        {
            throw new Exception("Invalid input: Name, City cannot be empty, and Service Radius must be positive.");
        }

        location.Name = name;
        location.City = city;
        location.Region = region ?? location.Region; // Keep old value if null passed
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
        if (!CheckRole(ctx, out var user, UserRole.Auditor)) // Example: Only Auditor can delete
        {
            throw new Exception("Permission denied: Only Auditors can delete locations.");
        }

        var location = ctx.Db.BenefitLocation.LocationId.Find(locationId);
        if (location == null)
        {
            throw new Exception($"Benefit Location with ID {locationId} not found.");
        }
        
        // 1. Check if any BenefitDefinition uses this as its primary LocationId FK
        bool isPrimaryFk = ctx.Db.BenefitDefinition.Iter().Any(bd => bd.LocationId == locationId);
        if (isPrimaryFk)
        {
            throw new Exception($"Cannot delete location {locationId}: It is referenced as the primary location by one or more Benefit Definitions.");
        }

        // 2. Check if any mappings exist in BenefitLocationMap
        var dependentMaps = ctx.Db.BenefitLocationMap.Iter().Where(m => m.LocationId == locationId).ToList();
        if (dependentMaps.Any())
        {
            throw new Exception($"Cannot delete location {locationId}: It is mapped to {dependentMaps.Count} benefits. Unmap them first.");
        }

        // Proceed with deletion
        ctx.Db.BenefitLocation.LocationId.Delete(locationId);
        LogBenefitAction(ctx, user!.Identity.ToString(), "LocationDeleted", $"Deleted Location ID: {locationId} - {location.Name}");
    }

    // Note: GetById/GetAll are typically handled by client-side subscriptions or specific query reducers below.

    // ====================== Benefit Definition Reducers (CRUD) ======================

    [Reducer]
    public static void CreateBenefitDefinition(ReducerContext ctx, string name, string description, int typeInt, long cost, string provider, string policyDetails, bool isActive, long primaryLocationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can create benefit definitions.");
        }

        // Validate BenefitType
        if (!Enum.IsDefined(typeof(BenefitType), typeInt))
        {
            throw new Exception($"Invalid BenefitType value: {typeInt}.");
        }
        var benefitType = (BenefitType)typeInt;

        // Validate primaryLocationId exists
        if (ctx.Db.BenefitLocation.LocationId.Find(primaryLocationId) == null)
        {
            throw new Exception($"Primary Location ID {primaryLocationId} does not exist.");
        }

        // Basic Input Validation
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

        // --- Auto-Map to Primary Location ---
        // Optionally, automatically create the map entry for the primary location
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

        var benefit = ctx.Db.BenefitDefinition.BenefitId.Find(benefitId);
        if (benefit == null)
        {
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        }

        // Validate BenefitType
        if (!Enum.IsDefined(typeof(BenefitType), typeInt))
        {
            throw new Exception($"Invalid BenefitType value: {typeInt}.");
        }
        var benefitType = (BenefitType)typeInt;

        // Validate primaryLocationId exists
        if (ctx.Db.BenefitLocation.LocationId.Find(primaryLocationId) == null)
        {
            throw new Exception($"Primary Location ID {primaryLocationId} does not exist.");
        }

        // Basic Input Validation
        if (string.IsNullOrWhiteSpace(name) || cost < 0)
        {
            throw new Exception("Invalid input: Name cannot be empty, and Cost cannot be negative.");
        }

        benefit.Name = name;
        benefit.Description = description ?? benefit.Description;
        benefit.Type = benefitType;
        benefit.Cost = cost;
        benefit.Provider = provider ?? benefit.Provider;
        benefit.PolicyDetails = policyDetails ?? benefit.PolicyDetails;
        benefit.IsActive = isActive;
        benefit.LocationId = primaryLocationId; // Update the FK if needed
        benefit.UpdatedAt = ctx.Timestamp;

        ctx.Db.BenefitDefinition.BenefitId.Update(benefit);
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitUpdated", $"Updated Benefit ID: {benefit.BenefitId} - {benefit.Name}");
    }

    [Reducer]
    public static void DeleteBenefitDefinition(ReducerContext ctx, long benefitId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor)) // Example: Only Auditor can delete
        {
            throw new Exception("Permission denied: Only Auditors can delete benefit definitions.");
        }

        var benefit = ctx.Db.BenefitDefinition.BenefitId.Find(benefitId);
        if (benefit == null)
        {
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        }

        // --- Dependency Check & Cleanup ---
        // Delete all associated entries in BenefitLocationMap first
        var dependentMaps = ctx.Db.BenefitLocationMap.Iter().Where(m => m.BenefitId == benefitId).ToList();
        if (dependentMaps.Any())
        {
            Log.Info($"Deleting {dependentMaps.Count} location mappings associated with benefit {benefitId} before deleting the definition.");
            foreach (var map in dependentMaps)
            {
                ctx.Db.BenefitLocationMap.Delete(map); // Assumes map object is sufficient for delete
            }
        }

        // Proceed with deletion
        ctx.Db.BenefitDefinition.BenefitId.Delete(benefitId);
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitDeleted", $"Deleted Benefit ID: {benefitId} - {benefit.Name}");
    }

    // ====================== Benefit Mapping Reducers ======================

    [Reducer]
    public static void MapBenefitToLocation(ReducerContext ctx, long benefitId, long locationId)
    {
        if (!CheckRole(ctx, out var user, UserRole.Auditor, UserRole.Benefactor))
        {
            throw new Exception("Permission denied: Only Auditors or Benefactor can manage benefit mappings.");
        }

        // Check if benefit and location exist
        if (ctx.Db.BenefitDefinition.BenefitId.Find(benefitId) == null)
            throw new Exception($"Benefit Definition with ID {benefitId} not found.");
        if (ctx.Db.BenefitLocation.LocationId.Find(locationId) == null)
            throw new Exception($"Benefit Location with ID {locationId} not found.");

        // Check if mapping already exists (important to prevent duplicates)
        // Note: This requires iterating if no unique index/PK on (BenefitId, LocationId)
        bool mappingExists = ctx.Db.BenefitLocationMap.Iter()
            .Any(m => m.BenefitId == benefitId && m.LocationId == locationId);
        if (mappingExists)
        {
            Log.Warn($"Benefit {benefitId} is already mapped to Location {locationId}. Skipping.");
            return; // Or throw an exception if desired
        }

        // Create the mapping
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

        // Find the specific map entry to delete
        // Note: This is inefficient without proper indexing/PK on the map table.
        var mapToDelete = ctx.Db.BenefitLocationMap.Iter()
            .FirstOrDefault(m => m.BenefitId == benefitId && m.LocationId == locationId);

        if (mapToDelete == null)
        {
            throw new Exception($"Mapping between Benefit {benefitId} and Location {locationId} not found.");
        }

        ctx.Db.BenefitLocationMap.Delete(mapToDelete); // Assumes mapToDelete is sufficient for deletion
        LogBenefitAction(ctx, user!.Identity.ToString(), "BenefitUnmapped", $"Unmapped Benefit {benefitId} from Location {locationId}");
    }


    // ====================== Query & Filtering Reducers ======================

    // NOTE: For queries returning data, SpacetimeDB typically relies on client-side
    // subscriptions to tables. Reducers modify state. If you need server-side filtering
    // *before* data reaches the client (e.g., for security or large datasets),
    // you might use reducers that populate specific "QueryResult" tables or similar patterns,
    // or rely on SpacetimeDB's future query capabilities if they evolve.
    // The examples below *demonstrate the filtering logic* but don't explicitly "return"
    // data in the typical web API sense. The client would subscribe to the relevant tables
    // and perform filtering, or subscribe to pre-filtered tables if designed that way.

    // Example: Get ACTIVE Benefit Definitions of a specific type (Logic Demo)
    // A real implementation might populate a user-specific view table.
    [Reducer]
    public static void QueryActiveBenefitsByType(ReducerContext ctx, int typeInt)
    {
        if (!IsAuthenticated(ctx, out var user)) // Anyone logged in can query
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
            .Select(b => new { b.BenefitId, b.Name, b.Description }) // Select desired fields
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsByType", $"Queried active benefits of type {benefitType}. Found {results.Count}.");
        // In a real STDB app, you wouldn't return 'results' directly here.
        // The client would subscribe to BenefitDefinition and filter, or you'd write
        // results to another table the client subscribes to.
        Log.Info($"Query results (for demo): Found {results.Count} active {benefitType} benefits.");
    }

    // Example: Get ACTIVE Benefits available at a specific Location (Logic Demo)
    [Reducer]
    public static void QueryActiveBenefitsAtLocation(ReducerContext ctx, long locationId)
    {
        if (!IsAuthenticated(ctx, out var user))
        {
            throw new Exception("Authentication required.");
        }

        // Check if location exists and is active
        var location = ctx.Db.BenefitLocation.LocationId.Find(locationId);
        if (location == null || !location.IsActive)
        {
            Log.Warn($"Query attempted for non-existent or inactive location ID: {locationId}");
            // Handle appropriately - maybe return empty result indication
            return;
        }

        // Find benefit IDs mapped to this location
        var mappedBenefitIds = ctx.Db.BenefitLocationMap.Iter()
            .Where(m => m.LocationId == locationId)
            .Select(m => m.BenefitId)
            .ToHashSet(); // Use HashSet for efficient lookup

        if (!mappedBenefitIds.Any())
        {
            Log.Info($"No benefits mapped to location {locationId}.");
            return;
        }

        // Find the actual benefit definitions that are active
        var results = ctx.Db.BenefitDefinition.Iter()
            .Where(b => b.IsActive && mappedBenefitIds.Contains(b.BenefitId))
            .Select(b => new { b.BenefitId, b.Name, b.Description, b.Type }) // Select desired fields
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsAtLocation", $"Queried active benefits at Location {locationId}. Found {results.Count}.");
        Log.Info($"Query results (for demo): Found {results.Count} active benefits at location {locationId}.");
    }


    /// <summary>
    /// Finds active benefits offered by active locations within a specified radius. (Logic Demo)
    /// </summary>
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

        // 1. Find active locations within the radius
        var nearbyLocationIds = ctx.Db.BenefitLocation.Iter()
            .Where(loc => loc.IsActive && CalculateDistanceKm(latitude, longitude, loc.Latitude, loc.Longitude) <= radiusKm)
            .Select(loc => loc.LocationId)
            .ToHashSet();

        if (!nearbyLocationIds.Any())
        {
            Log.Info($"No active locations found within {radiusKm}km of ({latitude}, {longitude}).");
            return;
        }

        // 2. Find benefit IDs mapped to these nearby locations
        var mappedBenefitIds = ctx.Db.BenefitLocationMap.Iter()
            .Where(m => nearbyLocationIds.Contains(m.LocationId))
            .Select(m => m.BenefitId)
            .ToHashSet();

        if (!mappedBenefitIds.Any())
        {
            Log.Info($"No benefits mapped to nearby locations.");
            return;
        }

        // 3. Find the actual benefit definitions that are active
        var results = ctx.Db.BenefitDefinition.Iter()
            .Where(b => b.IsActive && mappedBenefitIds.Contains(b.BenefitId))
            .Select(b => new { b.BenefitId, b.Name, b.Description, b.Type, b.Cost }) // Select desired fields
            .ToList();

        LogBenefitAction(ctx, user!.Identity.ToString(), "QueryBenefitsNearPoint", $"Queried active benefits near ({latitude}, {longitude}), radius {radiusKm}km. Found {results.Count}.");
        Log.Info($"Query results (for demo): Found {results.Count} active benefits within radius.");
    }
}
}
