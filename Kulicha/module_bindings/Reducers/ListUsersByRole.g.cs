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
        public delegate void ListUsersByRoleHandler(ReducerEventContext ctx, int roleInt);
        public event ListUsersByRoleHandler? OnListUsersByRole;

        public void ListUsersByRole(int roleInt)
        {
            conn.InternalCallReducer(new Reducer.ListUsersByRole(roleInt), this.SetCallReducerFlags.ListUsersByRoleFlags);
        }

        public bool InvokeListUsersByRole(ReducerEventContext ctx, Reducer.ListUsersByRole args)
        {
            if (OnListUsersByRole == null) return false;
            OnListUsersByRole(
                ctx,
                args.RoleInt
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class ListUsersByRole : Reducer, IReducerArgs
        {
            [DataMember(Name = "roleInt")]
            public int RoleInt;

            public ListUsersByRole(int RoleInt)
            {
                this.RoleInt = RoleInt;
            }

            public ListUsersByRole()
            {
            }

            string IReducerArgs.ReducerName => "ListUsersByRole";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags ListUsersByRoleFlags;
        public void ListUsersByRole(CallReducerFlags flags) => ListUsersByRoleFlags = flags;
    }
}
