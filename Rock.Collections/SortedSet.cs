using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using Rock.Collections.Internals;

namespace Rock.Collections
{
    internal struct Slot
    {
        public int Left;
        public int Right;
        public int Parent;
        public bool IsRed;
    }

    //
    // A binary search tree is a red-black tree if it satisfies the following red-black properties:
    // 1. Every node is either red or black
    // 2. Every leaf (nil node) is black
    // 3. If a node is red, the both its children are black
    // 4. Every simple path from a node to a descendant leaf contains the same number of black nodes
    // 
    // The basic idea of red-black tree is to represent 2-3-4 trees as standard BSTs but to add one extra bit of information  
    // per node to encode 3-nodes and 4-nodes. 
    // 4-nodes will be represented as:          B
    //                                                              R            R
    // 3 -node will be represented as:           B             or         B     
    //                                                              R          B               B       R
    // 
    // For a detailed description of the algorithm, take a look at "Algorithm" by Rebert Sedgewick.
    //
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "by design name choice")]
    [DebuggerTypeProxy(typeof(CollectionDebugView<>))]
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class SortedSet<T> : ICollection<T>, ICollection, IReadOnlyCollection<T>, ISerializable, IDeserializationCallback
    {
        #region local variables/constants
        private int m_root;
        private IComparer<T> m_comparer;
        private int m_count;
        private int m_version;
        [NonSerialized]
        private object m_syncRoot;
        private SerializationInfo m_siInfo; //A temporary variable which we need during deserialization
        private Slot[] m_slots;
        private T[] m_items;
        private int m_freeList;
        private int m_lastIndex;

        private const int MinSize = 4;
        private const int GrowFactor = 2;

        private const string ComparerName = "Comparer";
        private const string CountName = "Count";
        private const string ItemsName = "Items";
        private const string VersionName = "Version";
        //needed for enumerator
        private const string TreeName = "Tree";
        private const string NodeValueName = "Item";
        private const string EnumStartName = "EnumStarted";
        private const string ReverseName = "Reverse";
        private const string EnumVersionName = "EnumVersion";
        //needed for TreeSubset
        private const string minName = "Min";
        private const string maxName = "Max";
        private const string lBoundActiveName = "lBoundActive";
        private const string uBoundActiveName = "uBoundActive";

        #endregion

        public int Count
        {
            get { return m_count; }
        }

        public IComparer<T> Comparer
        {
            get { return m_comparer; }
        }

        /// <summary>
        /// Gets collection reader.
        /// </summary>
        public Reader Items
        {
            get { return new Reader(this); }
        }

        /// <summary>
        /// Gets reversed collection reader.
        /// </summary>
        public ReverseReader Reversed
        {
            get { return new ReverseReader(this); }
        }

        #region Constructors

        public SortedSet()
            : this(0)
        {
        }

        public SortedSet(int capacity)
            : this(capacity, null)
        {
        }

        public SortedSet(IComparer<T> comparer)
            : this(0, comparer)
        {
        }

        public SortedSet(int capacity, IComparer<T> comparer)
        {
            m_comparer = comparer ?? Comparer<T>.Default;
            m_freeList = -1;
            m_slots = new Slot[capacity];
            m_items = new T[capacity];
            m_root = -1;
        }

        public SortedSet(IEnumerable<T> collection)
            : this(collection, Comparer<T>.Default)
        {
        }

        // TODO: Optimize
        public SortedSet(IEnumerable<T> collection, IComparer<T> comparer)
            : this(comparer)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            foreach (var item in collection)
                Add(item);
        }

        //public SortedSet(IEnumerable<T> collection)
        //    : this(collection, Comparer<T>.Default)
        //{
        //}

        //public SortedSet(IEnumerable<T> collection, IComparer<T> comparer)
        //    : this(comparer)
        //{
        //    if (collection == null)
        //    {
        //        throw new ArgumentNullException(nameof(collection));
        //    }

        //    m_pool = new Slot[0];

        //    // these are explicit type checks in the mould of HashSet. It would have worked better
        //    // with something like an ISorted<T> (we could make this work for SortedList.Keys etc)
        //    SortedSet<T> baseSortedSet = collection as SortedSet<T>;
        //    if (baseSortedSet != null && AreComparersEqual(this, baseSortedSet))
        //    {
        //        //breadth first traversal to recreate nodes
        //        if (baseSortedSet.Count == 0)
        //        {
        //            return;
        //        }

        //        //pre order way to replicate nodes
        //        Stack<Slot> theirStack = new Stack<SortedSet<T>.Slot>(2 * log2(baseSortedSet.Count) + 2);
        //        Stack<Slot> myStack = new Stack<SortedSet<T>.Slot>(2 * log2(baseSortedSet.Count) + 2);
        //        Slot theirCurrent = baseSortedSet._root;
        //        Slot myCurrent = (theirCurrent != null ? new SortedSet<T>.Slot(theirCurrent.Item, theirCurrent.IsRed) : null);
        //        _root = myCurrent;
        //        if (_root != null)
        //        {
        //            _root.Parent = null;
        //        }
        //        while (theirCurrent != null)
        //        {
        //            theirStack.Push(theirCurrent);
        //            myStack.Push(myCurrent);
        //            myCurrent.Left = (theirCurrent.Left != null ? new SortedSet<T>.Slot(theirCurrent.Left.Item, theirCurrent.Left.IsRed) : null);
        //            theirCurrent = theirCurrent.Left;
        //            myCurrent = myCurrent.Left;
        //        }
        //        while (theirStack.Count != 0)
        //        {
        //            theirCurrent = theirStack.Pop();
        //            myCurrent = myStack.Pop();
        //            Slot theirRight = theirCurrent.Right;
        //            Slot myRight = null;
        //            if (theirRight != null)
        //            {
        //                myRight = new SortedSet<T>.Slot(theirRight.Item, theirRight.IsRed);
        //            }
        //            myCurrent.Right = myRight;

        //            while (theirRight != null)
        //            {
        //                theirStack.Push(theirRight);
        //                myStack.Push(myRight);
        //                myRight.Left = (theirRight.Left != null ? new SortedSet<T>.Slot(theirRight.Left.Item, theirRight.Left.IsRed) : null);
        //                theirRight = theirRight.Left;
        //                myRight = myRight.Left;
        //            }
        //        }
        //        _count = baseSortedSet._count;
        //    }
        //    else
        //    {
        //        int count;
        //        T[] els = ToArray(collection, out count);
        //        if (count > 0)
        //        {
        //            comparer = _comparer; // If comparer is null, sets it to Comparer<T>.Default
        //            Array.Sort(els, 0, count, comparer);
        //            int index = 1;
        //            for (int i = 1; i < count; i++)
        //            {
        //                if (comparer.Compare(els[i], els[i - 1]) != 0)
        //                {
        //                    els[index++] = els[i];
        //                }
        //            }
        //            count = index;

        //            _root = ConstructRootFromSortedArray(els, 0, count - 1, null);
        //            if (_root != null)
        //            {
        //                _root.Parent = null;
        //            }
        //            _count = count;
        //        }
        //    }
        //}

        protected SortedSet(SerializationInfo info, StreamingContext context)
        {
            m_siInfo = info;
        }

        //internal static T[] ToArray(IEnumerable<T> source, out int length)
        //{
        //    ICollection<T> ic = source as ICollection<T>;
        //    if (ic != null)
        //    {
        //        int count = ic.Count;
        //        if (count != 0)
        //        {
        //            T[] arr = new T[count];
        //            ic.CopyTo(arr, 0);
        //            length = count;
        //            return arr;
        //        }
        //    }
        //    else
        //    {
        //        using (var en = source.GetEnumerator())
        //        {
        //            if (en.MoveNext())
        //            {
        //                const int DefaultCapacity = 4;
        //                T[] arr = new T[DefaultCapacity];
        //                arr[0] = en.Current;
        //                int count = 1;

        //                while (en.MoveNext())
        //                {
        //                    if (count == arr.Length)
        //                    {
        //                        const int MaxArrayLength = 0x7FEFFFFF;
        //                        int newLength = count << 1;
        //                        if ((uint)newLength > MaxArrayLength)
        //                        {
        //                            newLength = MaxArrayLength <= count ? count + 1 : MaxArrayLength;
        //                        }

        //                        Array.Resize(ref arr, newLength);
        //                    }

        //                    arr[count++] = en.Current;
        //                }

        //                length = count;
        //                return arr;
        //            }
        //        }
        //    }

        //    length = 0;
        //    return null;
        //}

        //private static Slot ConstructRootFromSortedArray(T[] arr, int startIndex, int endIndex, Slot redNode)
        //{
        //    //what does this do?
        //    //you're given a sorted array... say 1 2 3 4 5 6 
        //    //2 cases:
        //    //    If there are odd # of elements, pick the middle element (in this case 4), and compute
        //    //    its left and right branches
        //    //    If there are even # of elements, pick the left middle element, save the right middle element
        //    //    and call the function on the rest
        //    //    1 2 3 4 5 6 -> pick 3, save 4 and call the fn on 1,2 and 5,6
        //    //    now add 4 as a red node to the lowest element on the right branch
        //    //             3                       3
        //    //         1       5       ->     1        5
        //    //           2       6             2     4   6            
        //    //    As we're adding to the leftmost of the right branch, nesting will not hurt the red-black properties
        //    //    Leaf nodes are red if they have no sibling (if there are 2 nodes or if a node trickles
        //    //    down to the bottom

        //    //the iterative way to do this ends up wasting more space than it saves in stack frames (at
        //    //least in what i tried)
        //    //so we're doing this recursively
        //    //base cases are described below
        //    int size = endIndex - startIndex + 1;
        //    if (size == 0)
        //    {
        //        return null;
        //    }
        //    Slot root = null;
        //    if (size == 1)
        //    {
        //        root = new Slot(arr[startIndex], false);
        //        if (redNode != null)
        //        {
        //            root.Left = redNode;
        //        }
        //    }
        //    else if (size == 2)
        //    {
        //        root = new Slot(arr[startIndex], false);
        //        root.Right = new Slot(arr[endIndex], false);
        //        root.Right.IsRed = true;
        //        if (redNode != null)
        //        {
        //            root.Left = redNode;
        //        }
        //    }
        //    else if (size == 3)
        //    {
        //        root = new Slot(arr[startIndex + 1], false);
        //        root.Left = new Slot(arr[startIndex], false);
        //        root.Right = new Slot(arr[endIndex], false);
        //        if (redNode != null)
        //        {
        //            root.Left.Left = redNode;
        //        }
        //    }
        //    else
        //    {
        //        int midpt = ((startIndex + endIndex) / 2);
        //        root = new Slot(arr[midpt], false);
        //        root.Left = ConstructRootFromSortedArray(arr, startIndex, midpt - 1, redNode);
        //        if (size % 2 == 0)
        //        {
        //            root.Right = ConstructRootFromSortedArray(arr, midpt + 2, endIndex, new Slot(arr[midpt + 1], true));
        //        }
        //        else
        //        {
        //            root.Right = ConstructRootFromSortedArray(arr, midpt + 1, endIndex, null);
        //        }
        //    }
        //    return root;
        //}

        #endregion

        #region Properties
        bool ICollection<T>.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                return false;
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                if (m_syncRoot == null)
                {
                    Interlocked.CompareExchange(ref m_syncRoot, new object(), null);
                }
                return m_syncRoot;
            }
        }
        #endregion

        #region ICollection<T> Members
        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        /// <summary>
        /// Add the value ITEM to the tree, returns true if added, false if duplicate 
        /// </summary>
        /// <param name="item">item to be added</param>        
        public bool Add(T item)
        {
            // empty tree
            if (m_root == -1)
            {
                m_root = ObtainSlot(item, false);
                m_count = 1;
                m_version++;
                return true;
            }

            // Search for a node at bottom to insert the new node. 
            // If we can guarantee the node we found is not a 4-node, it would be easy to do insertion.
            // We split 4-nodes along the search path.
            int current = m_root;
            int parent = -1;
            int grandParent = -1;
            int greatGrandParent = -1;

            //even if we don't actually add to the set, we may be altering its structure (by doing rotations
            //and such). so update version to disable any enumerators/subsets working on it
            m_version++;
            var slots = this.m_slots;

            int order = 0;
            while (current != -1)
            {
                order = m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    // We could have changed root node to red during the search process.
                    // We need to set it to black before we return.
                    slots[m_root].IsRed = false;
                    return false;
                }

                // split a 4-node into two 2-nodes                
                if (Is4Node(slots, current))
                {
                    Split4Node(slots, current);
                    // We could have introduced two consecutive red nodes after split. Fix that by rotation.
                    if (IsRed(slots, parent))
                    {
                        InsertionBalance(slots, ref m_root, current, ref parent, grandParent, greatGrandParent);
                    }
                }
                greatGrandParent = grandParent;
                grandParent = parent;
                parent = current;
                current = (order < 0) ? slots[current].Left : slots[current].Right;
            }

            Debug.Assert(parent != -1, "Parent node cannot be null here!");
            // ready to insert the new node
            int node = ObtainSlot(item);
            slots = this.m_slots;
            
            if (order > 0)
            {
                SetRight(slots, parent, node);
            }
            else
            {
                SetLeft(slots, parent, node);
            }

            // the new node will be red, so we will need to adjust the colors if parent node is also red
            if (slots[parent].IsRed)
            {
                InsertionBalance(slots, ref m_root, node, ref parent, grandParent, greatGrandParent);
            }

            // Root node is always black
            slots[m_root].IsRed = false;
            ++m_count;
            return true;
        }

        /// <summary>
        /// Remove the T ITEM from this SortedSet. Returns true if successfully removed.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            if (m_root == -1)
            {
                return false;
            }

            // Search for a node and then find its successor. 
            // Then copy the item from the successor to the matching node and delete the successor. 
            // If a node doesn't have a successor, we can replace it with its left child (if not empty.) 
            // or delete the matching node.
            // 
            // In top-down implementation, it is important to make sure the node to be deleted is not a 2-node.
            // Following code will make sure the node on the path is not a 2 Node. 

            //even if we don't actually remove from the set, we may be altering its structure (by doing rotations
            //and such). so update version to disable any enumerators/subsets working on it
            m_version++;

            var slots = m_slots; // local copy helps a lot
            int current = m_root;
            int parent = -1;
            int grandParent = -1;
            int match = -1;
            int parentOfMatch = -1;
            bool foundMatch = false;
            while (current != -1)
            {
                if (Is2Node(slots, current))
                { 
                    // fix up 2-Node
                    if (parent == -1)
                    {   
                        // current is root. Mark it as red
                        slots[current].IsRed = true;
                    }
                    else
                    {
                        int sibling = GetSibling(slots, current, parent);
                        if (slots[sibling].IsRed)
                        {
                            // If parent is a 3-node, flip the orientation of the red link. 
                            // We can achieve this by a single rotation        
                            // This case is converted to one of other cased below.
                            Debug.Assert(!slots[parent].IsRed, "parent must be a black node!");
                            if (slots[parent].Right == sibling)
                            {
                                RotateLeft(slots, parent);
                            }
                            else
                            {
                                RotateRight(slots, parent);
                            }

                            slots[parent].IsRed = true;
                            slots[sibling].IsRed = false;    // parent's color
                            // sibling becomes child of grandParent or root after rotation. Update link from grandParent or root
                            ReplaceChildOfNodeOrRoot(slots, ref m_root, grandParent, parent, sibling);
                            // sibling will become grandParent of current node 
                            grandParent = sibling;
                            if (parent == match)
                            {
                                parentOfMatch = sibling;
                            }

                            // update sibling, this is necessary for following processing
                            sibling = (slots[parent].Left == current) ? slots[parent].Right : slots[parent].Left;
                        }
                        Debug.Assert(sibling != -1 && slots[sibling].IsRed == false, "sibling must not be null and it must be black!");

                        if (Is2Node(slots, sibling))
                        {
                            Merge2Nodes(slots, parent, current, sibling);
                        }
                        else
                        {
                            // current is a 2-node and sibling is either a 3-node or a 4-node.
                            // We can change the color of current to red by some rotation.

                            int newGrandParent = -1;
                            Debug.Assert(IsRed(slots, slots[sibling].Left) || IsRed(slots, slots[sibling].Right), "sibling must have at least one red child");
                            if (IsRed(slots, slots[sibling].Left))
                            {
                                if (slots[parent].Left == current)
                                {
                                    Debug.Assert(slots[parent].Right == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(slots[slots[sibling].Left].IsRed, "Left child of sibling must be red!");
                                    newGrandParent = RotateRightLeft(slots, parent);
                                }
                                else
                                {
                                    Debug.Assert(slots[parent].Left == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(slots[slots[sibling].Left].IsRed, "Left child of sibling must be red!");
                                    slots[slots[sibling].Left].IsRed = false;
                                    newGrandParent = RotateRight(slots, parent);
                                }
                            }
                            else
                            {
                                if (slots[parent].Left == current)
                                {
                                    Debug.Assert(slots[parent].Right == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(slots[slots[sibling].Right].IsRed, "Right child of sibling must be red!");
                                    slots[slots[sibling].Right].IsRed = false;
                                    newGrandParent = RotateLeft(slots, parent);
                                }
                                else
                                {
                                    Debug.Assert(slots[parent].Left == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(slots[slots[sibling].Right].IsRed, "Right child of sibling must be red!");
                                    newGrandParent = RotateLeftRight(slots, parent);
                                }
                            }
                            
                            slots[newGrandParent].IsRed = slots[parent].IsRed;
                            slots[parent].IsRed = false;
                            slots[current].IsRed = true;
                            ReplaceChildOfNodeOrRoot(slots, ref m_root, grandParent, parent, newGrandParent);
                            if (parent == match)
                            {
                                parentOfMatch = newGrandParent;
                            }
                            grandParent = newGrandParent;
                        }
                    }
                }

                // we don't need to compare any more once we found the match
                int order = foundMatch ? -1 : m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    // save the matching node
                    foundMatch = true;
                    match = current;
                    parentOfMatch = parent;
                }

                grandParent = parent;
                parent = current;

                if (order < 0)
                {
                    current = slots[current].Left;
                }
                else
                {
                    current = slots[current].Right;       // continue the search in  right sub tree after we find a match
                }
            }

            // move successor to the matching node position and replace links
            if (match != -1)
            {
                ReplaceNode(slots, ref m_root, match, parentOfMatch, parent, grandParent);
                --m_count;
                ReturnSlot(match);
            }

            if (m_root != -1)
            {
                slots[m_root].IsRed = false;
            }
            return foundMatch;
        }

        public void Clear()
        {
            if (m_lastIndex > 0)
            {
                // clear the elements so that the gc can reclaim the references.
                // clear only up to m_lastIndex for m_slots
                Array.Clear(m_items, 0, m_lastIndex);
                m_lastIndex = 0;
                m_count = 0;
                m_freeList = -1;
                m_root = -1;
            }
            ++m_version;
        }

        public bool Contains(T item)
        {
            return FindNode(item) != -1;
        }

        public void CopyTo(T[] array)
        {
            CopyTo(array, 0, Count);
        }

        public void CopyTo(T[] array, int index)
        {
            CopyTo(array, index, Count);
        }

        public void CopyTo(T[] array, int index, int count)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_NeedNonNegNum");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "ArgumentOutOfRange_NeedNonNegNum");
            }

            // will array, starting at arrayIndex, be able to hold elements? Note: not
            // checking arrayIndex >= array.Length (consistency with list of allowing
            // count of 0; subsequent check takes care of the rest)
            if (index > array.Length || count > array.Length - index)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            Enumerator e = GetEnumerator();
            for (int i = 0; i < count && e.MoveNext(); i++)
            {
                array[index + i] = e.Current;
            }
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

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "ArgumentOutOfRange_NeedNonNegNum");
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            T[] tarray = array as T[];
            if (tarray != null)
            {
                CopyTo(tarray, index);
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
                    Enumerator e = GetEnumerator();
                    for (int i = 0; e.MoveNext(); i++)
                    {
                        objects[index + i] = e.Current;
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException("Argument_InvalidArrayType", nameof(array));
                }
            }
        }

        #endregion

        #region IEnumerable<T> members
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }
        #endregion

        #region Tree Specific Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSibling(Slot[] m_slots, int node, int parent)
        {
            if (m_slots[parent].Left == node)
            {
                return m_slots[parent].Right;
            }
            return m_slots[parent].Left;
        }

        // After calling InsertionBalance, we need to make sure current and parent up-to-date.
        // It doesn't matter if we keep grandParent and greatGrantParent up-to-date 
        // because we won't need to split again in the next node.
        // By the time we need to split again, everything will be correctly set.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InsertionBalance(Slot[] m_slots, ref int m_root, int current, ref int parent, int grandParent, int greatGrandParent)
        {
            Debug.Assert(grandParent != -1, "Grand parent cannot be null here!");
            bool parentIsOnRight = (m_slots[grandParent].Right == parent);
            bool currentIsOnRight = (m_slots[parent].Right == current);

            int newChildOfGreatGrandParent;
            if (parentIsOnRight == currentIsOnRight)
            { // same orientation, single rotation
                newChildOfGreatGrandParent = currentIsOnRight ? RotateLeft(m_slots, grandParent) : RotateRight(m_slots, grandParent);
            }
            else
            {  // different orientation, double rotation
                newChildOfGreatGrandParent = currentIsOnRight ? RotateLeftRight(m_slots, grandParent) : RotateRightLeft(m_slots, grandParent);
                // current node now becomes the child of greatgrandparent 
                parent = greatGrandParent;
            }
            // grand parent will become a child of either parent of current.
            m_slots[grandParent].IsRed = true;
            m_slots[newChildOfGreatGrandParent].IsRed = false;

            ReplaceChildOfNodeOrRoot(m_slots, ref m_root, greatGrandParent, grandParent, newChildOfGreatGrandParent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Is2Node(Slot[] m_slots, int node)
        {
            Debug.Assert(node != -1, "node cannot be null!");
            return IsBlack(m_slots, node) && IsNullOrBlack(m_slots, m_slots[node].Left) && IsNullOrBlack(m_slots, m_slots[node].Right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Is4Node(Slot[] m_slots, int node)
        {
            return IsRed(m_slots, m_slots[node].Left) && IsRed(m_slots, m_slots[node].Right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBlack(Slot[] m_slots, int node)
        {
            return (node != -1 && !m_slots[node].IsRed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNullOrBlack(Slot[] m_slots, int node)
        {
            return (node == -1 || !m_slots[node].IsRed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRed(Slot[] m_slots, int node)
        {
            return (node != -1 && m_slots[node].IsRed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Merge2Nodes(Slot[] m_slots, int parent, int child1, int child2)
        {
            Debug.Assert(IsRed(m_slots, parent), "parent must be red");
            // combing two 2-nodes into a 4-node
            m_slots[parent].IsRed = false;
            m_slots[child1].IsRed = true;
            m_slots[child2].IsRed = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int ObtainSlot(T item, bool isRed = true)
        {
            int index;
            if (m_freeList >= 0)
            {
                index = m_freeList;
                m_freeList = m_slots[index].Parent; // Next free node is stored in Parent field
            }
            else
            {
                if (m_lastIndex == m_slots.Length)
                {
                    var newSize = Math.Max(MinSize, GrowFactor * m_slots.Length);
                    Array.Resize(ref m_slots, newSize);
                    Array.Resize(ref m_items, newSize);
                }
                index = m_lastIndex;
                m_lastIndex++;
            }

            m_items[index] = item;
            m_slots[index].IsRed = isRed;
            m_slots[index].Left = -1;
            m_slots[index].Right = -1;
            m_slots[index].Parent = -1;
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ReturnSlot(int index)
        {
            m_items[index] = default(T);
            m_slots[index].Parent = m_freeList; // Next free node is stored in Parent field
            m_freeList = index;
        }

        // Replace the child of a parent node. 
        // If the parent node is null, replace the root.        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReplaceChildOfNodeOrRoot(Slot[] m_slots, ref int m_root, int parent, int child, int newChild)
        {
            if (parent != -1)
            {
                if (m_slots[parent].Left == child)
                {
                    SetLeft(m_slots, parent, newChild);
                }
                else
                {
                    SetRight(m_slots, parent, newChild);
                }
            }
            else
            {
                m_root = newChild;
                if (m_root != -1)
                {
                    m_slots[m_root].Parent = -1;
                }
            }
        }

        // Replace the matching node with its successor.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReplaceNode(Slot[] m_slots, ref int m_root, int match, int parentOfMatch, int successor, int parentOfsuccessor)
        {
            if (successor == match)
            {  // this node has no successor, should only happen if right child of matching node is null.
                Debug.Assert(m_slots[match].Right == -1, "Right child must be null!");
                successor = m_slots[match].Left;
            }
            else
            {
                Debug.Assert(parentOfsuccessor != -1, "parent of successor cannot be null!");
                Debug.Assert(m_slots[successor].Left == -1, "Left child of successor must be null!");
                Debug.Assert((m_slots[successor].Right == -1 && m_slots[successor].IsRed) || (m_slots[m_slots[successor].Right].IsRed && !m_slots[successor].IsRed), "Successor must be in valid state");
                if (m_slots[successor].Right != -1)
                {
                    m_slots[m_slots[successor].Right].IsRed = false;
                }

                if (parentOfsuccessor != match)
                {   // detach successor from its parent and set its right child
                    SetLeft(m_slots, parentOfsuccessor, m_slots[successor].Right);
                    SetRight(m_slots, successor, m_slots[match].Right);
                }

                SetLeft(m_slots, successor, m_slots[match].Left);
            }

            if (successor != -1)
            {
                m_slots[successor].IsRed = m_slots[match].IsRed;
            }

            ReplaceChildOfNodeOrRoot(m_slots, ref m_root, parentOfMatch, match, successor);
        }

        internal int FindNode(T item)
        {
            int current = m_root;
            while (current != -1)
            {
                int order = m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    return current;
                }
                else
                {
                    current = (order < 0) ? m_slots[current].Left : m_slots[current].Right;
                }
            }

            return -1;
        }

        //used for bithelpers. Note that this implementation is completely different 
        //from the Subset's. The two should not be mixed. This indexes as if the tree were an array.
        //http://en.wikipedia.org/wiki/Binary_Tree#Methods_for_storing_binary_trees
        internal int InternalIndexOf(T item)
        {
            int current = m_root;
            int count = 0;
            while (current != -1)
            {
                int order = m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    return count;
                }
                else
                {
                    current = (order < 0) ? m_slots[current].Left : m_slots[current].Right;
                    count = (order < 0) ? (2 * count + 1) : (2 * count + 2);
                }
            }
            return -1;
        }

        public Node FindNext(T item)
        {
            int current = m_root;
            while (current != -1)
            {
                int order = m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    return new Node(this, current).Next;
                }
                if (order > 0)
                {
                    if (m_slots[current].Right == -1)
                        return new Node(this, current).Next;
                    else
                        current = m_slots[current].Right;
                }
                else
                {
                    if (m_slots[current].Left == -1)
                        return new Node(this, current);
                    else
                        current = m_slots[current].Left;
                }
            }
            return new Node(this, -1);
        }

        public Node FindPrevious(T item)
        {
            int current = m_root;
            while (current != -1)
            {
                int order = m_comparer.Compare(item, m_items[current]);
                if (order == 0)
                {
                    return new Node(this, current).Previous;
                }
                if (order > 0)
                {
                    if (m_slots[current].Right == -1)
                        return new Node(this, current);
                    else
                        current = m_slots[current].Right;
                }
                else
                {
                    if (m_slots[current].Left == -1)
                        return new Node(this, current).Previous;
                    else
                        current = m_slots[current].Left;
                }
            }
            return new Node(this, -1);
        }

        internal int FindRange(T from, T to)
        {
            return FindRange(from, to, true, true);
        }

        internal int FindRange(T from, T to, bool lowerBoundActive, bool upperBoundActive)
        {
            int current = m_root;
            while (current != -1)
            {
                if (lowerBoundActive && m_comparer.Compare(from, m_items[current]) > 0)
                {
                    current = m_slots[current].Right;
                }
                else
                {
                    if (upperBoundActive && m_comparer.Compare(to, m_items[current]) < 0)
                    {
                        current = m_slots[current].Left;
                    }
                    else
                    {
                        return current;
                    }
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RotateLeft(Slot[] m_slots, int node)
        {
            int x = m_slots[node].Right;
            SetRight(m_slots, node, m_slots[x].Left);
            m_slots[x].Left = node;
            m_slots[node].Parent = x;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RotateLeftRight(Slot[] m_slots, int node)
        {
            int child = m_slots[node].Left;
            int grandChild = m_slots[child].Right;

            SetLeft(m_slots, node, m_slots[grandChild].Right);
            SetRight(m_slots, grandChild, node);
            SetRight(m_slots, child, m_slots[grandChild].Left);
            SetLeft(m_slots, grandChild, child);
            return grandChild;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RotateRight(Slot[] m_slots, int node)
        {
            int x = m_slots[node].Left;
            SetLeft(m_slots, node, m_slots[x].Right);
            m_slots[x].Right = node;
            m_slots[node].Parent = x;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RotateRightLeft(Slot[] m_slots, int node)
        {
            int child = m_slots[node].Right;
            int grandChild = m_slots[child].Left;

            SetRight(m_slots, node, m_slots[grandChild].Left);
            SetLeft(m_slots, grandChild, node);
            SetLeft(m_slots, child, m_slots[grandChild].Right);
            SetRight(m_slots, grandChild, child);
            return grandChild;
        }

        /// <summary>
        /// Used for deep equality of SortedSet testing
        /// </summary>
        /// <returns></returns>
        public static IEqualityComparer<SortedSet<T>> CreateSetComparer()
        {
            return new SortedSetEqualityComparer<T>();
        }

        /// <summary>
        /// Create a new set comparer for this set, where this set's members' equality is defined by the
        /// memberEqualityComparer. Note that this equality comparer's definition of equality must be the
        /// same as this set's Comparer's definition of equality
        /// </summary>                
        public static IEqualityComparer<SortedSet<T>> CreateSetComparer(IEqualityComparer<T> memberEqualityComparer)
        {
            return new SortedSetEqualityComparer<T>(memberEqualityComparer);
        }

        /// <summary>
        /// Decides whether these sets are the same, given the comparer. If the EC's are the same, we can
        /// just use SetEquals, but if they aren't then we have to manually check with the given comparer
        /// </summary>        
        internal static bool SortedSetEquals(SortedSet<T> set1, SortedSet<T> set2, IComparer<T> comparer)
        {
            // handle null cases first
            if (set1 == null)
            {
                return (set2 == null);
            }
            else if (set2 == null)
            {
                // set1 != null
                return false;
            }

            if (AreComparersEqual(set1, set2))
            {
                if (set1.Count != set2.Count)
                    return false;

                return set1.SetEquals(set2);
            }
            else
            {
                bool found = false;
                foreach (T item1 in set1)
                {
                    found = false;
                    foreach (T item2 in set2)
                    {
                        if (comparer.Compare(item1, item2) == 0)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        return false;
                }
                return true;
            }

        }

        //This is a little frustrating because we can't support more sorted structures
        private static bool AreComparersEqual(SortedSet<T> set1, SortedSet<T> set2)
        {
            return set1.Comparer.Equals(set2.Comparer);
        }

        private static void Split4Node(Slot[] m_slots, int node)
        {
            m_slots[node].IsRed = true;
            m_slots[m_slots[node].Left].IsRed = false;
            m_slots[m_slots[node].Right].IsRed = false;
        }

        public void TrimExcess()
        {
            Array.Resize(ref m_slots, m_lastIndex);
            Array.Resize(ref m_items, m_lastIndex);
        }

        /// <summary>
        /// Checks whether this Tree has all elements in common with IEnumerable other
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool SetEquals(SortedSet<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (!Comparer.Equals(other.Comparer))
            {
                throw new ArgumentException("Other set does not have same comparer", nameof(other));
            }

            Enumerator mine = GetEnumerator();
            Enumerator theirs = other.GetEnumerator();
            bool mineEnded = !mine.MoveNext();
            bool theirsEnded = !theirs.MoveNext();
            while (!mineEnded && !theirsEnded)
            {
                if (Comparer.Compare(mine.Current, theirs.Current) != 0)
                {
                    return false;
                }
                mineEnded = !mine.MoveNext();
                theirsEnded = !theirs.MoveNext();
            }
            return mineEnded && theirsEnded;
        }
        #endregion

        #region ISorted Members
        public T Min
        {
            get
            {
                if (m_root == -1)
                {
                    return default(T);
                }

                int current = m_root;
                while (m_slots[current].Left != -1)
                {
                    current = m_slots[current].Left;
                }

                return m_items[current];
            }
        }

        public T Max
        {
            get
            {
                if (m_root == -1)
                {
                    return default(T);
                }

                int current = m_root;
                while (m_slots[current].Right != -1)
                {
                    current = m_slots[current].Right;
                }

                return m_items[current];
            }
        }

        public Node FirstNode
        {
            get { return new Node(this, GetFirst()); }
        }

        public Node LastNode
        {
            get { return new Node(this, GetLast()); }
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            GetObjectData(info, context);
        }

        protected void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(CountName, m_count); //This is the length of the bucket array.
            info.AddValue(ComparerName, m_comparer, typeof(IComparer<T>));
            info.AddValue(VersionName, m_version);

            if (m_root != -1)
            {
                T[] items = new T[Count];
                CopyTo(items, 0);
                info.AddValue(ItemsName, items, typeof(T[]));
            }
        }

        void IDeserializationCallback.OnDeserialization(Object sender)
        {
            OnDeserialization(sender);
        }

        protected void OnDeserialization(Object sender)
        {
            if (m_comparer != null)
            {
                return; // Somebody had a dependency on this class and fixed us up before the ObjectManager got to it.
            }

            if (m_siInfo == null)
            {
                throw new SerializationException("Serialization_InvalidOnDeser");
            }

            m_comparer = (IComparer<T>)m_siInfo.GetValue(ComparerName, typeof(IComparer<T>));
            int savedCount = m_siInfo.GetInt32(CountName);

            if (savedCount != 0)
            {
                T[] items = (T[])m_siInfo.GetValue(ItemsName, typeof(T[]));

                if (items == null)
                {
                    throw new SerializationException("Serialization_MissingValues");
                }

                for (int i = 0; i < items.Length; i++)
                {
                    Add(items[i]);
                }
            }

            m_version = m_siInfo.GetInt32(VersionName);
            if (m_count != savedCount)
            {
                throw new SerializationException("Serialization_MismatchedCount");
            }

            m_siInfo = null;
        }
        #endregion

        #region Enumeration helpers
        int GetFirst()
        {
            if (m_root == -1)
                return -1;

            // Slide left
            var result = m_root;
            while (m_slots[result].Left != -1)
            {
                result = m_slots[result].Left;
            }
            return result;
        }

        int GetLast()
        {
            if (m_root == -1)
                return -1;

            // Slide Right
            var result = m_root;
            while (m_slots[result].Right != -1)
            {
                result = m_slots[result].Right;
            }
            return result;
        }

        int GetNext(int node)
        {
            // Has right node
            if (m_slots[node].Right != -1)
            {
                // Move to right
                node = m_slots[node].Right;

                // Slide left
                while (m_slots[node].Left != -1)
                {
                    node = m_slots[node].Left;
                }
                return node;
            }

            // While not root
            while (m_slots[node].Parent != -1)
            {
                // Is left child
                if (m_slots[m_slots[node].Parent].Left == node)
                {
                    // Continue with parent then
                    node = m_slots[node].Parent;
                    return node;
                }
                else
                {
                    // Is right child, go up then
                    node = m_slots[node].Parent;
                }
            }
            return -1;
        }

        int GetPrevious(int node)
        {
            // Has Left node
            if (m_slots[node].Left != -1)
            {
                // Move to Left
                node = m_slots[node].Left;

                // Slide Right
                while (m_slots[node].Right != -1)
                {
                    node = m_slots[node].Right;
                }
                return node;
            }

            // While not root
            while (m_slots[node].Parent != -1)
            {
                // Is Right child
                if (m_slots[m_slots[node].Parent].Right == node)
                {
                    // Continue with parent then
                    node = m_slots[node].Parent;
                    return node;
                }
                else
                {
                    // Is Left child, go up then
                    node = m_slots[node].Parent;
                }
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetLeft(Slot[] m_slots, int slot, int value)
        {
            m_slots[slot].Left = value;
            if (value != -1)
                m_slots[value].Parent = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SetRight(Slot[] m_slots, int slot, int value)
        {
            m_slots[slot].Right = value;
            if (value != -1)
                m_slots[value].Parent = slot;
        }

        #endregion

        #region Helper Classes
        public struct Node
        {
            SortedSet<T> m_tree;
            int m_node;
            int m_version;

            public bool IsNull
            {
                get { return m_node == -1; }
            }

            /// <summary>
            /// Gets node value.
            /// </summary>
            public T Value
            {
                get
                {
                    CheckVersion();
                    return m_tree.m_items[m_node];
                }
            }

            /// <summary>
            /// Gets next in-order node (node with bigger key).
            /// </summary>
            public Node Next
            {
                get
                {
                    CheckVersion();
                    return new Node(m_tree, m_tree.GetNext(m_node));
                }
            }

            /// <summary>
            /// Gets previous in-order node (node with smaller key).
            /// </summary>
            public Node Previous
            {
                get
                {
                    CheckVersion();
                    return new Node(m_tree, m_tree.GetPrevious(m_node));
                }
            }

            internal Node(SortedSet<T> tree, int node)
            {
                m_tree = tree;
                m_node = node;
                m_version = tree.m_version;
            }

            void CheckVersion()
            {
                if (m_version != m_tree.m_version)
                {
                    throw new InvalidOperationException("Collection has been modified.");
                }
            }
        }

        public struct Reader : IReadOnlyCollection<T>
        {
            private SortedSet<T> m_set;

            public int Count { get { return m_set.Count; } }

            public Reader(SortedSet<T> set)
            {
                this.m_set = set;
            }

            public bool Contains(T item)
            {
                return m_set.Contains(item);
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(m_set);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public struct ReverseReader : IReadOnlyCollection<T>
        {
            private SortedSet<T> m_set;

            public int Count { get { return m_set.Count; } }

            public ReverseReader(SortedSet<T> set)
            {
                this.m_set = set;
            }

            public bool Contains(T item)
            {
                return m_set.Contains(item);
            }

            public ReverseEnumerator GetEnumerator()
            {
                return new ReverseEnumerator(m_set);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "not an expected scenario")]
        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private SortedSet<T> m_tree;
            private int m_version;
            private int m_index;

            public T Current
            {
                get { return m_tree.m_items[m_index]; }
            }

            internal Enumerator(SortedSet<T> set)
            {
                m_tree = set;
                m_version = m_tree.m_version;
                m_index = -1;
            }

            public bool MoveNext()
            {
                if (m_version != m_tree.m_version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                m_index = m_index == -1 ? m_tree.GetFirst() : m_tree.GetNext(m_index);
                return m_index != -1;
            }

            void IDisposable.Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (m_index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }
                    return Current;
                }
            }

            void IEnumerator.Reset()
            {
                if (m_version != m_tree.m_version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }
                m_index = -1;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "not an expected scenario")]
        public struct ReverseEnumerator : IEnumerator<T>, IEnumerator
        {
            private SortedSet<T> m_tree;
            private int m_version;
            private int m_index;

            public T Current
            {
                get { return m_tree.m_items[m_index]; }
            }

            internal ReverseEnumerator(SortedSet<T> set)
            {
                m_tree = set;
                m_version = m_tree.m_version;
                m_index = -1;
            }

            public bool MoveNext()
            {
                if (m_version != m_tree.m_version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                m_index = m_index == -1 ? m_tree.GetLast() : m_tree.GetPrevious(m_index);
                return m_index != -1;
            }

            void IDisposable.Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (m_index == -1)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }
                    return Current;
                }
            }

            void IEnumerator.Reset()
            {
                if (m_version != m_tree.m_version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }
                m_index = -1;
            }
        }
        #endregion

        #region misc
        // used for set checking operations (using enumerables) that rely on counting
        private static int log2(int value)
        {
            int c = 0;
            while (value > 0)
            {
                c++;
                value >>= 1;
            }
            return c;
        }
        #endregion
    }

    /// <summary>
    /// A class that generates an IEqualityComparer for this SortedSet. Requires that the definition of
    /// equality defined by the IComparer for this SortedSet be consistent with the default IEqualityComparer
    /// for the type T. If not, such an IEqualityComparer should be provided through the constructor.
    /// </summary>    
    internal sealed class SortedSetEqualityComparer<T> : IEqualityComparer<SortedSet<T>>
    {
        private readonly IComparer<T> _comparer;
        private readonly IEqualityComparer<T> _memberEqualityComparer;

        public SortedSetEqualityComparer() : this(null, null) { }

        public SortedSetEqualityComparer(IEqualityComparer<T> memberEqualityComparer) : this(null, memberEqualityComparer) { }

        /// <summary>
        /// Create a new SetEqualityComparer, given a comparer for member order and another for member equality (these
        /// must be consistent in their definition of equality)
        /// </summary>        
        private SortedSetEqualityComparer(IComparer<T> comparer, IEqualityComparer<T> memberEqualityComparer)
        {
            _comparer = comparer ?? Comparer<T>.Default;
            _memberEqualityComparer = memberEqualityComparer ?? EqualityComparer<T>.Default;
        }

        // using comparer to keep equals properties in tact; don't want to choose one of the comparers
        public bool Equals(SortedSet<T> x, SortedSet<T> y)
        {
            return SortedSet<T>.SortedSetEquals(x, y, _comparer);
        }

        //IMPORTANT: this part uses the fact that GetHashCode() is consistent with the notion of equality in
        //the set
        public int GetHashCode(SortedSet<T> obj)
        {
            int hashCode = 0;
            if (obj != null)
            {
                foreach (T t in obj)
                {
                    hashCode = hashCode ^ (_memberEqualityComparer.GetHashCode(t) & 0x7FFFFFFF);
                }
            } // else returns hashcode of 0 for null HashSets
            return hashCode;
        }

        // Equals method for the comparer itself. 
        public override bool Equals(object obj)
        {
            SortedSetEqualityComparer<T> comparer = obj as SortedSetEqualityComparer<T>;
            if (comparer == null)
            {
                return false;
            }
            return (_comparer == comparer._comparer);
        }

        public override int GetHashCode()
        {
            return _comparer.GetHashCode() ^ _memberEqualityComparer.GetHashCode();
        }
    }
}