namespace StdbModule.Modules.Benefits {
using Enums;
using SpacetimeDB;

public static partial class BenefitModule {
    /// <summary>
    /// Initialize the database with proper hex validation
    /// </summary>
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info($"Initializing...");
        var benefitLocations = ctx.Db.BenefitLocation.Iter();
        // Check if Benefit Locations already exist to prevent duplicate seeding
        if (!benefitLocations.Any())
        {
            Log.Info("Database appears empty. Seeding initial data...");

            var locationLilongwe = ctx.Db.BenefitLocation.Insert(new BenefitLocation
            {
                Name = "Lilongwe Youth Drop-in Center",
                City = "Lilongwe",
                Region = "Central Region",
                Address = "Area 25, Near Community Hall",
                Latitude = -13.9626, // Example coordinates
                Longitude = 33.7743, // Example coordinates
                ServiceRadiusKm = 10.0,
                IsActive = true,
                CreatedAt = new Timestamp() // Use current SpacetimeDB time
                // UpdatedAt is nullable, defaults to null
            });

            Log.Info($"Created Location ID: {locationLilongwe.LocationId} - {locationLilongwe.Name}");


            var locationBlantyre = ctx.Db.BenefitLocation.Insert(new BenefitLocation
            {
                Name = "Blantyre Shelter & Skills Hub",
                City = "Blantyre",
                Region = "Southern Region",
                Address = "Limbe, Off Main Road",
                Latitude = -15.8129, // Example coordinates
                Longitude = 35.0587, // Example coordinates
                ServiceRadiusKm = 15.0,
                IsActive = true,
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Location ID: {locationBlantyre.LocationId} - {locationBlantyre.Name}");

            var locationMzuzu = ctx.Db.BenefitLocation.Insert(new BenefitLocation
            {
                Name = "Mzuzu Mobile Outreach Point",
                City = "Mzuzu",
                Region = "Northern Region",
                Address = "Various points, main office Katoto",
                Latitude = -11.4619, // Example coordinates
                Longitude = 34.0199, // Example coordinates
                ServiceRadiusKm = 25.0, // Larger radius for mobile unit
                IsActive = true,
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Location ID: {locationMzuzu.LocationId} - {locationMzuzu.Name}");

            var benefitShelter = ctx.Db.BenefitDefinition.Insert(new BenefitDefinition
            {
                Name = "Emergency Overnight Shelter",
                Description = "Safe temporary overnight accommodation for youth.",
                Type = BenefitType.Housing, // Assumes BenefitType enum exists
                Cost = 0.00m, // Free
                Provider = "Malawi Youth Support Network",
                PolicyDetails = "Available nightly 7 PM - 7 AM. Intake required.",
                IsActive = true,
                LocationId = locationLilongwe.LocationId, // Assigning first mapped location ID
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Benefit ID: {benefitShelter.BenefitId} - {benefitShelter.Name}");

            var benefitFood = ctx.Db.BenefitDefinition.Insert(new BenefitDefinition
            {
                Name = "Daily Hot Meal Program",
                Description = "Provides one nutritious hot meal daily.",
                Type = BenefitType.Food,
                Cost = 0.00m,
                Provider = "Community Food Initiative",
                PolicyDetails = "Served daily 12 PM - 1:30 PM.",
                IsActive = true,
                LocationId = locationBlantyre.LocationId, // Assigning first mapped location ID
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Benefit ID: {benefitFood.BenefitId} - {benefitFood.Name}");

            var benefitHealth = ctx.Db.BenefitDefinition.Insert(new BenefitDefinition
            {
                Name = "Basic Health Check & First Aid",
                Description = "Mobile clinic offering basic health screening and first aid.",
                Type = BenefitType.Healthcare,
                Cost = 0.00m,
                Provider = "District Mobile Health Unit",
                PolicyDetails = "Available weekly at designated points. Check schedule.",
                IsActive = true,
                LocationId = locationMzuzu.LocationId, // Assigning first mapped location ID
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Benefit ID: {benefitHealth.BenefitId} - {benefitHealth.Name}");

            var benefitCounseling = ctx.Db.BenefitDefinition.Insert(new BenefitDefinition
            {
                Name = "Youth Counseling Services",
                Description = "Individual and group counseling sessions.",
                Type = BenefitType.SupportServices, // Assuming this enum value exists
                Cost = 0.00m,
                Provider = "Malawi Youth Support Network",
                PolicyDetails = "By appointment or during drop-in hours.",
                IsActive = true,
                LocationId = locationLilongwe.LocationId, // Assigning first mapped location ID
                CreatedAt = new Timestamp()
            });

            Log.Info($"Created Benefit ID: {benefitCounseling.BenefitId} - {benefitCounseling.Name}");

            ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
            {
                BenefitId = benefitShelter.BenefitId,
                LocationId = locationLilongwe.LocationId,
                AddedAt = new Timestamp()
            });

            ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
            {
                BenefitId = benefitFood.BenefitId,
                LocationId = locationBlantyre.LocationId,
                AddedAt = new Timestamp()
            });

            ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
            {
                BenefitId = benefitHealth.BenefitId,
                LocationId = locationMzuzu.LocationId,
                AddedAt = new Timestamp()
            });

            ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
            {
                BenefitId = benefitCounseling.BenefitId,
                LocationId = locationLilongwe.LocationId,
                AddedAt = new Timestamp()
            });

            ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
            {
                BenefitId = benefitCounseling.BenefitId,
                LocationId = locationBlantyre.LocationId,
                AddedAt = new Timestamp()
            });

            Log.Info("Finished seeding benefit and location data.");
        }
        else
        {
            Log.Info("BenefitLocation table is not empty. Skipping initial data seeding.");
        }
        Log.Info("Benefit module initialized successfully");
    }
}
}
