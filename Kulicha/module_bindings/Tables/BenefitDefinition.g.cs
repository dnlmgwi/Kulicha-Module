// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.BSATN;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteTables
    {
        public sealed class BenefitDefinitionHandle : RemoteTableHandle<EventContext, BenefitDefinition>
        {
            protected override string RemoteTableName => "BenefitDefinition";

            public sealed class BenefitIdUniqueIndex : UniqueIndexBase<long>
            {
                protected override long GetKey(BenefitDefinition row) => row.BenefitId;

                public BenefitIdUniqueIndex(BenefitDefinitionHandle table) : base(table) { }
            }

            public readonly BenefitIdUniqueIndex BenefitId;

            internal BenefitDefinitionHandle(DbConnection conn) : base(conn)
            {
                BenefitId = new(this);
            }

            protected override object GetPrimaryKey(BenefitDefinition row) => row.BenefitId;
        }

        public readonly BenefitDefinitionHandle BenefitDefinition;
    }
}
