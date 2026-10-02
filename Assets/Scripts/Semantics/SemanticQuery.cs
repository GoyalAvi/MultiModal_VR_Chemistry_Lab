using System;
using System.Collections.Generic;
using System.Linq;

namespace MMI.Semantics
{
    /// <summary>Combinable property query, e.g. new SemanticQuery().Where("type", "flask").Where("color", "blue").Resolve().</summary>
    public class SemanticQuery
    {
        private readonly List<(string key, string value)> _filters = new List<(string, string)>();

        /// <summary>Adds a filter. Null or empty values are ignored, so unspoken properties can be passed straight in.</summary>
        public SemanticQuery Where(string key, string value)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                _filters.Add((key, value));
            return this;
        }

        /// <summary>All entities matching every filter.</summary>
        // An empty query matches nothing on purpose; if you want "any object", don't build a query.
        public IEnumerable<ISemanticEntity> Resolve()
        {
            if (_filters.Count == 0) yield break;

            foreach (ISemanticEntity entity in SemanticRegistry.All)
            {
                bool matchesAll = true;
                foreach (var (key, value) in _filters)
                {
                    if (!entity.TryGetProperty(key, out string actual) ||
                        !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
                    {
                        matchesAll = false;
                        break;
                    }
                }
                if (matchesAll) yield return entity;
            }
        }

        /// <summary>First match, or null.</summary>
        public ISemanticEntity ResolveFirst() => Resolve().FirstOrDefault();
    }
}
