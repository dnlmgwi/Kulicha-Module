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
        public delegate void UpdateBenefitDefinitionHandler(ReducerEventContext ctx, long benefitId, string name, string? description, int typeInt, long cost, string? provider, string? policyDetails, bool isActive, long primaryLocationId);
        public event UpdateBenefitDefinitionHandler? OnUpdateBenefitDefinition;

        public void UpdateBenefitDefinition(long benefitId, string name, string? description, int typeInt, long cost, string? provider, string? policyDetails, bool isActive, long primaryLocationId)
        {
            conn.InternalCallReducer(new Reducer.UpdateBenefitDefinition(benefitId, name, description, typeInt, cost, provider, policyDetails, isActive, primaryLocationId), this.SetCallReducerFlags.UpdateBenefitDefinitionFlags);
        }

        public bool InvokeUpdateBenefitDefinition(ReducerEventContext ctx, Reducer.UpdateBenefitDefinition args)
        {
            if (OnUpdateBenefitDefinition == null) return false;
            OnUpdateBenefitDefinition(
                ctx,
                args.BenefitId,
                args.Name,
                args.Description,
                args.TypeInt,
                args.Cost,
                args.Provider,
                args.PolicyDetails,
                args.IsActive,
                args.PrimaryLocationId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class UpdateBenefitDefinition : Reducer, IReducerArgs
        {
            [DataMember(Name = "benefitId")]
            public long BenefitId;
            [DataMember(Name = "name")]
            public string Name;
            [DataMember(Name = "description")]
            public string? Description;
            [DataMember(Name = "typeInt")]
            public int TypeInt;
            [DataMember(Name = "cost")]
            public long Cost;
            [DataMember(Name = "provider")]
            public string? Provider;
            [DataMember(Name = "policyDetails")]
            public string? PolicyDetails;
            [DataMember(Name = "isActive")]
            public bool IsActive;
            [DataMember(Name = "primaryLocationId")]
            public long PrimaryLocationId;

            public UpdateBenefitDefinition(
                long BenefitId,
                string Name,
                string? Description,
                int TypeInt,
                long Cost,
                string? Provider,
                string? PolicyDetails,
                bool IsActive,
                long PrimaryLocationId
            )
            {
                this.BenefitId = BenefitId;
                this.Name = Name;
                this.Description = Description;
                this.TypeInt = TypeInt;
                this.Cost = Cost;
                this.Provider = Provider;
                this.PolicyDetails = PolicyDetails;
                this.IsActive = IsActive;
                this.PrimaryLocationId = PrimaryLocationId;
            }

            public UpdateBenefitDefinition()
            {
                this.Name = "";
            }

            string IReducerArgs.ReducerName => "UpdateBenefitDefinition";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags UpdateBenefitDefinitionFlags;
        public void UpdateBenefitDefinition(CallReducerFlags flags) => UpdateBenefitDefinitionFlags = flags;
    }
}
