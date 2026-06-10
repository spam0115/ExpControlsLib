
namespace WindowsApiLib
{
    /// <summary>
    /// A simple implementation of a Least Recently Used (LRU) cache using a dictionary and a linked list.
    /// 
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class LruConcurrentDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _dictionary;
        private readonly LinkedList<(TKey Key, TValue Value)> _list = new();
        private readonly object _lock = new();

        public LruConcurrentDictionary(int capacity)
        {
            _capacity = capacity;
            _dictionary = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
        }

        public int Count { get { lock (_lock) return _dictionary.Count; } }
        public int Capacity => _capacity;
        public IEnumerable<TKey> Keys { get { lock (_lock) return _dictionary.Keys.ToArray(); } }
        public IEnumerable<TValue> Values { get { lock (_lock) return _list.Select(x => x.Value).ToArray(); } }

        // Indexer - get or set a value by key
        public TValue this[TKey key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        // Add a new item (throws if key already exists)
        public void Add(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_dictionary.ContainsKey(key))
                    throw new ArgumentException($"Key '{key}' already exists.");

                SetInternal(key, value);
            }
        }

        public bool TryAdd(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_dictionary.ContainsKey(key))
                    return false;

                SetInternal(key, value);
                return true;
            }
        }

        // Add or update a value (upsert)
        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                SetInternal(key, value);
            }
        }

        private void SetInternal(TKey key, TValue value)
        {
            if (_dictionary.TryGetValue(key, out var existingNode))
            {
                // Update existing - move to end (most recently used)
                _list.Remove(existingNode);
                _dictionary.Remove(key);
            }
            else if (_dictionary.Count >= _capacity)
            {
                // Evict least recently used (front of list)
                var lru = _list.First.Value;
                _list.RemoveFirst();
                _dictionary.Remove(lru.Key);
            }

            var node = _list.AddLast((key, value));
            _dictionary[key] = node;
        }

        // Get a value by key (promotes to most recently used)
        public TValue Get(TKey key)
        {
            lock (_lock)
            {
                if (!_dictionary.TryGetValue(key, out var node))
                    throw new KeyNotFoundException($"Key '{key}' not found.");

                // Promote to most recently used
                _list.Remove(node);
                _list.AddLast(node);

                return node.Value.Value;
            }
        }

        // Try to get a value without throwing
        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
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
        }

        // Check if a key exists (does NOT affect LRU order)
        public bool ContainsKey(TKey key)
        {
            lock (_lock)
            {
                return _dictionary.ContainsKey(key);
            }
        }

        // Remove a specific key
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                if (!_dictionary.TryGetValue(key, out var node))
                    return false;

                _list.Remove(node);
                _dictionary.Remove(key);
                return true;
            }
        }

        // Clear all items
        public void Clear()
        {
            lock (_lock)
            {
                _dictionary.Clear();
                _list.Clear();
            }
        }

        // Peek at a value WITHOUT promoting it to most recently used
        public bool TryPeek(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_dictionary.TryGetValue(key, out var node))
                {
                    value = node.Value.Value;
                    return true;
                }

                value = default;
                return false;
            }
        }

        // Get the least recently used key (next to be evicted)
        public TKey LeastRecentlyUsedKey
        {
            get
            {
                lock (_lock)
                {
                    return _list.Count > 0
                        ? _list.First.Value.Key
                        : throw new InvalidOperationException("Dictionary is empty.");
                }
            }
        }

        // Get the most recently used key
        public TKey MostRecentlyUsedKey
        {
            get
            {
                lock (_lock)
                {
                    return _list.Count > 0
                        ? _list.Last.Value.Key
                        : throw new InvalidOperationException("Dictionary is empty.");
                }
            }
        }

        // Enumerate all key-value pairs (LRU to MRU order)
        public IEnumerable<KeyValuePair<TKey, TValue>> GetItems()
        {
            lock (_lock)
            {
                return _list.Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value)).ToArray();
            }
        }
    }
}
