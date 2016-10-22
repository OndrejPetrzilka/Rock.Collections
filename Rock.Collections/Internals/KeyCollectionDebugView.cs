using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Rock.Collections.Internals
{
    public sealed class KeyCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TKey> m_collection;

        public KeyCollectionDebugView(ICollection<TKey> collection)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            m_collection = collection;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TKey[] Items
        {
            get
            {
                TKey[] items = new TKey[m_collection.Count];
                m_collection.CopyTo(items, 0);
                return items;
            }
        }
    }
}