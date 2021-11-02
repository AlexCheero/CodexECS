using System;
using System.Collections.Generic;

namespace ECS
{
    class TrieNode<K, V>
    {
        public V Value;
        public Dictionary<K, TrieNode<K, V>> Prev;
        public Dictionary<K, TrieNode<K, V>> Next;
        public bool IsIntermediate;

        public TrieNode(V val = default, bool intermediate = true)
        {
            Value = val;
            //TODO: keep Prev and Next null, if it is a leaf node
            Prev = new Dictionary<K, TrieNode<K, V>>();
            Next = new Dictionary<K, TrieNode<K, V>>();
            IsIntermediate = intermediate;
        }

        public bool TryAdd(K[] keys, V value)
        {
            if (keys.Length < 1)
                throw new Exception("empty key path");
            Array.Sort(keys);
            //TODO: check that all key entries are unique
            var current = this;
            int i = 0;
            for (; i < keys.Length - 1; i++)
            {
                //TODO: compress intermediate keys
                if (!current.Next.ContainsKey(keys[i]))
                    current.Next.Add(keys[i], new TrieNode<K, V>(default, true));
                current = current.Next[keys[i]];
            }
            if (!current.Next.ContainsKey(keys[i]))
            {
                current.Next.Add(keys[i], new TrieNode<K, V>(value, false));
                return true;
            }
            else
            {
                var last = current.Next[keys[i]];
                if (last.IsIntermediate)
                {
                    last.IsIntermediate = false;
                    last.Value = value;
                    return true;
                }
            }
            return false;
        }

        public bool TryGet(K[] keys, ref V value)
        {
            if (keys.Length < 1)
                throw new Exception("empty key path");
            Array.Sort(keys);
            //TODO: check that all key entries are unique
            var current = this;
            int i = 0;
            for (; i < keys.Length; i++)
            {
                if (!current.Next.ContainsKey(keys[i]))
                {
                    return false;
                }
                current = current.Next[keys[i]];
            }

            if (current.IsIntermediate)
                return false;

            value = ref current.Value;
            return true;
        }
    }
}
