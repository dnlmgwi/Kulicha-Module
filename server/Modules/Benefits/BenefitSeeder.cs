namespace ServerModule.Modules.Benefits {
using Enums;
using SpacetimeDB;

public static class BenefitSeeder {
    // --- Seeding Configuration ---
    private const int NumLocationsToCreate = 20; // How many locations (e.g., 10 across Malawi)
    private const int NumBenefitsToCreate = 200; // Target number of benefits
    private const int MinMappingsPerBenefit = 5; // Each benefit exists at least at 1 location
    private const int MaxMappingsPerBenefit = 6; // Each benefit exists at most at 4 locations (or total locations, whichever is smaller)
    private const double ProbBenefitIsActive = 0.7; // 90% chance a benefit is active
    private const double ProbLocationIsActive = 0.95; // 95% chance a location is active
    private const decimal ProbBenefitIsFree = 0.75m; // 75% chance a benefit is free

    private static readonly Random Rng = new Random();

    private static readonly (string City, string Region, double Lat, double Lon)[] MalawiCities =
    [
        ("Lilongwe", "Central Region", -13.96, 33.77), ("Blantyre", "Southern Region", -15.81, 35.05),
        ("Mzuzu", "Northern Region", -11.46, 34.01), ("Zomba", "Southern Region", -15.38, 35.32),
        ("Kasungu", "Central Region", -13.03, 33.48), ("Mangochi", "Southern Region", -14.47, 35.26),
        ("Karonga", "Northern Region", -9.93, 33.93), ("Salima", "Central Region", -13.78, 34.43),
        ("Nkhotakota", "Central Region", -12.92, 34.29), ("Liwonde", "Southern Region", -15.06, 35.22)
    ];

    private static readonly string[] LocationNamePrefixes = ["Youth", "Community", "District", "Regional", "Mobile", "Central", "Southern", "Northern"];
    private static readonly string[] LocationNameSuffixes = ["Center", "Hub", "Drop-in", "Shelter", "Clinic", "Office", "Outreach", "Skills Point", "Support Base"];
    private static readonly string[] AddressLandmarks = ["Market", "Hall", "Main Rd", "Bus Depot", "Hospital", "School", "Church", "Bridge", "Borehole", "Trading Ctr"];

    private static readonly string[] BenefitNameAdjectives = ["Basic", "Emergency", "Daily", "Weekly", "Monthly", "Comprehensive", "Introductory", "Advanced", "Nutritious", "Safe", "Urgent", "General"];
    private static readonly string[] BenefitNameNouns = ["Shelter", "Meal", "Health Check", "First Aid", "Counseling", "Skills Training", "Legal Aid", "Job Search", "Hygiene Pack", "Transport Voucher", "Education Support", "Clothing Aid"];
    private static readonly string[] ProviderOrgTypes = ["Support Network", "Community Initiative", "Health Unit", "Dev Project", "Youth Org", "Relief Service", "Govt Dept", "NGO Partner"];

    // --- Helper Methods ---
    private static T GetRandomItem<T>(IReadOnlyList<T> items) => (items.Any() ? items[Rng.Next(items.Count)] : default)!;
    private static bool GetRandomBool(double probabilityOfTrue) => Rng.NextDouble() < probabilityOfTrue;
    private static double GetRandomCoordinateOffset(double maxOffset = 0.05) => (Rng.NextDouble() - 0.5) * 2 * maxOffset;
    private static double GetRandomCost() => GetRandomBool((double)ProbBenefitIsFree) ? 0.00 : Rng.Next(5, 200) * 10; // Random cost, e.g., 50 to 2000 MWK, or 0
    private static string GeneratePolicy() => $"{(GetRandomBool(0.6) ? "Appointment needed. " : "")}Hours: {Rng.Next(8, 11)}:00-{(GetRandomBool(0.5) ? Rng.Next(12, 14) : Rng.Next(15, 17))}:00. {(GetRandomBool(0.4) ? "Bring ID." : "Call first.")}";

    public static void SeedInitialData(ReducerContext ctx)
    {
        var now = ctx.Timestamp;
        var createdLocations = new List<BenefitLocation>();
        var createdBenefits = new List<BenefitDefinition>();

        Log.Info($"Generating {NumLocationsToCreate} locations...");
        for (int i = 0; i < NumLocationsToCreate; i++)
        {
            var cityInfo = GetRandomItem(MalawiCities);
            if (cityInfo == default)
            {
                Log.Warn("MalawiCities list is empty, cannot create location.");
                continue;
            }

            var name = $"{GetRandomItem(LocationNamePrefixes)} {cityInfo.City} {GetRandomItem(LocationNameSuffixes)}";

            if (createdLocations.Any(l => l.Name == name)) name = $"{name} #{i + 1}";

            var location = ctx.Db.BenefitLocation.Insert(new BenefitLocation
            {
                Name = name,
                City = cityInfo.City,
                Region = cityInfo.Region,
                Address = $"Near {GetRandomItem(AddressLandmarks)}, Area {Rng.Next(1, 50)}",
                Latitude = cityInfo.Lat + GetRandomCoordinateOffset(),
                Longitude = cityInfo.Lon + GetRandomCoordinateOffset(),
                ServiceRadiusKm = Rng.Next(5, 41), // 5km to 40km radius
                IsActive = GetRandomBool(ProbLocationIsActive),
                CreatedAt = now
            });
            createdLocations.Add(location);
            Log.Debug($"Created Location ID: {location.LocationId} - {location.Name}");
        }

        if (!createdLocations.Any())
        {
            Log.Error("No locations were created. Cannot seed benefits or mappings. Aborting seed.");
            // Throw an exception or return to signal failure
            throw new InvalidOperationException("Failed to create any BenefitLocations during seeding.");
        }
        Log.Info($"Successfully created {createdLocations.Count} locations.");

        Log.Info($"Generating {NumBenefitsToCreate} benefit definitions...");
        var benefitTypes = (BenefitType[])Enum.GetValues(typeof(BenefitType));
        if (!benefitTypes.Any())
        {
            Log.Error("BenefitType enum has no values. Cannot seed benefits.");
            throw new InvalidOperationException("BenefitType enum is empty.");
        }

        for (int i = 0; i < NumBenefitsToCreate; i++)
        {
            var benefitType = GetRandomItem(benefitTypes);
            var name = $"{GetRandomItem(BenefitNameAdjectives)} {GetRandomItem(BenefitNameNouns)}";
            name = createdBenefits.Any(b => b.Name == name && b.Type == benefitType) ? $"{name} ({benefitType}) #{i + 1}" : $"{name} ({benefitType})"; // Add type to name for more variation

            var assignedLocationForFk = GetRandomItem(createdLocations);

            var benefit = ctx.Db.BenefitDefinition.Insert(new BenefitDefinition
            {
                Name = name,
                Description = $"Provides {name.ToLowerInvariant()}.",
                Type = benefitType,
                Cost = GetRandomCost(),
                Provider = $"{assignedLocationForFk.Region} {GetRandomItem(ProviderOrgTypes)}",
                PolicyDetails = GeneratePolicy(),
                IsActive = GetRandomBool(ProbBenefitIsActive),
                LocationId = assignedLocationForFk.LocationId, // Satisfy FK
                CreatedAt = now
            });
            createdBenefits.Add(benefit);
            Log.Debug($"Created Benefit ID: {benefit.BenefitId} - {benefit.Name}");
        }
        Log.Info($"Successfully created {createdBenefits.Count} benefits.");

        Log.Info("Mapping benefits to locations...");
        int totalMappings = 0;
        foreach (var benefit in createdBenefits)
        {
            // Ensure MaxMappingsPerBenefit is not greater than the number of locations available
            int maxPossibleMappings = Math.Min(MaxMappingsPerBenefit, createdLocations.Count);
            // Ensure MinMappingsPerBenefit is not greater than maxPossibleMappings
            int minActualMappings = Math.Min(MinMappingsPerBenefit, maxPossibleMappings);

            // Determine how many locations offer this benefit, ensuring it's within the valid range
            int numMappings = Rng.Next(minActualMappings, maxPossibleMappings + 1);

            // Select unique locations randomly
            var locationsForThisBenefit = createdLocations
                .OrderBy(_ => Rng.Next()) // Shuffle
                .Take(numMappings) // Take the desired number
                .ToList();

            foreach (var location in locationsForThisBenefit)
            {
                ctx.Db.BenefitLocationMap.Insert(new BenefitLocationMap
                {
                    BenefitId = benefit.BenefitId,
                    LocationId = location.LocationId,
                    AddedAt = now
                });
                totalMappings++;
                Log.Debug($"Mapped Benefit '{benefit.Name}' ({benefit.BenefitId}) -> Location '{location.Name}' ({location.LocationId})");
            }
        }
        Log.Info($"Successfully created {totalMappings} benefit-location mappings.");
    }
}
}
