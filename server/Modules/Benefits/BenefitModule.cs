using SpacetimeDB;

namespace Server.Modules.Benefits {
using Enums;
public static partial class BenefitModule {
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
            LogBenefitAction(ctx, user!.Identity.ToString(), "Privileged access", $"User {user.Username} ({user.Role}) attempted action requiring roles: {string.Join(", ", requiredRoles)}.");
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
}
}
