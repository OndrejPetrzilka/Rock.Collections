// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Rock.Collections.Internals;

namespace Rock.Collections
{
    [DebuggerTypeProxy(typeof(DictionaryDebugView<,>))]
    [DebuggerDisplay("Count = {Count}")]
    [System.Runtime.InteropServices.ComVisible(false)]
    [Serializable]
    public class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>, ISerializable, IDeserializationCallback
    {
        private struct Entry
        {
            public int hashCode;    // Lower 31 bits of hash code, -1 if unused
            public int next;        // Index of next entry, -1 if last
            public TKey key;           // Key of entry
            public TValue value;         // Value of entry

            public int nextOrder;     // Index of next entry by order, -1 if last
            public int previousOrder; // Index of previous entry by order, -1 if first
        }

        private int[] buckets;
        private Entry[] entries;
        private int count;
        private int version;
        private int m_firstOrderIndex;  // Index of first entry by order
        private int m_lastOrderIndex;   // Index of last entry by order

        private int freeList;
        private int freeCount;
        private IEqualityComparer<TKey> comparer;
        private KeyCollection keys;
        private ValueCollection values;
        private object _syncRoot;

        // constants for serialization
        private const string VersionName = "Version";
        private const string HashSizeName = "HashSize"; // Must save buckets.Length
        private const string KeyValuePairsName = "KeyValuePairs";
        private const string ComparerName = "Comparer";

        public Reader Items
        {
            get { return new Reader(this); }
        }

        public ReverseReader Reversed
        {
            get { return new ReverseReader(this); }
        }

        public OrderedDictionary() : this(0, null) { }

        public OrderedDictionary(int capacity) : this(capacity, null) { }

        public OrderedDictionary(IEqualityComparer<TKey> comparer) : this(0, comparer) { }

        public OrderedDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "ArgumentOutOfRange_NeedNonNegNum");
            if (capacity > 0) Initialize(capacity);
            else m_firstOrderIndex = m_lastOrderIndex = -1;
            this.comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        public OrderedDictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        public OrderedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer) :
            this(dictionary != null ? dictionary.Count : 0, comparer)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            // It is likely that the passed-in dictionary is OrderedDictionary<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation.
            if (dictionary.GetType() == typeof(OrderedDictionary<TKey, TValue>))
            {
                OrderedDictionary<TKey, TValue> d = (OrderedDictionary<TKey, TValue>)dictionary;
                foreach (var item in d)
                {
                    // Correct way to add items in same order
                    Add(item.Key, item.Value);
                }
            }
            else
            {
                foreach (KeyValuePair<TKey, TValue> pair in dictionary)
                {
                    Add(pair.Key, pair.Value);
                }
            }
        }

        protected OrderedDictionary(SerializationInfo info, StreamingContext context)
        {
            // We can't do anything with the keys and values until the entire graph has been deserialized
            // and we have a resonable estimate that GetHashCode is not going to fail.  For the time being,
            // we'll just cache this.  The graph is not valid until OnDeserialization has been called.
            HashHelpers.SerializationInfoTable.Add(this, info);
        }

        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                return comparer;
            }
        }

        public int Count
        {
            get { return count - freeCount; }
        }

        public KeyCollection Keys
        {
            get
            {
                Contract.Ensures(Contract.Result<KeyCollection>() != null);
                if (keys == null) keys = new KeyCollection(this);
                return keys;
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                if (keys == null) keys = new KeyCollection(this);
                return keys;
            }
        }

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get
            {
                if (keys == null) keys = new KeyCollection(this);
                return keys;
            }
        }

        public ValueCollection Values
        {
            get
            {
                Contract.Ensures(Contract.Result<ValueCollection>() != null);
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get
            {
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                int i = FindEntry(key);
                if (i >= 0) return entries[i].value;
                throw new KeyNotFoundException();
            }
            set
            {
                Insert(key, value, false);
            }
        }

        public void Add(TKey key, TValue value)
        {
            Insert(key, value, true);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair)
        {
            Add(keyValuePair.Key, keyValuePair.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            int i = FindEntry(keyValuePair.Key);
            if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value))
            {
                return true;
            }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            int i = FindEntry(keyValuePair.Key);
            if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value))
            {
                Remove(keyValuePair.Key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (count > 0)
            {
                for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
                Array.Clear(entries, 0, count);
                freeList = -1;
                count = 0;
                freeCount = 0;
                m_firstOrderIndex = -1;
                m_lastOrderIndex = -1;
                version++;
            }
        }

        public bool ContainsKey(TKey key)
        {
            return FindEntry(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            if (value == null)
            {
                for (int i = 0; i < count; i++)
                {
                    if (entries[i].hashCode >= 0 && entries[i].value == null) return true;
                }
            }
            else
            {
                EqualityComparer<TValue> c = EqualityComparer<TValue>.Default;
                for (int i = 0; i < count; i++)
                {
                    if (entries[i].hashCode >= 0 && c.Equals(entries[i].value, value)) return true;
                }
            }
            return false;
        }

        private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            int numCopied = 0;
            Entry[] entries = this.entries;
            for (int i = m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
            {
                array[index + numCopied] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
                numCopied++;
            }
            Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        /// <summary>
        /// Gets enumerator which starts enumerating at specified element, O(1).
        /// When element is not found, empty enumerator is returned.
        /// </summary>
        public Enumerator GetEnumerator(TKey startingElement)
        {
            int index = FindEntry(startingElement);
            return new Enumerator(this, Enumerator.KeyValuePair, index);
        }

        public ReverseEnumerator GetReverseEnumerator()
        {
            return new ReverseEnumerator(this, Enumerator.KeyValuePair);
        }

        /// <summary>
        /// Gets reverse enumerator which starts enumerating at specified element, O(1).
        /// When element is not found, empty enumerator is returned.
        /// </summary>
        public ReverseEnumerator GetReverseEnumerator(TKey startingElement)
        {
            int index = FindEntry(startingElement);
            return new ReverseEnumerator(this, Enumerator.KeyValuePair, index);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(VersionName, version);
            info.AddValue(ComparerName, HashHelpers.GetEqualityComparerForSerialization(comparer), typeof(IEqualityComparer<TKey>));
            info.AddValue(HashSizeName, buckets == null ? 0 : buckets.Length); // This is the length of the bucket array

            if (buckets != null)
            {
                var array = new KeyValuePair<TKey, TValue>[Count];
                CopyTo(array, 0);
                info.AddValue(KeyValuePairsName, array, typeof(KeyValuePair<TKey, TValue>[]));
            }
        }

        private int FindEntry(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (buckets != null)
            {
                int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
                for (int i = buckets[hashCode % buckets.Length]; i >= 0; i = entries[i].next)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key)) return i;
                }
            }
            return -1;
        }

        private void Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            buckets = new int[size];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
            entries = new Entry[size];
            freeList = -1;
            m_firstOrderIndex = -1;
            m_lastOrderIndex = -1;
        }

        private void Insert(TKey key, TValue value, bool add)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (buckets == null) Initialize(0);
            int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
            int targetBucket = hashCode % buckets.Length;

#if FEATURE_RANDOMIZED_STRING_HASHING
            int collisionCount = 0;
#endif

            for (int i = buckets[targetBucket]; i >= 0; i = entries[i].next)
            {
                if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                {
                    if (add)
                    {
                        throw new ArgumentException("Argument_AddingDuplicate" + key);
                    }
                    entries[i].value = value;
                    version++;
                    return;
                }
#if FEATURE_RANDOMIZED_STRING_HASHING
                collisionCount++;
#endif
            }

            int index;

            if (freeCount > 0)
            {
                index = freeList;
                freeList = entries[index].next;
                freeCount--;
            }
            else
            {
                if (count == entries.Length)
                {
                    Resize();
                    targetBucket = hashCode % buckets.Length;
                }
                index = count;
                count++;
            }

            entries[index].hashCode = hashCode;
            entries[index].next = buckets[targetBucket];
            entries[index].key = key;
            entries[index].value = value;

            // Append to linked list
            if (m_lastOrderIndex != -1)
            {
                entries[m_lastOrderIndex].nextOrder = index;
            }
            if (m_firstOrderIndex == -1)
            {
                m_firstOrderIndex = index;
            }
            entries[index].nextOrder = -1;
            entries[index].previousOrder = m_lastOrderIndex;
            m_lastOrderIndex = index;

            buckets[targetBucket] = index;
            version++;
#if FEATURE_RANDOMIZED_STRING_HASHING
            if (collisionCount > HashHelpers.HashCollisionThreshold && HashHelpers.IsWellKnownEqualityComparer(comparer))
            {
                comparer = (IEqualityComparer<TKey>)HashHelpers.GetRandomizedEqualityComparer(comparer);
                Resize(entries.Length, true);
            }
#endif
        }

        public virtual void OnDeserialization(object sender)
        {
            SerializationInfo siInfo;
            HashHelpers.SerializationInfoTable.TryGetValue(this, out siInfo);
            if (siInfo == null)
            {
                // We can return immediately if this function is called twice. 
                // Note we remove the serialization info from the table at the end of this method.
                return;
            }

            int realVersion = siInfo.GetInt32(VersionName);
            int hashsize = siInfo.GetInt32(HashSizeName);
            comparer = (IEqualityComparer<TKey>)siInfo.GetValue(ComparerName, typeof(IEqualityComparer<TKey>));

            if (hashsize != 0)
            {
                buckets = new int[hashsize];
                for (int i = 0; i < buckets.Length; i++) buckets[i] = -1;
                entries = new Entry[hashsize];
                freeList = -1;
                m_firstOrderIndex = -1;
                m_lastOrderIndex = -1;

                KeyValuePair<TKey, TValue>[] array =
                    (KeyValuePair<TKey, TValue>[])siInfo.GetValue(KeyValuePairsName, typeof(KeyValuePair<TKey, TValue>[]));

                if (array == null)
                {
                    throw new SerializationException("Serialization_MissingKeys");
                }

                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i].Key == null)
                    {
                        throw new SerializationException("Serialization_NullKey");
                    }
                    Insert(array[i].Key, array[i].Value, true);
                }
            }
            else
            {
                buckets = null;
            }

            version = realVersion;
            HashHelpers.SerializationInfoTable.Remove(this);
        }

        private void Resize()
        {
            Resize(HashHelpers.ExpandPrime(count), false);
        }

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            Debug.Assert(newSize >= entries.Length);
            int[] newBuckets = new int[newSize];
            for (int i = 0; i < newBuckets.Length; i++) newBuckets[i] = -1;

            Entry[] newEntries = new Entry[newSize];
            Array.Copy(entries, 0, newEntries, 0, count);

            if (forceNewHashCodes)
            {
                for (int i = 0; i < count; i++)
                {
                    if (newEntries[i].hashCode != -1)
                    {
                        newEntries[i].hashCode = (comparer.GetHashCode(newEntries[i].key) & 0x7FFFFFFF);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (newEntries[i].hashCode >= 0)
                {
                    int bucket = newEntries[i].hashCode % newSize;
                    newEntries[i].next = newBuckets[bucket];
                    newBuckets[bucket] = i;
                }
            }

            buckets = newBuckets;
            entries = newEntries;
        }

        public bool Remove(TKey key, out TValue value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (buckets != null)
            {
                int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
                int bucket = hashCode % buckets.Length;
                int last = -1;
                for (int i = buckets[bucket]; i >= 0; last = i, i = entries[i].next)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                    {
                        if (last < 0)
                        {
                            buckets[bucket] = entries[i].next;
                        }
                        else
                        {
                            entries[last].next = entries[i].next;
                        }
                        value = entries[i].value;
                        entries[i].hashCode = -1;
                        entries[i].next = freeList;
                        entries[i].key = default(TKey);
                        entries[i].value = default(TValue);

                        // Connect linked list
                        if (m_firstOrderIndex == i) // Is first
                        {
                            m_firstOrderIndex = entries[i].nextOrder;
                        }
                        if (m_lastOrderIndex == i) // Is last
                        {
                            m_lastOrderIndex = entries[i].previousOrder;
                        }

                        var next = entries[i].nextOrder;
                        var prev = entries[i].previousOrder;
                        if (next != -1)
                        {
                            entries[next].previousOrder = prev;
                        }
                        if (prev != -1)
                        {
                            entries[prev].nextOrder = next;
                        }

                        entries[i].previousOrder = -1;
                        entries[i].nextOrder = -1;

                        freeList = i;
                        freeCount++;
                        version++;
                        return true;
                    }
                }
            }
            value = default(TValue);
            return false;
        }

        public bool Remove(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (buckets != null)
            {
                int hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
                int bucket = hashCode % buckets.Length;
                int last = -1;
                for (int i = buckets[bucket]; i >= 0; last = i, i = entries[i].next)
                {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key))
                    {
                        if (last < 0)
                        {
                            buckets[bucket] = entries[i].next;
                        }
                        else
                        {
                            entries[last].next = entries[i].next;
                        }
                        entries[i].hashCode = -1;
                        entries[i].next = freeList;
                        entries[i].key = default(TKey);
                        entries[i].value = default(TValue);

                        // Connect linked list
                        if (m_firstOrderIndex == i) // Is first
                        {
                            m_firstOrderIndex = entries[i].nextOrder;
                        }
                        if (m_lastOrderIndex == i) // Is last
                        {
                            m_lastOrderIndex = entries[i].previousOrder;
                        }

                        var next = entries[i].nextOrder;
                        var prev = entries[i].previousOrder;
                        if (next != -1)
                        {
                            entries[next].previousOrder = prev;
                        }
                        if (prev != -1)
                        {
                            entries[prev].nextOrder = next;
                        }

                        entries[i].previousOrder = -1;
                        entries[i].nextOrder = -1;

                        freeList = i;
                        freeCount++;
                        version++;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            int i = FindEntry(key);
            if (i >= 0)
            {
                value = entries[i].value;
                return true;
            }
            value = default(TValue);
            return false;
        }

        public bool MoveFirst(TKey key)
        {
            int index = FindEntry(key);
            if (index != -1)
            {
                var prev = entries[index].previousOrder;
                if (prev != -1) // Not first
                {
                    // Disconnect
                    var next = entries[index].nextOrder;
                    if (next == -1) // Last
                    {
                        m_lastOrderIndex = prev;
                    }
                    else
                    {
                        entries[next].previousOrder = prev;
                    }
                    entries[prev].nextOrder = next;

                    // Reconnect
                    entries[index].previousOrder = -1;
                    entries[index].nextOrder = m_firstOrderIndex;
                    entries[m_firstOrderIndex].previousOrder = index;
                    m_firstOrderIndex = index;
                }
                return true;
            }
            return false;
        }

        public bool MoveLast(TKey key)
        {
            int index = FindEntry(key);
            if (index != -1)
            {
                var next = entries[index].nextOrder;
                if (next != -1) // Not last
                {
                    // Disconnect
                    var prev = entries[index].previousOrder;
                    if (prev == -1) // First
                    {
                        m_firstOrderIndex = next;
                    }
                    else
                    {
                        entries[prev].nextOrder = next;
                    }
                    entries[next].previousOrder = prev;

                    // Reconnect
                    entries[index].nextOrder = -1;
                    entries[index].previousOrder = m_lastOrderIndex;
                    entries[m_lastOrderIndex].nextOrder = index;
                    m_lastOrderIndex = index;
                }
                return true;
            }
            return false;
        }

        public bool MoveBefore(TKey keyToMove, TKey mark)
        {
            int index = FindEntry(keyToMove);
            int markIndex = FindEntry(mark);
            if (index != -1 && markIndex != -1 && index != markIndex)
            {
                // Disconnect
                var next = entries[index].nextOrder;
                var prev = entries[index].previousOrder;
                if (prev == -1) // First
                {
                    m_firstOrderIndex = next;
                }
                else
                {
                    entries[prev].nextOrder = next;
                }
                if (next == -1) // Last
                {
                    m_lastOrderIndex = prev;
                }
                else
                {
                    entries[next].previousOrder = prev;
                }

                // Reconnect
                var preMark = entries[markIndex].previousOrder;
                entries[index].nextOrder = markIndex;
                entries[index].previousOrder = preMark;
                entries[markIndex].previousOrder = index;
                if (preMark == -1)
                {
                    m_firstOrderIndex = index;
                }
                else
                {
                    entries[preMark].nextOrder = index;
                }
                return true;
            }
            return false;
        }

        public bool MoveAfter(TKey keyToMove, TKey mark)
        {
            int index = FindEntry(keyToMove);
            int markIndex = FindEntry(mark);
            if (index != -1 && markIndex != -1 && index != markIndex)
            {
                // Disconnect
                var next = entries[index].nextOrder;
                var prev = entries[index].previousOrder;
                if (prev == -1) // First
                {
                    m_firstOrderIndex = next;
                }
                else
                {
                    entries[prev].nextOrder = next;
                }
                if (next == -1) // Last
                {
                    m_lastOrderIndex = prev;
                }
                else
                {
                    entries[next].previousOrder = prev;
                }

                // Reconnect
                var postMark = entries[markIndex].nextOrder;
                entries[index].previousOrder = markIndex;
                entries[index].nextOrder = postMark;
                entries[markIndex].nextOrder = index;
                if (postMark == -1)
                {
                    m_lastOrderIndex = index;
                }
                else
                {
                    entries[postMark].previousOrder = index;
                }
                return true;
            }
            return false;
        }

        // This is a convenience method for the internal callers that were converted from using Hashtable.
        // Many were combining key doesn't exist and key exists but null value (for non-value types) checks.
        // This allows them to continue getting that behavior with minimal code delta. This is basically
        // TryGetValue without the out param
        internal TValue GetValueOrDefault(TKey key)
        {
            int i = FindEntry(key);
            if (i >= 0)
            {
                return entries[i].value;
            }
            return default(TValue);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get { return false; }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            CopyTo(array, index);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (array.Rank != 1)
            {
                throw new ArgumentException("Arg_RankMultiDimNotSupported", nameof(array));
            }

            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException("Arg_NonZeroLowerBound", nameof(array));
            }

            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            KeyValuePair<TKey, TValue>[] pairs = array as KeyValuePair<TKey, TValue>[];
            if (pairs != null)
            {
                CopyTo(pairs, index);
            }
            else if (array is DictionaryEntry[])
            {
                DictionaryEntry[] dictEntryArray = array as DictionaryEntry[];
                Entry[] entries = this.entries;

                int numCopied = 0;
                for (int i = m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                {
                    dictEntryArray[index + numCopied] = new DictionaryEntry(entries[i].key, entries[i].value);
                    numCopied++;
                }
                Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
            }
            else
            {
                object[] objects = array as object[];
                if (objects == null)
                {
                    throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                }

                try
                {
                    Entry[] entries = this.entries;
                    int numCopied = 0;
                    for (int i = m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                    {
                        objects[index + numCopied] = new DictionaryEntry(entries[i].key, entries[i].value);
                        numCopied++;
                    }
                    Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        bool ICollection.IsSynchronized
        {
            get { return false; }
        }

        object ICollection.SyncRoot
        {
            get
            {
                if (_syncRoot == null)
                {
                    System.Threading.Interlocked.CompareExchange<object>(ref _syncRoot, new object(), null);
                }
                return _syncRoot;
            }
        }

        bool IDictionary.IsFixedSize
        {
            get { return false; }
        }

        bool IDictionary.IsReadOnly
        {
            get { return false; }
        }

        ICollection IDictionary.Keys
        {
            get { return (ICollection)Keys; }
        }

        ICollection IDictionary.Values
        {
            get { return (ICollection)Values; }
        }

        object IDictionary.this[object key]
        {
            get
            {
                if (IsCompatibleKey(key))
                {
                    int i = FindEntry((TKey)key);
                    if (i >= 0)
                    {
                        return entries[i].value;
                    }
                }
                return null;
            }
            set
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }
                if (value == null && !(default(TValue) == null))
                    throw new ArgumentNullException(nameof(value));

                try
                {
                    TKey tempKey = (TKey)key;
                    try
                    {
                        this[tempKey] = (TValue)value;
                    }
                    catch (InvalidCastException)
                    {
                        throw new ArgumentException(string.Format("The value '{0}' is not of type '{1}' and cannot be used in this generic collection.", value, typeof(TValue)), nameof(value));
                    }
                }
                catch (InvalidCastException)
                {
                    throw new ArgumentException(string.Format("The value '{0}' is not of type '{1}' and cannot be used in this generic collection.", key, typeof(TKey)), nameof(key));
                }
            }
        }

        private static bool IsCompatibleKey(object key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            return (key is TKey);
        }

        void IDictionary.Add(object key, object value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (value == null && !(default(TValue) == null))
                throw new ArgumentNullException(nameof(value));

            try
            {
                TKey tempKey = (TKey)key;

                try
                {
                    Add(tempKey, (TValue)value);
                }
                catch (InvalidCastException)
                {
                    throw new ArgumentException(String.Format("The value '{0}' is not of type '{1}' and cannot be used in this generic collection.", value, typeof(TValue)), nameof(value));
                }
            }
            catch (InvalidCastException)
            {
                throw new ArgumentException(String.Format("The value '{0}' is not of type '{1}' and cannot be used in this generic collection.", key, typeof(TKey)), nameof(key));
            }
        }

        bool IDictionary.Contains(object key)
        {
            if (IsCompatibleKey(key))
            {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.DictEntry);
        }

        void IDictionary.Remove(object key)
        {
            if (IsCompatibleKey(key))
            {
                Remove((TKey)key);
            }
        }

        internal struct EmptyHelper
        {
            // We want to initialize static field as late as possible, that's why it's separate type
            public static OrderedDictionary<TKey, TValue> m_empty = new OrderedDictionary<TKey, TValue>();
        }

        public struct Reader : IEnumerable<KeyValuePair<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>
        {
            public static Reader Empty { get { return new Reader(EmptyHelper.m_empty); } }

            private OrderedDictionary<TKey, TValue> dictionary;

            public TValue this[TKey key]
            {
                get { return dictionary[key]; }
            }

            public int Count
            {
                get { return dictionary.Count; }
            }

            IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
            {
                get { return dictionary.keys; }
            }

            IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
            {
                get { return dictionary.values; }
            }

            public Reader(OrderedDictionary<TKey, TValue> dictionary)
            {
                this.dictionary = dictionary;
            }

            public bool ContainsKey(TKey key)
            {
                return dictionary.ContainsKey(key);
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                return dictionary.TryGetValue(key, out value);
            }

            public Enumerator GetEnumerator()
            {
                return dictionary.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public struct ReverseReader : IEnumerable<KeyValuePair<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>
        {
            public static ReverseReader Empty { get { return new ReverseReader(EmptyHelper.m_empty); } }

            private OrderedDictionary<TKey, TValue> dictionary;

            public TValue this[TKey key]
            {
                get { return dictionary[key]; }
            }

            public int Count
            {
                get { return dictionary.Count; }
            }

            IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
            {
                get { return this.Select(s => s.Key); }
            }

            IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
            {
                get { return this.Select(s => s.Value); }
            }

            public ReverseReader(OrderedDictionary<TKey, TValue> dictionary)
            {
                this.dictionary = dictionary;
            }

            public bool ContainsKey(TKey key)
            {
                return dictionary.ContainsKey(key);
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                return dictionary.TryGetValue(key, out value);
            }

            public ReverseEnumerator GetEnumerator()
            {
                return new ReverseEnumerator(dictionary, Enumerator.KeyValuePair);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        [Serializable]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private OrderedDictionary<TKey, TValue> dictionary;
            private int version;
            private int index;
            private KeyValuePair<TKey, TValue> current;
            private int getEnumeratorRetType;  // What should Enumerator.Current return?

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(OrderedDictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = dictionary.m_firstOrderIndex;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = default(KeyValuePair<TKey, TValue>);
            }

            internal Enumerator(OrderedDictionary<TKey, TValue> dictionary, int getEnumeratorRetType, int startingIndex)
            {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = startingIndex;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = default(KeyValuePair<TKey, TValue>);
            }

            public bool MoveNext()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                while (index != -1)
                {
                    current = new KeyValuePair<TKey, TValue>(dictionary.entries[index].key, dictionary.entries[index].value);
                    index = dictionary.entries[index].nextOrder;
                    return true;
                }

                index = -1;
                current = default(KeyValuePair<TKey, TValue>);
                return false;
            }

            public KeyValuePair<TKey, TValue> Current
            {
                get { return current; }
            }

            public void Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (index == dictionary.m_firstOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    if (getEnumeratorRetType == DictEntry)
                    {
                        return new System.Collections.DictionaryEntry(current.Key, current.Value);
                    }
                    else
                    {
                        return current;
                    }
                }
            }

            void IEnumerator.Reset()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                index = dictionary.m_firstOrderIndex;
                current = default(KeyValuePair<TKey, TValue>);
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (index == dictionary.m_firstOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return new DictionaryEntry(current.Key, current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (index == dictionary.m_firstOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Key;
                }
            }

            object IDictionaryEnumerator.Value
            {
                get
                {
                    if (index == dictionary.m_firstOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Value;
                }
            }
        }

        [Serializable]
        public struct ReverseEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private OrderedDictionary<TKey, TValue> dictionary;
            private int version;
            private int index;
            private KeyValuePair<TKey, TValue> current;
            private int getEnumeratorRetType;  // What should Enumerator.Current return?

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal ReverseEnumerator(OrderedDictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = dictionary.m_lastOrderIndex;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = default(KeyValuePair<TKey, TValue>);
            }

            internal ReverseEnumerator(OrderedDictionary<TKey, TValue> dictionary, int getEnumeratorRetType, int startingIndex)
            {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = startingIndex;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = default(KeyValuePair<TKey, TValue>);
            }

            public bool MoveNext()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                while (index != -1)
                {
                    current = new KeyValuePair<TKey, TValue>(dictionary.entries[index].key, dictionary.entries[index].value);
                    index = dictionary.entries[index].previousOrder;
                    return true;
                }

                index = -1;
                current = default(KeyValuePair<TKey, TValue>);
                return false;
            }

            public KeyValuePair<TKey, TValue> Current
            {
                get { return current; }
            }

            public void Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (index == dictionary.m_lastOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    if (getEnumeratorRetType == DictEntry)
                    {
                        return new System.Collections.DictionaryEntry(current.Key, current.Value);
                    }
                    else
                    {
                        return current;
                    }
                }
            }

            void IEnumerator.Reset()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                index = dictionary.m_lastOrderIndex;
                current = default(KeyValuePair<TKey, TValue>);
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (index == dictionary.m_lastOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return new DictionaryEntry(current.Key, current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (index == dictionary.m_lastOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Key;
                }
            }

            object IDictionaryEnumerator.Value
            {
                get
                {
                    if (index == dictionary.m_lastOrderIndex || index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Value;
                }
            }
        }

        [DebuggerTypeProxy(typeof(KeyCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        [Serializable]
        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private OrderedDictionary<TKey, TValue> dictionary;

            public KeyCollection(OrderedDictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException(nameof(dictionary));
                }
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            public void CopyTo(TKey[] array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                int numCopied = 0;
                Entry[] entries = dictionary.entries;
                for (int i = dictionary.m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                {
                    array[index + numCopied] = entries[i].key;
                    numCopied++;
                }
                Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
            }

            public int Count
            {
                get { return dictionary.Count; }
            }

            bool ICollection<TKey>.IsReadOnly
            {
                get { return true; }
            }

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            bool ICollection<TKey>.Contains(TKey item)
            {
                return dictionary.ContainsKey(item);
            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (array.Rank != 1)
                {
                    throw new ArgumentException("Arg_RankMultiDimNotSupported", nameof(array));
                }

                if (array.GetLowerBound(0) != 0)
                {
                    throw new ArgumentException("Arg_NonZeroLowerBound", nameof(array));
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                TKey[] keys = array as TKey[];
                if (keys != null)
                {
                    CopyTo(keys, index);
                }
                else
                {
                    object[] objects = array as object[];
                    if (objects == null)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                    }

                    int numCopied = 0;
                    Entry[] entries = dictionary.entries;

                    try
                    {
                        for (int i = dictionary.m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                        {
                            objects[index + numCopied] = entries[i].key;
                            numCopied++;
                        }
                        Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                    }
                }
            }

            bool ICollection.IsSynchronized
            {
                get { return false; }
            }

            object ICollection.SyncRoot
            {
                get { return ((ICollection)dictionary).SyncRoot; }
            }

            [Serializable]
            public struct Enumerator : IEnumerator<TKey>, System.Collections.IEnumerator
            {
                private OrderedDictionary<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TKey currentKey;

                internal Enumerator(OrderedDictionary<TKey, TValue> dictionary)
                {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = dictionary.m_firstOrderIndex;
                    currentKey = default(TKey);
                }

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    while (index != -1)
                    {
                        currentKey = dictionary.entries[index].key;
                        index = dictionary.entries[index].nextOrder;
                        return true;
                    }

                    index = -1;
                    currentKey = default(TKey);
                    return false;
                }

                public TKey Current
                {
                    get
                    {
                        return currentKey;
                    }
                }

                object System.Collections.IEnumerator.Current
                {
                    get
                    {
                        if (index == dictionary.m_firstOrderIndex || index == -1)
                        {
                            throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                        }

                        return currentKey;
                    }
                }

                void System.Collections.IEnumerator.Reset()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    index = dictionary.m_firstOrderIndex;
                    currentKey = default(TKey);
                }
            }
        }

        [DebuggerTypeProxy(typeof(ValueCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        [Serializable]
        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private OrderedDictionary<TKey, TValue> dictionary;

            public ValueCollection(OrderedDictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException(nameof(dictionary));
                }
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            public void CopyTo(TValue[] array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                int numCopied = 0;
                Entry[] entries = dictionary.entries;
                for (int i = dictionary.m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                {
                    array[index + numCopied] = entries[i].value;
                    numCopied++;
                }
                Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
            }

            public int Count
            {
                get { return dictionary.Count; }
            }

            bool ICollection<TValue>.IsReadOnly
            {
                get { return true; }
            }

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            bool ICollection<TValue>.Contains(TValue item)
            {
                return dictionary.ContainsValue(item);
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException(nameof(array));
                }

                if (array.Rank != 1)
                {
                    throw new ArgumentException("Arg_RankMultiDimNotSupported", nameof(array));
                }

                if (array.GetLowerBound(0) != 0)
                {
                    throw new ArgumentException("Arg_NonZeroLowerBound", nameof(array));
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_Index");
                }

                if (array.Length - index < dictionary.Count)
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");

                TValue[] values = array as TValue[];
                if (values != null)
                {
                    CopyTo(values, index);
                }
                else
                {
                    object[] objects = array as object[];
                    if (objects == null)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                    }

                    int numCopied = 0;
                    Entry[] entries = dictionary.entries;

                    try
                    {
                        for (int i = dictionary.m_firstOrderIndex; i != -1; i = entries[i].nextOrder)
                        {
                            objects[index + numCopied] = entries[i].value;
                            numCopied++;
                        }
                        Debug.Assert(numCopied == Count, "Copied all elements but number of copied elements does not match count");
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                    }
                }
            }

            bool ICollection.IsSynchronized
            {
                get { return false; }
            }

            object ICollection.SyncRoot
            {
                get { return ((ICollection)dictionary).SyncRoot; }
            }

            [Serializable]
            public struct Enumerator : IEnumerator<TValue>, System.Collections.IEnumerator
            {
                private OrderedDictionary<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TValue currentValue;

                internal Enumerator(OrderedDictionary<TKey, TValue> dictionary)
                {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = dictionary.m_firstOrderIndex;
                    currentValue = default(TValue);
                }

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    while (index != -1)
                    {
                        currentValue = dictionary.entries[index].value;
                        index = dictionary.entries[index].nextOrder;
                        return true;
                    }

                    index = -1;
                    currentValue = default(TValue);
                    return false;
                }

                public TValue Current
                {
                    get
                    {
                        return currentValue;
                    }
                }

                object System.Collections.IEnumerator.Current
                {
                    get
                    {
                        if (index == dictionary.m_firstOrderIndex || index == -1)
                        {
                            throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                        }

                        return currentValue;
                    }
                }

                void System.Collections.IEnumerator.Reset()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }
                    index = dictionary.m_firstOrderIndex;
                    currentValue = default(TValue);
                }
            }
        }
    }
}