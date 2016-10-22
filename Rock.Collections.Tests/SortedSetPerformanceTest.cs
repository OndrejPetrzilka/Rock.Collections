using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rock.Collections;

namespace Rock.Tests.Base
{
    using NetSortedSet = System.Collections.Generic.SortedSet<int>;
    using RockSortedSet = Rock.Collections.SortedSet<int>;

    [TestClass]
    public class SortedSetPerformanceTest
    {
        static readonly int IterCount = 200000;
        static IEnumerable<int> m_warm = Enumerable.Range(0, IterCount);

        static NetSortedSet m_warmNet = new NetSortedSet(m_warm);
        static RockSortedSet m_warmRock = new RockSortedSet(m_warm);

        static NetSortedSet m_warmNetFull = new NetSortedSet(m_warm);
        static RockSortedSet m_warmRockFull = new RockSortedSet(m_warm);

        static SortedSetPerformanceTest()
        {
            m_warmNet.Clear();
            m_warmRock.Clear();

            new SortedSetPerformanceTest().TestAddDotNet();
            new SortedSetPerformanceTest().TestAddRock();
        }

        [TestMethod]
        public void TestAddDotNet()
        {
            m_warmNet.Clear();
            for (int i = 0; i < IterCount; i++)
            {
                m_warmNet.Add(i);
            }
        }

        [TestMethod]
        public void TestAddRock()
        {
            m_warmRock.Clear();
            for (int i = 0; i < IterCount; i++)
            {
                m_warmRock.Add(i);
            }
        }

        [TestMethod]
        public void TestRemoveDotNet()
        {
            for (int i = 0; i < IterCount; i++)
            {
                m_warmNetFull.Remove(i);
            }
        }

        [TestMethod]
        public void TestRemoveRock()
        {
            for (int i = 0; i < IterCount; i++)
            {
                m_warmRockFull.Remove(i);
            }
        }
    }
}
