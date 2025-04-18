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
        public delegate void QueryActiveBenefitsNearPointHandler(ReducerEventContext ctx, double latitude, double longitude, double radiusKm);
        public event QueryActiveBenefitsNearPointHandler? OnQueryActiveBenefitsNearPoint;

        public void QueryActiveBenefitsNearPoint(double latitude, double longitude, double radiusKm)
        {
            conn.InternalCallReducer(new Reducer.QueryActiveBenefitsNearPoint(latitude, longitude, radiusKm), this.SetCallReducerFlags.QueryActiveBenefitsNearPointFlags);
        }

        public bool InvokeQueryActiveBenefitsNearPoint(ReducerEventContext ctx, Reducer.QueryActiveBenefitsNearPoint args)
        {
            if (OnQueryActiveBenefitsNearPoint == null) return false;
            OnQueryActiveBenefitsNearPoint(
                ctx,
                args.Latitude,
                args.Longitude,
                args.RadiusKm
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class QueryActiveBenefitsNearPoint : Reducer, IReducerArgs
        {
            [DataMember(Name = "latitude")]
            public double Latitude;
            [DataMember(Name = "longitude")]
            public double Longitude;
            [DataMember(Name = "radiusKm")]
            public double RadiusKm;

            public QueryActiveBenefitsNearPoint(
                double Latitude,
                double Longitude,
                double RadiusKm
            )
            {
                this.Latitude = Latitude;
                this.Longitude = Longitude;
                this.RadiusKm = RadiusKm;
            }

            public QueryActiveBenefitsNearPoint()
            {
            }

            string IReducerArgs.ReducerName => "QueryActiveBenefitsNearPoint";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags QueryActiveBenefitsNearPointFlags;
        public void QueryActiveBenefitsNearPoint(CallReducerFlags flags) => QueryActiveBenefitsNearPointFlags = flags;
    }
}
