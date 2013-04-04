// Stephen Toub
// stoub@microsoft.com
// 
// PriorityQueue.cs
// A C# implementation of a max priority queue.
//
// HISTORY:
// v1.0.0 - Original version
// 
// October 4th, 2002
// v1.0.0

#region Namespaces
using System;
using System.Collections;
using System.Collections.Generic;
#endregion

namespace Toub.Collections
{
	/// <summary>A priority queue.</summary>
    public class PriorityQueue<T> : ICollection, IEnumerable<T>
	{
		#region Member Variables
		/// <summary>The binary heap on which the priority queue is based.</summary>
		private BinaryHeap _heap;
		#endregion

		#region Construction
		/// <summary>Initialize the queue.</summary>
		public PriorityQueue() { _heap = new BinaryHeap(); }

		/// <summary>Initialize the queue.</summary>
		/// <param name="queue">The queue is intialized with a shalled-copy of this queue.</param>
		public PriorityQueue(PriorityQueue<T> queue)
		{
			_heap = queue._heap.Clone();
		}
		#endregion

		#region Methods
		/// <summary>Enqueues an item to the priority queue.</summary>
		/// <param name="priority">The priority of the object to be enqueued.</param>
		/// <param name="value">The object to be enqueued.</param>
		public virtual void Enqueue(int priority, T value)
		{
			_heap.Insert(priority, value);
		}

		/// <summary>Dequeues an object from the priority queue.</summary>
		/// <returns>The top item (max priority) from the queue.</returns>
		public virtual T Dequeue()
		{
			return (T)_heap.Remove();
		}

		/// <summary>Empties the queue.</summary>
		public virtual void Clear()
		{
			_heap.Clear();
		}
		#endregion

		#region Implementation of ICollection
		/// <summary>Copies the priority queue to an array.</summary>
		/// <param name="array">The array to which the queue should be copied.</param>
		/// <param name="index">The starting index.</param>
		public virtual void CopyTo(System.Array array, int index) { _heap.CopyTo(array, index); }

		/// <summary>Determines whether the priority queue is synchronized.</summary>
		public virtual bool IsSynchronized { get { return _heap.IsSynchronized; } }

		/// <summary>Gets the number of items in the queue.</summary>
		public virtual int Count { get { return _heap.Count; } }

		/// <summary>Gets the synchronization root object for the queue.</summary>
		public object SyncRoot { get { return _heap.SyncRoot; } }
		#endregion

		#region Implementation of IEnumerable

        public class PriorityQueueHeapEnumerator<TT> : IEnumerator<TT>
        {
            #region Member Variables
            /// <summary>The enumerator of the array list containing BinaryHeapEntry objects.</summary>
            private IEnumerator _enumerator;
            #endregion

            #region Construction
            /// <summary>Initialize the enumerator</summary>
            /// <param name="enumerator">The array list enumerator.</param>
            internal PriorityQueueHeapEnumerator(IEnumerator enumerator)
            {
                _enumerator = enumerator;
            }
            #endregion

            #region Implementation of IEnumerator
            /// <summary>Resets the enumerator.</summary>
            public void Reset() { _enumerator.Reset(); }

            /// <summary>Moves to the next item in the list.</summary>
            /// <returns>Whether there are more items in the list.</returns>
            public bool MoveNext() { return _enumerator.MoveNext(); }

            public void Dispose() { _enumerator = null; }

            /// <summary>Gets the current object in the list.</summary>
            object IEnumerator.Current
            {
                get
                {
                    // Returns the value from the entry if it exists; otherwise, null.
                    return _enumerator.Current;
                }
            }
            /// <summary>Gets the current object in the list.</summary>
            TT IEnumerator<TT>.Current
            {
                get
                {
                    // Returns the value from the entry if it exists; otherwise, null.
                    return (TT)_enumerator.Current;
                }
            }
            #endregion
        }

		/// <summary>Gets the enumerator for the queue.</summary>
		/// <returns>An enumerator for the queue.</returns>
        public IEnumerator<T> GetEnumerator() { return new PriorityQueueHeapEnumerator<T>(_heap.GetEnumerator()); }
        IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
		#endregion
	}
}
