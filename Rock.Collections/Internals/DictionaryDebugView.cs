using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Rock.Collections.Internals
{
    public sealed class DictionaryDebugView<K, V>
    {
        private readonly IDictionary<K, V> m_dict;

        public DictionaryDebugView(IDictionary<K, V> dictionary)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            m_dict = dictionary;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<K, V>[] Items
        {
            get
            {
                KeyValuePair<K, V>[] items = new KeyValuePair<K, V>[m_dict.Count];
                m_dict.CopyTo(items, 0);
                return items;
            }
        }
    }
}