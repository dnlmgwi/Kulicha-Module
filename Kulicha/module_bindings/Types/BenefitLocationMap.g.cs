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
    public sealed partial class BenefitLocationMap
    {
        [DataMember(Name = "MapId")]
        public long MapId;
        [DataMember(Name = "BenefitId")]
        public long BenefitId;
        [DataMember(Name = "LocationId")]
        public long LocationId;
        [DataMember(Name = "AddedAt")]
        public SpacetimeDB.Timestamp AddedAt;
        [DataMember(Name = "RemovedAt")]
        public SpacetimeDB.Timestamp? RemovedAt;

        public BenefitLocationMap(
            long MapId,
            long BenefitId,
            long LocationId,
            SpacetimeDB.Timestamp AddedAt,
            SpacetimeDB.Timestamp? RemovedAt
        )
        {
            this.MapId = MapId;
            this.BenefitId = BenefitId;
            this.LocationId = LocationId;
            this.AddedAt = AddedAt;
            this.RemovedAt = RemovedAt;
        }

        public BenefitLocationMap()
        {
        }
    }
}
