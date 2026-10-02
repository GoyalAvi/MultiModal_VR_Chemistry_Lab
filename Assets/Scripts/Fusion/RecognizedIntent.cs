using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MMI.Fusion
{
    /// <summary>Result of fusion: the action, its spoken parameters and the pointing/gesture samples that completed it.</summary>
    // Resolving "green ball" to an actual object is up to the app.
    public class RecognizedIntent
    {
        public string Action { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }
        public IReadOnlyList<PointingSample> PointingTargets { get; }
        public GestureSample? Gesture { get; }

        public RecognizedIntent(
            string action,
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlyList<PointingSample> pointingTargets,
            GestureSample? gesture)
        {
            Action = action;
            Parameters = parameters ?? new Dictionary<string, string>();
            PointingTargets = pointingTargets ?? System.Array.Empty<PointingSample>();
            Gesture = gesture;
        }

        /// <summary>Parameter value, or null if the slot wasn't filled.</summary>
        public string GetParameter(string key) =>
            Parameters.TryGetValue(key, out string v) ? v : null;

        /// <summary>
        /// Readable form, e.g. "{action:select; target:red beaker (pointed: Beaker_03); color:red; objectType:beaker; pointing:[Beaker_03]}".
        /// "target" only appears when there are both parameters and pointing; empty parts are left out.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('{').Append("action:").Append(Action);

            if (Parameters.Count > 0 && PointingTargets.Count > 0)
            {
                sb.Append("; target:");
                bool first = true;
                foreach (var kv in Parameters)
                {
                    if (!first) sb.Append(' ');
                    sb.Append(kv.Value);
                    first = false;
                }
                sb.Append(" (pointed: ").Append(Describe(PointingTargets[0])).Append(')');
            }

            foreach (var kv in Parameters)
                sb.Append("; ").Append(kv.Key).Append(':').Append(kv.Value);

            if (PointingTargets.Count > 0)
            {
                sb.Append("; pointing:[");
                for (int i = 0; i < PointingTargets.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Describe(PointingTargets[i]));
                }
                sb.Append(']');
            }

            if (Gesture.HasValue)
                sb.Append("; gesture:").Append(Gesture.Value.Kind).Append('=')
                  .Append(Gesture.Value.Value.ToString("F2", CultureInfo.InvariantCulture));

            sb.Append('}');
            return sb.ToString();
        }

        // Hit object name, or the hit point in free space
        private static string Describe(PointingSample sample)
        {
            if (sample.HitObject != null) return sample.HitObject.name;
            var p = sample.HitPoint;
            return string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", p.x, p.y, p.z);
        }
    }
}
