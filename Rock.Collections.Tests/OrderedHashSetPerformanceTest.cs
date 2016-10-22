using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rock.Collections;

namespace Rock.Tests.Base
{
    [TestClass]
    public class OrderedHashSetPerformanceTest
    {
        static readonly int IterCount = 5000000;
        static IEnumerable<int> m_warm = Enumerable.Range(0, IterCount);

        static HashSet<int> m_warmHashSet = new HashSet<int>(m_warm);
        static OrderedHashSet<int> m_warmOrderedSet = new OrderedHashSet<int>(m_warm);

        static HashSet<int> m_warmHashSetFull = new HashSet<int>(m_warm);
        static OrderedHashSet<int> m_warmOrderedSetFull = new OrderedHashSet<int>(m_warm);

        static OrderedHashSetPerformanceTest()
        {
            m_warmHashSet.Clear();
            m_warmOrderedSet.Clear();

            new OrderedHashSetPerformanceTest().TestAddHashSet();
            new OrderedHashSetPerformanceTest().TestAddOrderedHashSet();
        }

        [TestMethod]
        public void TestAddHashSet()
        {
            m_warmHashSet.Clear();
            for (int i = 0; i < IterCount; i++)
            {
                m_warmHashSet.Add(i);
            }
        }

        [TestMethod]
        public void TestAddOrderedHashSet()
        {
            m_warmOrderedSet.Clear();
            for (int i = 0; i < IterCount; i++)
            {
                m_warmOrderedSet.Add(i);
            }
        }

        [TestMethod]
        public void TestRemoveHashSet()
        {
            for (int i = 0; i < IterCount; i++)
            {
                m_warmHashSetFull.Remove(i);
            }
        }

        [TestMethod]
        public void TestRemoveOrderedHashSet()
        {
            for (int i = 0; i < IterCount; i++)
            {
                m_warmOrderedSetFull.Remove(i);
            }
        }
    }
}
