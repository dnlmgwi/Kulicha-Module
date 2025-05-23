// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class BenefitLocation
    {
        [DataMember(Name = "LocationId")]
        public long LocationId;
        [DataMember(Name = "Name")]
        public string? Name;
        [DataMember(Name = "City")]
        public string? City;
        [DataMember(Name = "Region")]
        public string? Region;
        [DataMember(Name = "Address")]
        public string? Address;
        [DataMember(Name = "Latitude")]
        public double Latitude;
        [DataMember(Name = "Longitude")]
        public double Longitude;
        [DataMember(Name = "ServiceRadiusKm")]
        public double ServiceRadiusKm;
        [DataMember(Name = "IsActive")]
        public bool IsActive;
        [DataMember(Name = "CreatedAt")]
        public SpacetimeDB.Timestamp CreatedAt;
        [DataMember(Name = "UpdatedAt")]
        public SpacetimeDB.Timestamp? UpdatedAt;

        public BenefitLocation(
            long LocationId,
            string? Name,
            string? City,
            string? Region,
            string? Address,
            double Latitude,
            double Longitude,
            double ServiceRadiusKm,
            bool IsActive,
            SpacetimeDB.Timestamp CreatedAt,
            SpacetimeDB.Timestamp? UpdatedAt
        )
        {
            this.LocationId = LocationId;
            this.Name = Name;
            this.City = City;
            this.Region = Region;
            this.Address = Address;
            this.Latitude = Latitude;
            this.Longitude = Longitude;
            this.ServiceRadiusKm = ServiceRadiusKm;
            this.IsActive = IsActive;
            this.CreatedAt = CreatedAt;
            this.UpdatedAt = UpdatedAt;
        }

        public BenefitLocation()
        {
        }
    }
}
