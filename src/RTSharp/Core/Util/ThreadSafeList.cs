using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace RTSharp.Core.Util
{
    public class ThreadSafeList<T> : IList<T>
    {
        private List<T> _interalList = new List<T>();

        public IEnumerator<T> GetEnumerator()
        {
            return Clone().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return Clone().GetEnumerator();
        }

        private Lock _lock = new();

        public int Count {
            get {
                lock (_lock) {
                    return _interalList.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public T this[int index] {
            get {
                lock (_lock) {
                    return _interalList[index];
                }
            }
            set {
                lock (_lock) {
                    _interalList[index] = value;
                }
            }
        }

        public List<T> Clone()
        {
            List<T> newList = new List<T>();

            lock (_lock) {
                _interalList.ForEach(x => newList.Add(x));
            }

            return newList;
        }

        public int IndexOf(T item)
        {
            lock (_lock) {
                return _interalList.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            lock (_lock) {
                _interalList.Insert(index, item);
            }
        }

        public void RemoveAt(int index)
        {
            lock (_lock) {
                _interalList.RemoveAt(index);
            }
        }

        public void Add(T item)
        {
            lock (_lock) {
                _interalList.Add(item);
            }
        }

        public void Clear()
        {
            lock (_lock) {
                _interalList.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (_lock) {
                return _interalList.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock) {
                _interalList.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            lock (_lock) {
                return _interalList.Remove(item);
            }
        }
    }
}
