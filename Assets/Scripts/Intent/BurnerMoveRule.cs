/// <summary>Decides who moves where in a "move ... burner" sentence, from the engine's slots only.</summary>
// The burner is the destination after "to/on (the) burner" or when no "there/here" was said, so "move
// burner" with a dropped "to" still works. Only "move the burner there" moves the burner itself.
public static class BurnerMoveRule
{
    public enum Destination { BurnerStand, PointedSpot }
    public enum TargetRule { Pointed, Named, Selection }

    public struct Decision
    {
        public Destination Destination;
        public TargetRule Rule;
        public string TargetType;   // null = search by colour only
        public bool Refused;
        public string Reason;

        public override string ToString() => Refused
            ? $"refused ({Reason})"
            : $"target {Rule}{(Rule == TargetRule.Named ? $" ({TargetType ?? "by colour"})" : "")} → {Destination}";
    }

    public static Decision Decide(string objectType, string color, bool burnerDest, bool spatialDest, bool burnerSelf,
                                  bool pointing, bool hasSelection, bool selectionIsBurner)
    {
        bool otherType = !string.IsNullOrEmpty(objectType) && objectType != "burner";
        // "move the burner there" misheard as "move to burner there" with the burner selected: moving it
        // onto itself makes no sense, so "there" wins
        if (burnerDest && spatialDest && !burnerSelf && selectionIsBurner && !otherType && string.IsNullOrEmpty(color))
            return new Decision { Destination = Destination.PointedSpot, Rule = TargetRule.Selection };

        bool burnerIsDestination = burnerDest || !spatialDest;
        if (!burnerIsDestination)
        {
            return new Decision { Destination = Destination.PointedSpot, Rule = TargetRule.Named, TargetType = "burner" };
        }

        var d = new Decision { Destination = Destination.BurnerStand };
        if (burnerSelf)
            return Refuse(d, TargetRule.Named, "can't move the burner onto itself");
        if (pointing)
        {
            d.Rule = TargetRule.Pointed;                    // "move that to the burner"
            return d;
        }
        if (otherType || !string.IsNullOrEmpty(color))
        {
            d.Rule = TargetRule.Named;                      // "move the red beaker to the burner"
            d.TargetType = otherType ? objectType : null;
            return d;
        }
        if (!hasSelection) return Refuse(d, TargetRule.Selection, "no target — nothing selected, pointed at or named");
        if (selectionIsBurner) return Refuse(d, TargetRule.Selection, "can't move the burner onto itself");
        d.Rule = TargetRule.Selection;                      // "move to burner", "put it on the burner"
        return d;
    }

    private static Decision Refuse(Decision d, TargetRule rule, string reason)
    {
        d.Rule = rule;
        d.Refused = true;
        d.Reason = reason;
        return d;
    }
}
