using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;

namespace Masuit.Tools.Systems;

/// <summary>
/// 并发List
/// </summary>
/// <typeparam name="T"></typeparam>
public sealed class ConcurrentList<T> : IList<T>, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);

    private readonly List<T> _list = [];

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _list.Count;
            }
            finally
            {
                if (_lock.IsReadLockHeld)
                {
                    _lock.ExitReadLock();
                }
            }
        }
    }

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _list[index];
            }
            finally
            {
                if (_lock.IsReadLockHeld)
                {
                    _lock.ExitReadLock();
                }
            }
        }
        set
        {
            _lock.EnterWriteLock();
            try
            {
                _list[index] = value;
            }
            finally
            {
                if (_lock.IsWriteLockHeld)
                {
                    _lock.ExitWriteLock();
                }
            }
        }
    }

    public ConcurrentList()
    {
    }

    public ConcurrentList(int capacity)
    {
        _list = new List<T>(capacity);
    }

    public ConcurrentList(IEnumerable<T> collection)
    {
        _list = new List<T>(collection);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<T> GetEnumerator()
    {
        _lock.EnterReadLock();
        try
        {
            return new List<T>(_list).GetEnumerator();
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Add(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Add(item);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void AddRange(IEnumerable<T> collection)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.AddRange(collection);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int IndexOf(T item)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.IndexOf(item);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Insert(int index, T item)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Insert(index, item);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void InsertRange(int index, IEnumerable<T> collection)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.InsertRange(index, collection);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void RemoveAt(int index)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.RemoveAt(index);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public bool Remove(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            return _list.Remove(item);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int RemoveAll(Predicate<T> match)
    {
        _lock.EnterWriteLock();
        try
        {
            return _list.RemoveAll(match);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Clear();
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public bool Contains(T item)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Contains(item);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _lock.EnterReadLock();
        try
        {
            _list.CopyTo(array, arrayIndex);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public List<T> GetRange(int index, int count)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.GetRange(index, count);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Sort()
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Sort();
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Sort(IComparer<T> comparer)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Sort(comparer);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Sort(int index, int count, IComparer<T> comparer)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Sort(index, count, comparer);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Reverse()
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Reverse();
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Reverse(int index, int count)
    {
        _lock.EnterWriteLock();
        try
        {
            _list.Reverse(index, count);
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int FindIndex(Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.FindIndex(match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public int FindIndex(int startIndex, Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.FindIndex(startIndex, match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.FindIndex(startIndex, count, match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public T Find(Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Find(match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public List<T> FindAll(Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.FindAll(match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public bool TrueForAll(Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.TrueForAll(match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public bool Exists(Predicate<T> match)
    {
        _lock.EnterReadLock();
        try
        {
            return _list.Exists(match);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void ForEach(Action<T> action)
    {
        _lock.EnterReadLock();
        try
        {
            _list.ForEach(action);
        }
        finally
        {
            if (_lock.IsReadLockHeld)
            {
                _lock.ExitReadLock();
            }
        }
    }

    public int Capacity
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _list.Capacity;
            }
            finally
            {
                if (_lock.IsReadLockHeld)
                {
                    _lock.ExitReadLock();
                }
            }
        }
        set
        {
            _lock.EnterWriteLock();
            try
            {
                _list.Capacity = value;
            }
            finally
            {
                if (_lock.IsWriteLockHeld)
                {
                    _lock.ExitWriteLock();
                }
            }
        }
    }

    public void TrimExcess()
    {
        _lock.EnterWriteLock();
        try
        {
            _list.TrimExcess();
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing && _lock != null)
        {
            _lock.Dispose();
        }
    }

    ~ConcurrentList()
    {
        Dispose(false);
    }
}
