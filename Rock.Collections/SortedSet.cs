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
        private NodeInternal _root;
        private IComparer<T> _comparer;
        private int _count;
        private int _version;
        [NonSerialized]
        private object _syncRoot;
        private SerializationInfo _siInfo; //A temporary variable which we need during deserialization
        private NodeInternal[] m_pool;
        private int m_poolCount;

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
            get { return _count; }
        }

        public IComparer<T> Comparer
        {
            get { return _comparer; }
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
            m_pool = new NodeInternal[capacity];
            if (comparer == null)
            {
                _comparer = Comparer<T>.Default;
            }
            else
            {
                _comparer = comparer;
            }
        }

        public SortedSet(IEnumerable<T> collection)
            : this(collection, Comparer<T>.Default)
        {
        }

        public SortedSet(IEnumerable<T> collection, IComparer<T> comparer)
            : this(comparer)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            m_pool = new NodeInternal[0];

            // these are explicit type checks in the mould of HashSet. It would have worked better
            // with something like an ISorted<T> (we could make this work for SortedList.Keys etc)
            SortedSet<T> baseSortedSet = collection as SortedSet<T>;
            if (baseSortedSet != null && AreComparersEqual(this, baseSortedSet))
            {
                //breadth first traversal to recreate nodes
                if (baseSortedSet.Count == 0)
                {
                    return;
                }

                //pre order way to replicate nodes
                Stack<NodeInternal> theirStack = new Stack<SortedSet<T>.NodeInternal>(2 * log2(baseSortedSet.Count) + 2);
                Stack<NodeInternal> myStack = new Stack<SortedSet<T>.NodeInternal>(2 * log2(baseSortedSet.Count) + 2);
                NodeInternal theirCurrent = baseSortedSet._root;
                NodeInternal myCurrent = (theirCurrent != null ? new SortedSet<T>.NodeInternal(theirCurrent.Item, theirCurrent.IsRed) : null);
                _root = myCurrent;
                if (_root != null)
                {
                    _root.Parent = null;
                }
                while (theirCurrent != null)
                {
                    theirStack.Push(theirCurrent);
                    myStack.Push(myCurrent);
                    myCurrent.Left = (theirCurrent.Left != null ? new SortedSet<T>.NodeInternal(theirCurrent.Left.Item, theirCurrent.Left.IsRed) : null);
                    theirCurrent = theirCurrent.Left;
                    myCurrent = myCurrent.Left;
                }
                while (theirStack.Count != 0)
                {
                    theirCurrent = theirStack.Pop();
                    myCurrent = myStack.Pop();
                    NodeInternal theirRight = theirCurrent.Right;
                    NodeInternal myRight = null;
                    if (theirRight != null)
                    {
                        myRight = new SortedSet<T>.NodeInternal(theirRight.Item, theirRight.IsRed);
                    }
                    myCurrent.Right = myRight;

                    while (theirRight != null)
                    {
                        theirStack.Push(theirRight);
                        myStack.Push(myRight);
                        myRight.Left = (theirRight.Left != null ? new SortedSet<T>.NodeInternal(theirRight.Left.Item, theirRight.Left.IsRed) : null);
                        theirRight = theirRight.Left;
                        myRight = myRight.Left;
                    }
                }
                _count = baseSortedSet._count;
            }
            else
            {
                int count;
                T[] els = ToArray(collection, out count);
                if (count > 0)
                {
                    comparer = _comparer; // If comparer is null, sets it to Comparer<T>.Default
                    Array.Sort(els, 0, count, comparer);
                    int index = 1;
                    for (int i = 1; i < count; i++)
                    {
                        if (comparer.Compare(els[i], els[i - 1]) != 0)
                        {
                            els[index++] = els[i];
                        }
                    }
                    count = index;

                    _root = ConstructRootFromSortedArray(els, 0, count - 1, null);
                    if (_root != null)
                    {
                        _root.Parent = null;
                    }
                    _count = count;
                }
            }
        }

        protected SortedSet(SerializationInfo info, StreamingContext context)
        {
            _siInfo = info;
        }

        internal static T[] ToArray(IEnumerable<T> source, out int length)
        {
            ICollection<T> ic = source as ICollection<T>;
            if (ic != null)
            {
                int count = ic.Count;
                if (count != 0)
                {
                    T[] arr = new T[count];
                    ic.CopyTo(arr, 0);
                    length = count;
                    return arr;
                }
            }
            else
            {
                using (var en = source.GetEnumerator())
                {
                    if (en.MoveNext())
                    {
                        const int DefaultCapacity = 4;
                        T[] arr = new T[DefaultCapacity];
                        arr[0] = en.Current;
                        int count = 1;

                        while (en.MoveNext())
                        {
                            if (count == arr.Length)
                            {
                                const int MaxArrayLength = 0x7FEFFFFF;
                                int newLength = count << 1;
                                if ((uint)newLength > MaxArrayLength)
                                {
                                    newLength = MaxArrayLength <= count ? count + 1 : MaxArrayLength;
                                }

                                Array.Resize(ref arr, newLength);
                            }

                            arr[count++] = en.Current;
                        }

                        length = count;
                        return arr;
                    }
                }
            }

            length = 0;
            return null;
        }

        private static NodeInternal ConstructRootFromSortedArray(T[] arr, int startIndex, int endIndex, NodeInternal redNode)
        {
            //what does this do?
            //you're given a sorted array... say 1 2 3 4 5 6 
            //2 cases:
            //    If there are odd # of elements, pick the middle element (in this case 4), and compute
            //    its left and right branches
            //    If there are even # of elements, pick the left middle element, save the right middle element
            //    and call the function on the rest
            //    1 2 3 4 5 6 -> pick 3, save 4 and call the fn on 1,2 and 5,6
            //    now add 4 as a red node to the lowest element on the right branch
            //             3                       3
            //         1       5       ->     1        5
            //           2       6             2     4   6            
            //    As we're adding to the leftmost of the right branch, nesting will not hurt the red-black properties
            //    Leaf nodes are red if they have no sibling (if there are 2 nodes or if a node trickles
            //    down to the bottom

            //the iterative way to do this ends up wasting more space than it saves in stack frames (at
            //least in what i tried)
            //so we're doing this recursively
            //base cases are described below
            int size = endIndex - startIndex + 1;
            if (size == 0)
            {
                return null;
            }
            NodeInternal root = null;
            if (size == 1)
            {
                root = new NodeInternal(arr[startIndex], false);
                if (redNode != null)
                {
                    root.Left = redNode;
                }
            }
            else if (size == 2)
            {
                root = new NodeInternal(arr[startIndex], false);
                root.Right = new NodeInternal(arr[endIndex], false);
                root.Right.IsRed = true;
                if (redNode != null)
                {
                    root.Left = redNode;
                }
            }
            else if (size == 3)
            {
                root = new NodeInternal(arr[startIndex + 1], false);
                root.Left = new NodeInternal(arr[startIndex], false);
                root.Right = new NodeInternal(arr[endIndex], false);
                if (redNode != null)
                {
                    root.Left.Left = redNode;
                }
            }
            else
            {
                int midpt = ((startIndex + endIndex) / 2);
                root = new NodeInternal(arr[midpt], false);
                root.Left = ConstructRootFromSortedArray(arr, startIndex, midpt - 1, redNode);
                if (size % 2 == 0)
                {
                    root.Right = ConstructRootFromSortedArray(arr, midpt + 2, endIndex, new NodeInternal(arr[midpt + 1], true));
                }
                else
                {
                    root.Right = ConstructRootFromSortedArray(arr, midpt + 1, endIndex, null);
                }
            }
            return root;
        }

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
                if (_syncRoot == null)
                {
                    Interlocked.CompareExchange(ref _syncRoot, new object(), null);
                }
                return _syncRoot;
            }
        }
        #endregion

        #region ICollection<T> Members
        /// <summary>
        /// Add the value ITEM to the tree, returns true if added, false if duplicate 
        /// </summary>
        /// <param name="item">item to be added</param> 
        public bool Add(T item)
        {
            return AddIfNotPresent(item);
        }

        void ICollection<T>.Add(T item)
        {
            AddIfNotPresent(item);
        }

        /// <summary>
        /// Adds ITEM to the tree if not already present. Returns TRUE if value was successfully added         
        /// or FALSE if it is a duplicate
        /// </summary>        
        internal bool AddIfNotPresent(T item)
        {
            if (_root == null)
            {   // empty tree
                _root = ObtainNode(item, false);
                _count = 1;
                _version++;
                return true;
            }

            // Search for a node at bottom to insert the new node. 
            // If we can guarantee the node we found is not a 4-node, it would be easy to do insertion.
            // We split 4-nodes along the search path.
            NodeInternal current = _root;
            NodeInternal parent = null;
            NodeInternal grandParent = null;
            NodeInternal greatGrandParent = null;

            //even if we don't actually add to the set, we may be altering its structure (by doing rotations
            //and such). so update version to disable any enumerators/subsets working on it
            _version++;

            int order = 0;
            while (current != null)
            {
                order = _comparer.Compare(item, current.Item);
                if (order == 0)
                {
                    // We could have changed root node to red during the search process.
                    // We need to set it to black before we return.
                    _root.IsRed = false;
                    return false;
                }

                // split a 4-node into two 2-nodes                
                if (Is4Node(current))
                {
                    Split4Node(current);
                    // We could have introduced two consecutive red nodes after split. Fix that by rotation.
                    if (IsRed(parent))
                    {
                        InsertionBalance(current, ref parent, grandParent, greatGrandParent);
                    }
                }
                greatGrandParent = grandParent;
                grandParent = parent;
                parent = current;
                current = (order < 0) ? current.Left : current.Right;
            }

            Debug.Assert(parent != null, "Parent node cannot be null here!");
            // ready to insert the new node
            NodeInternal node = ObtainNode(item);
            if (order > 0)
            {
                parent.Right = node;
            }
            else
            {
                parent.Left = node;
            }

            // the new node will be red, so we will need to adjust the colors if parent node is also red
            if (parent.IsRed)
            {
                InsertionBalance(node, ref parent, grandParent, greatGrandParent);
            }

            // Root node is always black
            _root.IsRed = false;
            ++_count;
            return true;
        }

        /// <summary>
        /// Remove the T ITEM from this SortedSet. Returns true if successfully removed.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            return DoRemove(item); // hack so it can be made non-virtual
        }

        internal bool DoRemove(T item)
        {
            if (_root == null)
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
            _version++;

            NodeInternal current = _root;
            NodeInternal parent = null;
            NodeInternal grandParent = null;
            NodeInternal match = null;
            NodeInternal parentOfMatch = null;
            bool foundMatch = false;
            while (current != null)
            {
                if (Is2Node(current))
                { // fix up 2-Node
                    if (parent == null)
                    {   // current is root. Mark it as red
                        current.IsRed = true;
                    }
                    else
                    {
                        NodeInternal sibling = GetSibling(current, parent);
                        if (sibling.IsRed)
                        {
                            // If parent is a 3-node, flip the orientation of the red link. 
                            // We can achieve this by a single rotation        
                            // This case is converted to one of other cased below.
                            Debug.Assert(!parent.IsRed, "parent must be a black node!");
                            if (parent.Right == sibling)
                            {
                                RotateLeft(parent);
                            }
                            else
                            {
                                RotateRight(parent);
                            }

                            parent.IsRed = true;
                            sibling.IsRed = false;    // parent's color
                            // sibling becomes child of grandParent or root after rotation. Update link from grandParent or root
                            ReplaceChildOfNodeOrRoot(grandParent, parent, sibling);
                            // sibling will become grandParent of current node 
                            grandParent = sibling;
                            if (parent == match)
                            {
                                parentOfMatch = sibling;
                            }

                            // update sibling, this is necessary for following processing
                            sibling = (parent.Left == current) ? parent.Right : parent.Left;
                        }
                        Debug.Assert(sibling != null && sibling.IsRed == false, "sibling must not be null and it must be black!");

                        if (Is2Node(sibling))
                        {
                            Merge2Nodes(parent, current, sibling);
                        }
                        else
                        {
                            // current is a 2-node and sibling is either a 3-node or a 4-node.
                            // We can change the color of current to red by some rotation.
                            TreeRotation rotation = RotationNeeded(parent, current, sibling);
                            NodeInternal newGrandParent = null;
                            switch (rotation)
                            {
                                case TreeRotation.RightRotation:
                                    Debug.Assert(parent.Left == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(sibling.Left.IsRed, "Left child of sibling must be red!");
                                    sibling.Left.IsRed = false;
                                    newGrandParent = RotateRight(parent);
                                    break;
                                case TreeRotation.LeftRotation:
                                    Debug.Assert(parent.Right == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(sibling.Right.IsRed, "Right child of sibling must be red!");
                                    sibling.Right.IsRed = false;
                                    newGrandParent = RotateLeft(parent);
                                    break;

                                case TreeRotation.RightLeftRotation:
                                    Debug.Assert(parent.Right == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(sibling.Left.IsRed, "Left child of sibling must be red!");
                                    newGrandParent = RotateRightLeft(parent);
                                    break;

                                case TreeRotation.LeftRightRotation:
                                    Debug.Assert(parent.Left == sibling, "sibling must be left child of parent!");
                                    Debug.Assert(sibling.Right.IsRed, "Right child of sibling must be red!");
                                    newGrandParent = RotateLeftRight(parent);
                                    break;
                            }

                            newGrandParent.IsRed = parent.IsRed;
                            parent.IsRed = false;
                            current.IsRed = true;
                            ReplaceChildOfNodeOrRoot(grandParent, parent, newGrandParent);
                            if (parent == match)
                            {
                                parentOfMatch = newGrandParent;
                            }
                            grandParent = newGrandParent;
                        }
                    }
                }

                // we don't need to compare any more once we found the match
                int order = foundMatch ? -1 : _comparer.Compare(item, current.Item);
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
                    current = current.Left;
                }
                else
                {
                    current = current.Right;       // continue the search in  right sub tree after we find a match
                }
            }

            // move successor to the matching node position and replace links
            if (match != null)
            {
                ReplaceNode(match, parentOfMatch, parent, grandParent);
                --_count;
                ReturnNode(match);
            }

            if (_root != null)
            {
                _root.IsRed = false;
            }
            return foundMatch;
        }

        public void Clear()
        {
            int oldPoolCount = m_poolCount;

            // Return all nodes without clearing (so we can iterate)
            var node = GetFirst();
            while (node != null)
            {
                ReturnNodeNoClear(node);
                node = GetNext(node);
            }

            // Clear returned nodes
            for (int i = oldPoolCount; i < m_poolCount; i++)
            {
                ClearNode(m_pool[i]);
            }

            _root = null;
            _count = 0;
            ++_version;
        }


        public bool Contains(T item)
        {
            return FindNode(item) != null;
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

        private static NodeInternal GetSibling(NodeInternal node, NodeInternal parent)
        {
            if (parent.Left == node)
            {
                return parent.Right;
            }
            return parent.Left;
        }

        // After calling InsertionBalance, we need to make sure current and parent up-to-date.
        // It doesn't matter if we keep grandParent and greatGrantParent up-to-date 
        // because we won't need to split again in the next node.
        // By the time we need to split again, everything will be correctly set.
        private void InsertionBalance(NodeInternal current, ref NodeInternal parent, NodeInternal grandParent, NodeInternal greatGrandParent)
        {
            Debug.Assert(grandParent != null, "Grand parent cannot be null here!");
            bool parentIsOnRight = (grandParent.Right == parent);
            bool currentIsOnRight = (parent.Right == current);

            NodeInternal newChildOfGreatGrandParent;
            if (parentIsOnRight == currentIsOnRight)
            { // same orientation, single rotation
                newChildOfGreatGrandParent = currentIsOnRight ? RotateLeft(grandParent) : RotateRight(grandParent);
            }
            else
            {  // different orientation, double rotation
                newChildOfGreatGrandParent = currentIsOnRight ? RotateLeftRight(grandParent) : RotateRightLeft(grandParent);
                // current node now becomes the child of greatgrandparent 
                parent = greatGrandParent;
            }
            // grand parent will become a child of either parent of current.
            grandParent.IsRed = true;
            newChildOfGreatGrandParent.IsRed = false;

            ReplaceChildOfNodeOrRoot(greatGrandParent, grandParent, newChildOfGreatGrandParent);
        }

        private static bool Is2Node(NodeInternal node)
        {
            Debug.Assert(node != null, "node cannot be null!");
            return IsBlack(node) && IsNullOrBlack(node.Left) && IsNullOrBlack(node.Right);
        }

        private static bool Is4Node(NodeInternal node)
        {
            return IsRed(node.Left) && IsRed(node.Right);
        }

        private static bool IsBlack(NodeInternal node)
        {
            return (node != null && !node.IsRed);
        }

        private static bool IsNullOrBlack(NodeInternal node)
        {
            return (node == null || !node.IsRed);
        }

        private static bool IsRed(NodeInternal node)
        {
            return (node != null && node.IsRed);
        }

        private static void Merge2Nodes(NodeInternal parent, NodeInternal child1, NodeInternal child2)
        {
            Debug.Assert(IsRed(parent), "parent must be red");
            // combing two 2-nodes into a 4-node
            parent.IsRed = false;
            child1.IsRed = true;
            child2.IsRed = true;
        }

        NodeInternal ObtainNode(T item, bool isRed = true)
        {
            if (m_poolCount > 0)
            {
                m_poolCount--;
                NodeInternal node = m_pool[m_poolCount];
                node.Item = item;
                node.IsRed = isRed;
                return node;
            }
            else
            {
                return new NodeInternal(item, isRed);
            }
        }

        void ReturnNode(NodeInternal node)
        {
            Debug.Assert(node.m_left == null || node.m_left.Parent != node, "Returning attached node");
            Debug.Assert(node.m_right == null || node.m_right.Parent != node, "Returning attached node");
            Debug.Assert(node.Parent == null || (node.Parent.m_left != node && node.Parent.m_right != node), "Returning attached node");
            ClearNode(node);
            ReturnNodeNoClear(node);
        }

        static void ClearNode(NodeInternal node)
        {
            node.Item = default(T);
            node.Parent = null;
            node.m_left = null;
            node.m_right = null;
        }

        void ReturnNodeNoClear(NodeInternal node)
        {
            if (m_poolCount >= m_pool.Length)
            {
                Array.Resize(ref m_pool, Math.Max(4, 2 * m_pool.Length));
            }
            m_pool[m_poolCount] = node;
            m_poolCount++;
        }

        // Replace the child of a parent node. 
        // If the parent node is null, replace the root.        
        private void ReplaceChildOfNodeOrRoot(NodeInternal parent, NodeInternal child, NodeInternal newChild)
        {
            if (parent != null)
            {
                if (parent.Left == child)
                {
                    parent.Left = newChild;
                }
                else
                {
                    parent.Right = newChild;
                }
            }
            else
            {
                _root = newChild;
                if (_root != null)
                {
                    _root.Parent = null;
                }
            }
        }

        // Replace the matching node with its successor.
        private void ReplaceNode(NodeInternal match, NodeInternal parentOfMatch, NodeInternal successor, NodeInternal parentOfsuccessor)
        {
            if (successor == match)
            {  // this node has no successor, should only happen if right child of matching node is null.
                Debug.Assert(match.Right == null, "Right child must be null!");
                successor = match.Left;
            }
            else
            {
                Debug.Assert(parentOfsuccessor != null, "parent of successor cannot be null!");
                Debug.Assert(successor.Left == null, "Left child of successor must be null!");
                Debug.Assert((successor.Right == null && successor.IsRed) || (successor.Right.IsRed && !successor.IsRed), "Successor must be in valid state");
                if (successor.Right != null)
                {
                    successor.Right.IsRed = false;
                }

                if (parentOfsuccessor != match)
                {   // detach successor from its parent and set its right child
                    parentOfsuccessor.Left = successor.Right;
                    successor.Right = match.Right;
                }

                successor.Left = match.Left;
            }

            if (successor != null)
            {
                successor.IsRed = match.IsRed;
            }

            ReplaceChildOfNodeOrRoot(parentOfMatch, match, successor);
        }

        internal NodeInternal FindNode(T item)
        {
            NodeInternal current = _root;
            while (current != null)
            {
                int order = _comparer.Compare(item, current.Item);
                if (order == 0)
                {
                    return current;
                }
                else
                {
                    current = (order < 0) ? current.Left : current.Right;
                }
            }

            return null;
        }

        //used for bithelpers. Note that this implementation is completely different 
        //from the Subset's. The two should not be mixed. This indexes as if the tree were an array.
        //http://en.wikipedia.org/wiki/Binary_Tree#Methods_for_storing_binary_trees
        internal int InternalIndexOf(T item)
        {
            NodeInternal current = _root;
            int count = 0;
            while (current != null)
            {
                int order = _comparer.Compare(item, current.Item);
                if (order == 0)
                {
                    return count;
                }
                else
                {
                    current = (order < 0) ? current.Left : current.Right;
                    count = (order < 0) ? (2 * count + 1) : (2 * count + 2);
                }
            }
            return -1;
        }

        public Node FindNext(T item)
        {
            NodeInternal current = _root;
            while (current != null)
            {
                int order = _comparer.Compare(item, current.Item);
                if (order == 0)
                {
                    return new Node(this, current).Next;
                }
                if(order > 0)
                {
                    if (current.m_right == null)
                        return new Node(this, current).Next;
                    else
                        current = current.m_right;
                }
                else
                {
                    if (current.m_left == null)
                        return new Node(this, current);
                    else
                        current = current.m_left;
                }
            }
            return new Node(this, null);
        }

        public Node FindPrevious(T item)
        {
            NodeInternal current = _root;
            while (current != null)
            {
                int order = _comparer.Compare(item, current.Item);
                if (order == 0)
                {
                    return new Node(this, current).Previous;
                }
                if (order > 0)
                {
                    if (current.m_right == null)
                        return new Node(this, current);
                    else
                        current = current.m_right;
                }
                else
                {
                    if (current.m_left == null)
                        return new Node(this, current).Previous;
                    else
                        current = current.m_left;
                }
            }
            return new Node(this, null);
        }

        internal NodeInternal FindRange(T from, T to)
        {
            return FindRange(from, to, true, true);
        }

        internal NodeInternal FindRange(T from, T to, bool lowerBoundActive, bool upperBoundActive)
        {
            NodeInternal current = _root;
            while (current != null)
            {
                if (lowerBoundActive && _comparer.Compare(from, current.Item) > 0)
                {
                    current = current.Right;
                }
                else
                {
                    if (upperBoundActive && _comparer.Compare(to, current.Item) < 0)
                    {
                        current = current.Left;
                    }
                    else
                    {
                        return current;
                    }
                }
            }

            return null;
        }

        internal void UpdateVersion()
        {
            ++_version;
        }

        private static NodeInternal RotateLeft(NodeInternal node)
        {
            NodeInternal x = node.Right;
            node.Right = x.Left;
            x.Left = node;
            return x;
        }

        private static NodeInternal RotateLeftRight(NodeInternal node)
        {
            NodeInternal child = node.Left;
            NodeInternal grandChild = child.Right;

            node.Left = grandChild.Right;
            grandChild.Right = node;
            child.Right = grandChild.Left;
            grandChild.Left = child;
            return grandChild;
        }

        private static NodeInternal RotateRight(NodeInternal node)
        {
            NodeInternal x = node.Left;
            node.Left = x.Right;
            x.Right = node;
            return x;
        }

        private static NodeInternal RotateRightLeft(NodeInternal node)
        {
            NodeInternal child = node.Right;
            NodeInternal grandChild = child.Left;

            node.Right = grandChild.Left;
            grandChild.Left = node;
            child.Left = grandChild.Right;
            grandChild.Right = child;
            return grandChild;
        }

        /// <summary>
        /// Testing counter that can track rotations
        /// </summary>
        private static TreeRotation RotationNeeded(NodeInternal parent, NodeInternal current, NodeInternal sibling)
        {
            Debug.Assert(IsRed(sibling.Left) || IsRed(sibling.Right), "sibling must have at least one red child");
            if (IsRed(sibling.Left))
            {
                if (parent.Left == current)
                {
                    return TreeRotation.RightLeftRotation;
                }
                return TreeRotation.RightRotation;
            }
            else
            {
                if (parent.Left == current)
                {
                    return TreeRotation.LeftRotation;
                }
                return TreeRotation.LeftRightRotation;
            }
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

        private static void Split4Node(NodeInternal node)
        {
            node.IsRed = true;
            node.Left.IsRed = false;
            node.Right.IsRed = false;
        }

        public void TrimExcess()
        {
            Array.Resize(ref m_pool, m_poolCount);
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
                if (_root == null)
                {
                    return default(T);
                }

                NodeInternal current = _root;
                while (current.Left != null)
                {
                    current = current.Left;
                }

                return current.Item;
            }
        }

        public T Max
        {
            get
            {
                if (_root == null)
                {
                    return default(T);
                }

                NodeInternal current = _root;
                while (current.Right != null)
                {
                    current = current.Right;
                }

                return current.Item;
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

            info.AddValue(CountName, _count); //This is the length of the bucket array.
            info.AddValue(ComparerName, _comparer, typeof(IComparer<T>));
            info.AddValue(VersionName, _version);

            if (_root != null)
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
            if (_comparer != null)
            {
                return; // Somebody had a dependency on this class and fixed us up before the ObjectManager got to it.
            }

            if (_siInfo == null)
            {
                throw new SerializationException("Serialization_InvalidOnDeser");
            }

            _comparer = (IComparer<T>)_siInfo.GetValue(ComparerName, typeof(IComparer<T>));
            int savedCount = _siInfo.GetInt32(CountName);

            if (savedCount != 0)
            {
                T[] items = (T[])_siInfo.GetValue(ItemsName, typeof(T[]));

                if (items == null)
                {
                    throw new SerializationException("Serialization_MissingValues");
                }

                for (int i = 0; i < items.Length; i++)
                {
                    Add(items[i]);
                }
            }

            _version = _siInfo.GetInt32(VersionName);
            if (_count != savedCount)
            {
                throw new SerializationException("Serialization_MismatchedCount");
            }

            _siInfo = null;
        }
        #endregion

        #region Enumeration helpers
        NodeInternal GetFirst()
        {
            if (_root == null)
                return null;

            // Slide left
            var result = _root;
            while (result.Left != null)
            {
                result = result.Left;
            }
            return result;
        }

        NodeInternal GetLast()
        {
            if (_root == null)
                return null;

            // Slide Right
            var result = _root;
            while (result.Right != null)
            {
                result = result.Right;
            }
            return result;
        }

        NodeInternal GetNext(NodeInternal node)
        {
            // Has right node
            if (node.Right != null)
            {
                // Move to right
                node = node.Right;

                // Slide left
                while (node.Left != null)
                {
                    node = node.Left;
                }
                return node;
            }

            // While not root
            while (node.Parent != null)
            {
                // Is left child
                if (node.Parent.Left == node)
                {
                    // Continue with parent then
                    node = node.Parent;
                    return node;
                }
                else
                {
                    // Is right child, go up then
                    node = node.Parent;
                }
            }
            return null;
        }

        NodeInternal GetPrevious(NodeInternal node)
        {
            // Has Left node
            if (node.Left != null)
            {
                // Move to Left
                node = node.Left;

                // Slide Right
                while (node.Right != null)
                {
                    node = node.Right;
                }
                return node;
            }

            // While not root
            while (node.Parent != null)
            {
                // Is Right child
                if (node.Parent.Right == node)
                {
                    // Continue with parent then
                    node = node.Parent;
                    return node;
                }
                else
                {
                    // Is Left child, go up then
                    node = node.Parent;
                }
            }
            return null;
        }
        #endregion

        #region Helper Classes
        internal enum TreeRotation
        {
            LeftRotation = 1,
            RightRotation = 2,
            RightLeftRotation = 3,
            LeftRightRotation = 4,
        }

        [Serializable]
        internal sealed class NodeInternal
        {
            public NodeInternal m_left;
            public NodeInternal m_right;

            public bool IsRed;
            public T Item;

            public NodeInternal Parent;

            public NodeInternal Left
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return m_left; }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set
                {
                    m_left = value;
                    if (m_left != null)
                        m_left.Parent = this;
                }
            }

            public NodeInternal Right
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return m_right; }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set
                {
                    m_right = value;
                    if (m_right != null)
                        m_right.Parent = this;
                }
            }

            public NodeInternal(T item)
            {
                // The default color will be red, we never need to create a black node directly.                
                Item = item;
                IsRed = true;
            }

            public NodeInternal(T item, bool isRed)
            {
                // The default color will be red, we never need to create a black node directly.                
                Item = item;
                IsRed = isRed;
            }
        }

        public struct Node
        {
            SortedSet<T> m_tree;
            NodeInternal m_node;
            int m_version;

            public bool IsNull
            {
                get { return m_node == null; }
            }

            /// <summary>
            /// Gets node value.
            /// </summary>
            public T Value
            {
                get
                {
                    CheckVersion();
                    return m_node.Item;
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

            internal Node(SortedSet<T> tree, NodeInternal node)
            {
                m_tree = tree;
                m_node = node;
                m_version = tree._version;
            }

            void CheckVersion()
            {
                if (m_version != m_tree._version)
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
            private SortedSet<T> _tree;
            private int _version;
            private SortedSet<T>.NodeInternal _current;

            public T Current
            {
                get { return _current.Item; }
            }

            internal Enumerator(SortedSet<T> set)
            {
                _tree = set;
                _version = _tree._version;
                _current = null;
            }

            public bool MoveNext()
            {
                if (_version != _tree._version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                _current = _current == null ? _tree.GetFirst() : _tree.GetNext(_current);
                return _current != null;
            }

            void IDisposable.Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (_current == null)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }
                    return _current.Item;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _tree._version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }
                _current = null;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "not an expected scenario")]
        public struct ReverseEnumerator : IEnumerator<T>, IEnumerator
        {
            private SortedSet<T> _tree;
            private int _version;
            private SortedSet<T>.NodeInternal _current;

            public T Current
            {
                get { return _current.Item; }
            }

            internal ReverseEnumerator(SortedSet<T> set)
            {
                _tree = set;
                _version = _tree._version;
                _current = null;
            }

            public bool MoveNext()
            {
                if (_version != _tree._version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                _current = _current == null ? _tree.GetLast() : _tree.GetPrevious(_current);
                return _current != null;
            }

            void IDisposable.Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (_current == null)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }
                    return _current.Item;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _tree._version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }
                _current = null;
            }
        }

        internal struct ElementCount
        {
            internal int uniqueCount;
            internal int unfoundCount;
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