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
        public delegate void GetAuditLogsHandler(ReducerEventContext ctx, long startTime, long endTime, int limit);
        public event GetAuditLogsHandler? OnGetAuditLogs;

        public void GetAuditLogs(long startTime, long endTime, int limit)
        {
            conn.InternalCallReducer(new Reducer.GetAuditLogs(startTime, endTime, limit), this.SetCallReducerFlags.GetAuditLogsFlags);
        }

        public bool InvokeGetAuditLogs(ReducerEventContext ctx, Reducer.GetAuditLogs args)
        {
            if (OnGetAuditLogs == null) return false;
            OnGetAuditLogs(
                ctx,
                args.StartTime,
                args.EndTime,
                args.Limit
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class GetAuditLogs : Reducer, IReducerArgs
        {
            [DataMember(Name = "startTime")]
            public long StartTime;
            [DataMember(Name = "endTime")]
            public long EndTime;
            [DataMember(Name = "limit")]
            public int Limit;

            public GetAuditLogs(
                long StartTime,
                long EndTime,
                int Limit
            )
            {
                this.StartTime = StartTime;
                this.EndTime = EndTime;
                this.Limit = Limit;
            }

            public GetAuditLogs()
            {
            }

            string IReducerArgs.ReducerName => "GetAuditLogs";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags GetAuditLogsFlags;
        public void GetAuditLogs(CallReducerFlags flags) => GetAuditLogsFlags = flags;
    }
}
