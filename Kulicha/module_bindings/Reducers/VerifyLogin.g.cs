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
        public delegate void VerifyLoginHandler(ReducerEventContext ctx, string verificationCode, string deviceId);
        public event VerifyLoginHandler? OnVerifyLogin;

        public void VerifyLogin(string verificationCode, string deviceId)
        {
            conn.InternalCallReducer(new Reducer.VerifyLogin(verificationCode, deviceId), this.SetCallReducerFlags.VerifyLoginFlags);
        }

        public bool InvokeVerifyLogin(ReducerEventContext ctx, Reducer.VerifyLogin args)
        {
            if (OnVerifyLogin == null) return false;
            OnVerifyLogin(
                ctx,
                args.VerificationCode,
                args.DeviceId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class VerifyLogin : Reducer, IReducerArgs
        {
            [DataMember(Name = "verificationCode")]
            public string VerificationCode;
            [DataMember(Name = "deviceId")]
            public string DeviceId;

            public VerifyLogin(
                string VerificationCode,
                string DeviceId
            )
            {
                this.VerificationCode = VerificationCode;
                this.DeviceId = DeviceId;
            }

            public VerifyLogin()
            {
                this.VerificationCode = "";
                this.DeviceId = "";
            }

            string IReducerArgs.ReducerName => "VerifyLogin";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags VerifyLoginFlags;
        public void VerifyLogin(CallReducerFlags flags) => VerifyLoginFlags = flags;
    }
}
