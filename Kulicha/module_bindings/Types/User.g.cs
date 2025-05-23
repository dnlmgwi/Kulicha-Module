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
    public sealed partial class User
    {
        [DataMember(Name = "Identity")]
        public SpacetimeDB.Identity Identity;
        [DataMember(Name = "Username")]
        public string? Username;
        [DataMember(Name = "Email")]
        public string? Email;
        [DataMember(Name = "IsEmailVerified")]
        public bool IsEmailVerified;
        [DataMember(Name = "RegisteredAt")]
        public SpacetimeDB.Timestamp RegisteredAt;

        public User(
            SpacetimeDB.Identity Identity,
            string? Username,
            string? Email,
            bool IsEmailVerified,
            SpacetimeDB.Timestamp RegisteredAt
        )
        {
            this.Identity = Identity;
            this.Username = Username;
            this.Email = Email;
            this.IsEmailVerified = IsEmailVerified;
            this.RegisteredAt = RegisteredAt;
        }

        public User()
        {
        }
    }
}
