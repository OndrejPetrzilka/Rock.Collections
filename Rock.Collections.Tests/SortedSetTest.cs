using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
        Both TenWithoutFive { get { return new Both().Add(1).Add(2).Add(3).Add(4).Add(6).Add(7).Add(8).Add(9).Add(10); } }

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

        [TestMethod]
        public void FindNextTest()
        {
            Assert.IsTrue(TenWithoutFive.Set.FindNext(0).Value == 1);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(1).Value == 2);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(2).Value == 3);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(3).Value == 4);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(4).Value == 6);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(5).Value == 6);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(6).Value == 7);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(8).Value == 9);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(9).Value == 10);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(10).IsNull);
            Assert.IsTrue(TenWithoutFive.Set.FindNext(11).IsNull);
        }

        [TestMethod]
        public void FindPrevTest()
        {
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(0).IsNull);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(1).IsNull);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(2).Value == 1);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(3).Value == 2);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(4).Value == 3);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(5).Value == 4);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(6).Value == 4);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(7).Value == 6);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(8).Value == 7);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(9).Value == 8);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(10).Value == 9);
            Assert.IsTrue(TenWithoutFive.Set.FindPrevious(11).Value == 10);
        }
    }
}
