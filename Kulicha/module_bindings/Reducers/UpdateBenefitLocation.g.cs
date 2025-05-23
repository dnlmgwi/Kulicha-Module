// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteReducers : RemoteBase
    {
        public delegate void UpdateBenefitLocationHandler(ReducerEventContext ctx, long locationId, string name, string city, string? region, string? address, double latitude, double longitude, double serviceRadiusKm, bool isActive);
        public event UpdateBenefitLocationHandler? OnUpdateBenefitLocation;

        public void UpdateBenefitLocation(long locationId, string name, string city, string? region, string? address, double latitude, double longitude, double serviceRadiusKm, bool isActive)
        {
            conn.InternalCallReducer(new Reducer.UpdateBenefitLocation(locationId, name, city, region, address, latitude, longitude, serviceRadiusKm, isActive), this.SetCallReducerFlags.UpdateBenefitLocationFlags);
        }

        public bool InvokeUpdateBenefitLocation(ReducerEventContext ctx, Reducer.UpdateBenefitLocation args)
        {
            if (OnUpdateBenefitLocation == null) return false;
            OnUpdateBenefitLocation(
                ctx,
                args.LocationId,
                args.Name,
                args.City,
                args.Region,
                args.Address,
                args.Latitude,
                args.Longitude,
                args.ServiceRadiusKm,
                args.IsActive
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class UpdateBenefitLocation : Reducer, IReducerArgs
        {
            [DataMember(Name = "locationId")]
            public long LocationId;
            [DataMember(Name = "name")]
            public string Name;
            [DataMember(Name = "city")]
            public string City;
            [DataMember(Name = "region")]
            public string? Region;
            [DataMember(Name = "address")]
            public string? Address;
            [DataMember(Name = "latitude")]
            public double Latitude;
            [DataMember(Name = "longitude")]
            public double Longitude;
            [DataMember(Name = "serviceRadiusKm")]
            public double ServiceRadiusKm;
            [DataMember(Name = "isActive")]
            public bool IsActive;

            public UpdateBenefitLocation(
                long LocationId,
                string Name,
                string City,
                string? Region,
                string? Address,
                double Latitude,
                double Longitude,
                double ServiceRadiusKm,
                bool IsActive
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
            }

            public UpdateBenefitLocation()
            {
                this.Name = "";
                this.City = "";
            }

            string IReducerArgs.ReducerName => "UpdateBenefitLocation";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags UpdateBenefitLocationFlags;
        public void UpdateBenefitLocation(CallReducerFlags flags) => UpdateBenefitLocationFlags = flags;
    }
}
