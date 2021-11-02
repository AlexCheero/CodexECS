using System;
using System.Collections.Generic;
using FiltersTrieInternal = ECS.TrieNode<System.Type, ECS.TrieNode<System.Type, System.Collections.Generic.HashSet<int>>>;
using FiltersTrieLeaf = ECS.TrieNode<System.Type, System.Collections.Generic.HashSet<int>>;

namespace ECS
{
    class FiltersTrie
    {
        private FiltersTrieInternal _trie;

        public FiltersTrie()
        {
            _trie = new FiltersTrieInternal();
        }

        public void RegisterFilter(Type[] comps, Type[] excludes)
        {
            //TODO: implement GetOrAdd
            var leaf = new FiltersTrieLeaf();
            if (!_trie.TryGet(comps, ref leaf))
                _trie.TryAdd(comps, leaf);

            if (excludes == null || excludes.Length == 0)
            {
                if (leaf.Value == null)
                {
                    leaf.Value = new HashSet<int>();
                    leaf.IsIntermediate = false;
                }
            }
            else
            {
                leaf.TryAdd(excludes, new HashSet<int>());
            }
        }

        public HashSet<int> GetFilter(Type[] comps, Type[] excludes)
        {
            //TODO: implement more handy way to get values out of trie without unnecessary allowcations
            var leaf = new FiltersTrieLeaf();
            if (!_trie.TryGet(comps, ref leaf))
                return null;

            if (excludes == null || excludes.Length == 0)
                return leaf.IsIntermediate ? null : leaf.Value;

            var filter = new HashSet<int>();
            return leaf.TryGet(excludes, ref filter) ? filter : null;
        }
    }
}
