using System.Collections.Generic;

namespace MMI.Semantics
{
    /// <summary>All semantic entities currently in the scene.</summary>
    // Entities register themselves (usually OnEnable/OnDisable), so queries never scan the scene graph.
    public static class SemanticRegistry
    {
        private static readonly HashSet<ISemanticEntity> _entities = new HashSet<ISemanticEntity>();

        public static void Register(ISemanticEntity entity)
        {
            if (entity != null) _entities.Add(entity);
        }

        public static void Unregister(ISemanticEntity entity)
        {
            if (entity != null) _entities.Remove(entity);
        }

        public static IReadOnlyCollection<ISemanticEntity> All => _entities;
    }
}
