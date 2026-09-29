using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Masuit.Tools.Systems.Tests
{
    public class ConcurrentListTests
    {
        [Fact]
        public void Constructor_Default_ShouldCreateEmptyList()
        {
            var list = new ConcurrentList<int>();
            Assert.Empty(list);
        }

        [Fact]
        public void Constructor_WithCapacity_ShouldCreateListWithCapacity()
        {
            var list = new ConcurrentList<int>(10);
            Assert.Equal(10, list.Capacity);
        }

        [Fact]
        public void Constructor_WithCollection_ShouldCopyItems()
        {
            var items = new[] { 1, 2, 3 };
            var list = new ConcurrentList<int>(items);
            Assert.Equal(3, list.Count);
            Assert.Contains(1, list);
            Assert.Contains(2, list);
            Assert.Contains(3, list);
        }

        [Fact]
        public void Add_ShouldAddItem()
        {
            var list = new ConcurrentList<int>();
            list.Add(1);
            Assert.Contains(1, list);
            Assert.Equal(1, list.Count);
        }

        [Fact]
        public void Add_MultipleItems_ShouldAddAllItems()
        {
            var list = new ConcurrentList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);
            Assert.Equal(3, list.Count);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void AddRange_ShouldAddMultipleItems()
        {
            var list = new ConcurrentList<int> { 1 };
            list.AddRange(new[] { 2, 3, 4 });
            Assert.Equal(4, list.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, list);
        }

        [Fact]
        public void Insert_ShouldInsertItemAtIndex()
        {
            var list = new ConcurrentList<int> { 1, 3 };
            list.Insert(1, 2);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void Insert_AtBeginning_ShouldInsertAtIndex0()
        {
            var list = new ConcurrentList<int> { 2, 3 };
            list.Insert(0, 1);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void InsertRange_ShouldInsertMultipleItemsAtIndex()
        {
            var list = new ConcurrentList<int> { 1, 4 };
            list.InsertRange(1, new[] { 2, 3 });
            Assert.Equal(new[] { 1, 2, 3, 4 }, list);
        }

        [Fact]
        public void RemoveAt_ShouldRemoveItemAtIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            list.RemoveAt(1);
            Assert.Equal(new[] { 1, 3 }, list);
        }

        [Fact]
        public void Remove_ShouldRemoveFirstOccurrence()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 2 };
            var result = list.Remove(2);
            Assert.True(result);
            Assert.Equal(new[] { 1, 3, 2 }, list);
        }

        [Fact]
        public void Remove_NonExistentItem_ShouldReturnFalse()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var result = list.Remove(4);
            Assert.False(result);
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void RemoveAll_ShouldRemoveAllMatchingItems()
        {
            var list = new ConcurrentList<int> { 1, 2, 2, 3, 2 };
            var removedCount = list.RemoveAll(x => x == 2);
            Assert.Equal(3, removedCount);
            Assert.Equal(new[] { 1, 3 }, list);
        }

        [Fact]
        public void Clear_ShouldRemoveAllItems()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            list.Clear();
            Assert.Empty(list);
            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Contains_ShouldReturnTrueForContainedItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.True(list.Contains(2));
        }

        [Fact]
        public void Contains_ShouldReturnFalseForNonContainedItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.False(list.Contains(4));
        }

        [Fact]
        public void IndexOf_ShouldReturnCorrectIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.Equal(1, list.IndexOf(2));
        }

        [Fact]
        public void IndexOf_NonExistentItem_ShouldReturnNegativeOne()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.Equal(-1, list.IndexOf(4));
        }

        [Fact]
        public void Indexer_Get_ShouldReturnItemAtIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.Equal(2, list[1]);
        }

        [Fact]
        public void Indexer_Set_ShouldSetItemAtIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            list[1] = 5;
            Assert.Equal(new[] { 1, 5, 3 }, list);
        }

        [Fact]
        public void Count_ShouldReturnCorrectCount()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void Count_EmptyList_ShouldReturnZero()
        {
            var list = new ConcurrentList<int>();
            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void IsReadOnly_ShouldReturnFalse()
        {
            var list = new ConcurrentList<int>();
            Assert.False(list.IsReadOnly);
        }

        [Fact]
        public void CopyTo_ShouldCopyItemsToArray()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var array = new int[3];
            list.CopyTo(array, 0);
            Assert.Equal(new[] { 1, 2, 3 }, array);
        }

        [Fact]
        public void CopyTo_WithOffset_ShouldCopyWithOffset()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var array = new int[5];
            list.CopyTo(array, 1);
            Assert.Equal(new[] { 0, 1, 2, 3, 0 }, array);
        }

        [Fact]
        public void GetRange_ShouldReturnSubset()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4, 5 };
            var range = list.GetRange(1, 3);
            Assert.Equal(new[] { 2, 3, 4 }, range);
        }

        [Fact]
        public void Sort_ShouldSortItems()
        {
            var list = new ConcurrentList<int> { 3, 1, 4, 1, 5 };
            list.Sort();
            Assert.Equal(new[] { 1, 1, 3, 4, 5 }, list);
        }

        [Fact]
        public void Sort_WithComparer_ShouldSortWithCustomComparer()
        {
            var list = new ConcurrentList<int> { 3, 1, 4, 1, 5 };
            list.Sort(Comparer<int>.Create((a, b) => b.CompareTo(a))); // Descending
            Assert.Equal(new[] { 5, 4, 3, 1, 1 }, list);
        }

        [Fact]
        public void Sort_WithIndexAndCount_ShouldSortSubset()
        {
            var list = new ConcurrentList<int> { 1, 3, 2, 5, 4 };
            list.Sort(1, 3, Comparer<int>.Default);
            Assert.Equal(new[] { 1, 2, 3, 5, 4 }, list);
        }

        [Fact]
        public void Reverse_ShouldReverseItems()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            list.Reverse();
            Assert.Equal(new[] { 3, 2, 1 }, list);
        }

        [Fact]
        public void Reverse_WithIndexAndCount_ShouldReverseSubset()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4, 5 };
            list.Reverse(1, 3);
            Assert.Equal(new[] { 1, 4, 3, 2, 5 }, list);
        }

        [Fact]
        public void Find_ShouldReturnFirstMatchingItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4 };
            var result = list.Find(x => x > 2);
            Assert.Equal(3, result);
        }

        [Fact]
        public void Find_NoMatch_ShouldReturnDefault()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var result = list.Find(x => x > 10);
            Assert.Equal(0, result);
        }

        [Fact]
        public void FindAll_ShouldReturnAllMatchingItems()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4, 5 };
            var results = list.FindAll(x => x > 2);
            Assert.Equal(new[] { 3, 4, 5 }, results);
        }

        [Fact]
        public void FindAll_NoMatches_ShouldReturnEmptyList()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var results = list.FindAll(x => x > 10);
            Assert.Empty(results);
        }

        [Fact]
        public void FindIndex_ShouldReturnFirstMatchingIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4 };
            var index = list.FindIndex(x => x > 2);
            Assert.Equal(2, index);
        }

        [Fact]
        public void FindIndex_WithStartIndex_ShouldSearchFromStartIndex()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4 };
            var index = list.FindIndex(2, x => x > 2);
            Assert.Equal(2, index);
        }


        [Fact]
        public void FindIndex_NoMatch_ShouldReturnNegativeOne()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var index = list.FindIndex(x => x > 10);
            Assert.Equal(-1, index);
        }

        [Fact]
        public void Exists_ShouldReturnTrueForMatchingItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3, 4 };
            Assert.True(list.Exists(x => x > 2));
        }

        [Fact]
        public void Exists_ShouldReturnFalseForNonMatchingItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            Assert.False(list.Exists(x => x > 10));
        }

        [Fact]
        public void TrueForAll_ShouldReturnTrueForAllMatching()
        {
            var list = new ConcurrentList<int> { 2, 4, 6 };
            Assert.True(list.TrueForAll(x => x % 2 == 0));
        }

        [Fact]
        public void TrueForAll_ShouldReturnFalseIfAnyNotMatching()
        {
            var list = new ConcurrentList<int> { 2, 3, 4 };
            Assert.False(list.TrueForAll(x => x % 2 == 0));
        }

        [Fact]
        public void ForEach_ShouldExecuteActionForEachItem()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var results = new List<int>();
            list.ForEach(x => results.Add(x * 2));
            Assert.Equal(new[] { 2, 4, 6 }, results);
        }

        [Fact]
        public void Capacity_Get_ShouldReturnCapacity()
        {
            var list = new ConcurrentList<int>(20);
            Assert.Equal(20, list.Capacity);
        }

        [Fact]
        public void Capacity_Set_ShouldSetCapacity()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            list.Capacity = 10;
            Assert.Equal(10, list.Capacity);
        }

        [Fact]
        public void TrimExcess_ShouldReduceCapacity()
        {
            var list = new ConcurrentList<int>(100) { 1, 2, 3 };
            list.TrimExcess();
            Assert.True(list.Capacity <= 10); // Implementation dependent
        }

        [Fact]
        public void GetEnumerator_ShouldEnumerateAllItems()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var items = new List<int>();
            foreach (var item in list)
            {
                items.Add(item);
            }
            Assert.Equal(new[] { 1, 2, 3 }, items);
        }

        [Fact]
        public void GetEnumerator_ShouldBeEnumerableMultipleTimes()
        {
            var list = new ConcurrentList<int> { 1, 2, 3 };
            var items1 = list.ToList();
            var items2 = list.ToList();
            Assert.Equal(items1, items2);
        }

        [Fact]
        public void Dispose_ShouldDisposeLock()
        {
            var list = new ConcurrentList<int>();
            list.Dispose();
            Assert.Throws<ObjectDisposedException>(() => list.Add(1));
        }

        [Fact]
        public void ConcurrentAdd_ShouldBeThreadSafe()
        {
            var list = new ConcurrentList<int>();
            var tasks = Enumerable.Range(0, 100).Select(i =>
                Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        list.Add(i * 100 + j);
                    }
                })
            ).ToArray();

            Task.WaitAll(tasks);
            Assert.Equal(10000, list.Count);
        }

        [Fact]
        public void ConcurrentReadWrite_ShouldBeThreadSafe()
        {
            var list = new ConcurrentList<int>(Enumerable.Range(1, 100));
            var sum = 0;
            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    list.Add(i);
                    Thread.Sleep(1);
                }
            });

            var readTask = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    sum += list.Count;
                    Thread.Sleep(1);
                }
            });

            Task.WaitAll(writeTask, readTask);
            Assert.True(sum > 0);
        }

        [Fact]
        public void ConcurrentRemove_ShouldBeThreadSafe()
        {
            var list = new ConcurrentList<int>(Enumerable.Range(1, 1000));
            var tasks = Enumerable.Range(0, 10).Select(i =>
                Task.Run(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        if (list.Count > 0)
                        {
                            list.RemoveAt(0);
                        }
                    }
                })
            ).ToArray();

            Task.WaitAll(tasks);
            Assert.Equal(0, list.Count);
        }
    }
}
