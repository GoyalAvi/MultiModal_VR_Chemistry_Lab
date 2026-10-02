using System.Collections.Generic;
using System.Linq;

namespace MMI.Fusion
{
    /// <summary>
    /// One interaction: its trigger phrases, parameter slots, how many pointing samples it takes and whether it needs a gesture.
    /// </summary>
    // A trigger phrase matches when all its words were spoken, in any order. Compound commands like
    // "burner" + "on" are one two-word phrase so they only fire when both words are present.
    public class InteractionDefinition
    {
        public string Action { get; }
        public IReadOnlyList<string[]> TriggerPhrases { get; }
        public IReadOnlyList<string> ParameterSlots { get; }

        /// <summary>Pointing samples this interaction can use (0 = never waits for pointing).</summary>
        public int MaxPointingSlots { get; }

        /// <summary>Whether it waits for a gesture sample before firing.</summary>
        public bool RequiresGesture { get; }

        /// <summary>Each trigger word is a synonym on its own ("select", "pick", "choose").</summary>
        public InteractionDefinition(
            string action,
            IEnumerable<string> triggerWords,
            IEnumerable<string> parameterSlots = null,
            int maxPointingSlots = 0,
            bool requiresGesture = false)
            : this(action, triggerWords.Select(w => new[] { w }), parameterSlots, maxPointingSlots, requiresGesture)
        {
        }

        /// <summary>Explicit multi-word trigger phrases, e.g. ["burner", "on"].</summary>
        public InteractionDefinition(
            string action,
            IEnumerable<string[]> triggerPhrases,
            IEnumerable<string> parameterSlots = null,
            int maxPointingSlots = 0,
            bool requiresGesture = false)
        {
            Action = action;
            TriggerPhrases = triggerPhrases.ToList();
            ParameterSlots = parameterSlots?.ToList() ?? new List<string>();
            MaxPointingSlots = maxPointingSlots;
            RequiresGesture = requiresGesture;
        }
    }
}
