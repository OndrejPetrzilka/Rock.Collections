using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rock.Collections;

namespace Rock.Collections.Tests
{
    [TestClass]
    public class SortedSetTest
    {
        class Both
        {
            public LinkedList<int> List = new LinkedList<int>();
            public SortedSet<int> Set = new SortedSet<int>();

            public int[] CopiedData
            {
                get
                {
                    int[] data = new int[Set.Count];
                    Set.CopyTo(data);
                    return data;
                }
            }

            private void Validate()
            {
                Assert.IsTrue(Enumerable.SequenceEqual(List, Set));
                Assert.IsTrue(Enumerable.SequenceEqual(List.Reverse(), Set.Reversed));
                Assert.IsTrue(Enumerable.SequenceEqual(List, CopiedData));
            }

            public Both Add(int item) { if (!List.Contains(item)) { List.AddLast(item); } Set.Add(item); Validate(); return this; }
            public Both Remove(int item) { List.Remove(item); Set.Remove(item); Validate(); return this; }
            public Both Contains(int item) { Assert.IsTrue(List.Contains(item) == Set.Contains(item)); return this; }
        }

        Both New { get { return new Both(); } }
        Both One { get { return new Both().Add(1); } }
        Both Two { get { return new Both().Add(1).Add(2); } }
        Both Four { get { return new Both().Add(1).Add(2).Add(3).Add(4); } }

        [TestMethod]
        public void TestAdd()
        {
            New.Add(1).Add(2).Add(3).Add(3);
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
    }
}
