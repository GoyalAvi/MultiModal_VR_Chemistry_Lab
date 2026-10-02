using UnityEngine;

namespace MMI.Semantics
{
    /// <summary>Something a SemanticQuery can find by key/value properties. The package doesn't care what the keys mean.</summary>
    public interface ISemanticEntity
    {
        /// <summary>The scene object this entity stands for.</summary>
        GameObject Owner { get; }

        /// <summary>Case-insensitive property lookup, e.g. TryGetProperty("color", out value).</summary>
        bool TryGetProperty(string key, out string value);
    }
}
