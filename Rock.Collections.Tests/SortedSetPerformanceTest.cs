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

        static NetSortedSet m_warmNet;
        static RockSortedSet m_warmRock;

        static NetSortedSet m_warmNetFull;
        static RockSortedSet m_warmRockFull;

        static SortedSetPerformanceTest()
        {
            m_warmNet = new NetSortedSet();
            m_warmRock = new RockSortedSet();
            m_warmNetFull = new NetSortedSet(m_warm);
            m_warmRockFull = new RockSortedSet(m_warm);

            m_warmNet.Clear();
            m_warmRock.Clear();
            new SortedSetPerformanceTest().TestAddDotNet();
            new SortedSetPerformanceTest().TestAddRock();
            new SortedSetPerformanceTest().TestRemoveDotNet();
            new SortedSetPerformanceTest().TestRemoveRock();

            m_warmNet.Clear();
            m_warmRock.Clear();
            m_warmNetFull.Clear();
            m_warmRockFull.Clear();
            for (int i = 0; i < IterCount; i++)
            {
                m_warmNetFull.Add(i);
            }
            for (int i = 0; i < IterCount; i++)
            {
                m_warmRockFull.Add(i);
            }
        }

        [TestMethod]
        public void TestAddDotNet()
        {
            for (int i = 0; i < IterCount; i++)
            {
                m_warmNet.Add(i);
            }
        }

        [TestMethod]
        public void TestAddRock()
        {
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
