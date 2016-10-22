using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Rock.Collections.Internals
{
    public sealed class ValueCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TValue> m_collection;

        public ValueCollectionDebugView(ICollection<TValue> collection)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            m_collection = collection;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TValue[] Items
        {
            get
            {
                TValue[] items = new TValue[m_collection.Count];
                m_collection.CopyTo(items, 0);
                return items;
            }
        }
    }
}