
using System.Collections.Concurrent;

namespace WindowsApiLib
{
    /// <summary>
    /// A simple implementation of a Least Recently Used (LRU) cache using a dictionary and a linked list.
    /// 
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class LruConcurrentDictionary<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _dictionary;
        private readonly LinkedList<(TKey Key, TValue Value)> _list = new();

        public LruConcurrentDictionary(int capacity)
        {
            _capacity = capacity;
            _dictionary = new ConcurrentDictionary<TKey, LinkedListNode<(TKey, TValue)>>(-1, capacity);
        }

        public int Count => _dictionary.Count;
        public int Capacity => _capacity;
        public IEnumerable<TKey> Keys => _dictionary.Keys;
        public IEnumerable<TValue> Values => _list.Select(x => x.Value);

        // Indexer - get or set a value by key
        public TValue this[TKey key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        // Add a new item (throws if key already exists)
        public void Add(TKey key, TValue value)
        {
            if (_dictionary.ContainsKey(key))
                throw new ArgumentException($"Key '{key}' already exists.");

            Set(key, value);
        }

        // Add or update a value (upsert)
        public void Set(TKey key, TValue value)
        {
            if (_dictionary.TryGetValue(key, out var existingNode))
            {
                // Update existing - move to end (most recently used)
                _list.Remove(existingNode);
                _dictionary.TryRemove(key, out _);
            }
            else if (_dictionary.Count >= _capacity)
            {
                // Evict least recently used (front of list)
                var lru = _list.First.Value;
                _list.RemoveFirst();
                _dictionary.TryRemove(lru.Key, out _);
            }

            var node = _list.AddLast((key, value));
            _dictionary[key] = node;
        }

        // Get a value by key (promotes to most recently used)
        public TValue Get(TKey key)
        {
            if (!_dictionary.TryGetValue(key, out var node))
                throw new KeyNotFoundException($"Key '{key}' not found.");

            // Promote to most recently used
            _list.Remove(node);
            _list.AddLast(node);

            return node.Value.Value;
        }

        // Try to get a value without throwing
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_dictionary.TryGetValue(key, out var node))
            {
                // Promote to most recently used
                _list.Remove(node);
                _list.AddLast(node);

                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }

        // Check if a key exists (does NOT affect LRU order)
        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

        // Remove a specific key
        public bool Remove(TKey key)
        {
            if (!_dictionary.TryGetValue(key, out var node))
                return false;

            _list.Remove(node);
            _dictionary.TryRemove(key, out _);
            return true;
        }

        // Clear all items
        public void Clear()
        {
            _dictionary.Clear();
            _list.Clear();
        }

        // Peek at a value WITHOUT promoting it to most recently used
        public bool TryPeek(TKey key, out TValue value)
        {
            if (_dictionary.TryGetValue(key, out var node))
            {
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }

        // Get the least recently used key (next to be evicted)
        public TKey LeastRecentlyUsedKey => _list.Count > 0
            ? _list.First.Value.Key
            : throw new InvalidOperationException("Dictionary is empty.");

        // Get the most recently used key
        public TKey MostRecentlyUsedKey => _list.Count > 0
            ? _list.Last.Value.Key
            : throw new InvalidOperationException("Dictionary is empty.");

        // Enumerate all key-value pairs (LRU to MRU order)
        public IEnumerable<KeyValuePair<TKey, TValue>> GetItems()
            => _list.Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value));
    }
}
