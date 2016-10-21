using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rock.Collections;

namespace Rock.Collections.Tests
{
    [TestClass]
    public class OrderedDictionaryTest
    {
        class Both
        {
            public LinkedList<int> List = new LinkedList<int>();
            public OrderedDictionary<int, object> Dictionary = new OrderedDictionary<int, object>();

            public int[] CopiedKeys
            {
                get
                {
                    int[] data = new int[Dictionary.Count];
                    Dictionary.Keys.CopyTo(data, 0);
                    return data;
                }
            }

            public int[] CopiedData
            {
                get
                {
                    KeyValuePair<int, object>[] data = new KeyValuePair<int, object>[Dictionary.Count];
                    ((ICollection<KeyValuePair<int, object>>)Dictionary).CopyTo(data, 0);
                    return data.Select(s => s.Key).ToArray();
                }
            }

            private void Validate()
            {
                Assert.IsTrue(Enumerable.SequenceEqual(List, Dictionary.Keys));
                Assert.IsTrue(Enumerable.SequenceEqual(List, Dictionary.Select(s => s.Key)));
                Assert.IsTrue(Enumerable.SequenceEqual(List.Reverse(), Dictionary.Reversed.Select(s => s.Key)));
                Assert.IsTrue(Enumerable.SequenceEqual(List, CopiedKeys));
                Assert.IsTrue(Enumerable.SequenceEqual(List, CopiedData));
            }

            public Both Add(int item) { if (!List.Contains(item)) { List.AddLast(item); } Dictionary.Add(item, null); Validate(); return this; }
            public Both Remove(int item) { List.Remove(item); Dictionary.Remove(item); Validate(); return this; }
            public Both Contains(int item) { Assert.IsTrue(List.Contains(item) == Dictionary.ContainsKey(item)); return this; }
            public Both MoveFirst(int item) { if (List.Remove(item)) List.AddFirst(item); Dictionary.MoveFirst(item); Validate(); return this; }
            public Both MoveLast(int item) { if (List.Remove(item)) List.AddLast(item); Dictionary.MoveLast(item); Validate(); return this; }
            public Both MoveBefore(int item, int mark) { if (item != mark && List.Contains(mark) && List.Remove(item)) List.AddBefore(List.Find(mark), item); Dictionary.MoveBefore(item, mark); Validate(); return this; }
            public Both MoveAfter(int item, int mark) { if (item != mark && List.Contains(mark) && List.Remove(item)) List.AddAfter(List.Find(mark), item); Dictionary.MoveAfter(item, mark); Validate(); return this; }
        }

        Both New { get { return new Both(); } }
        Both One { get { return new Both().Add(1); } }
        Both Two { get { return new Both().Add(1).Add(2); } }
        Both Four { get { return new Both().Add(1).Add(2).Add(3).Add(4); } }

        [TestMethod]
        public void TestAdd()
        {
            New.Add(1).Add(2).Add(3);
        }

        [TestMethod]
        public void TestAddRemove()
        {
            New.Add(1).Add(2).Add(3).Remove(2).Add(4).Contains(2).Contains(3);
            New.Add(1).Add(2).Add(3).Remove(2).Add(4).Remove(4).Remove(3).Remove(1).Remove(2);
            New.Add(1).Add(2).Add(3).Remove(2).Add(4).Remove(4).Remove(3).Remove(1).Remove(2).Add(5);
            var x = New;
            for (int i = 0; i < 100; i++)
            {
                x.Add(i);
            }
            for (int i = 0; i < 100; i++)
            {
                x.Remove(i);
            }
        }

        [TestMethod]
        public void TestMove()
        {
            One.MoveFirst(1);
            One.MoveLast(1);
            One.MoveFirst(2);
            One.MoveLast(2);

            Two.MoveFirst(1);
            Two.MoveLast(1);
            Two.MoveFirst(2);
            Two.MoveLast(2);

            Two.MoveAfter(1, 2);
            Two.MoveAfter(2, 1);
            Two.MoveBefore(1, 2);
            Two.MoveBefore(2, 1);


            Four.MoveFirst(1).MoveFirst(2).MoveFirst(3).MoveFirst(4).MoveFirst(6);
            Four.MoveFirst(4).MoveFirst(3).MoveFirst(2).MoveFirst(1);
            Four.MoveLast(1).MoveLast(2).MoveLast(3).MoveLast(4).MoveLast(6);
            Four.MoveLast(4).MoveLast(3).MoveLast(2).MoveLast(1);

            Four.MoveBefore(1, 2).MoveBefore(2, 1).MoveBefore(4, 3).MoveBefore(4, 3);
            Four.MoveAfter(1, 2).MoveAfter(2, 1).MoveAfter(4, 3).MoveAfter(4, 3);
            Four.MoveAfter(1, 4);
            Four.MoveAfter(4, 1);
            Four.MoveBefore(4, 1);
            Four.MoveBefore(1, 4);

            Four.MoveAfter(1, 1);
            Four.MoveAfter(4, 4);
            Four.MoveBefore(1, 1);
            Four.MoveBefore(4, 4);

            Four.MoveAfter(1, 6);
            Four.MoveAfter(6, 1);
            Four.MoveBefore(1, 6);
            Four.MoveBefore(6, 1);
        }
    }
}
