using System.Collections.Generic;
using UnityEngine;

/// <summary>Known chemicals, their spoken aliases and the reactions between them.</summary>
// Most of these solutions are colourless in reality. The colours show each solution as if universal
// indicator had been added (red = acidic, green = neutral, violet = basic), like in a school lab.
public static class ChemicalDatabase
{

    public class Chemical
    {
        public string id;           // lowercase key, e.g. "hcl"
        public string displayName;  // shown in labels and logs
        public Color  color;        // colour with indicator added, from phValue
        public string type;
        public float  phValue;      // approximate real pH; drives the colour of the chemical and of mixtures
    }

    // Universal indicator scale. Colours come from the pH, so a new chemical only needs a pH value.
    private static readonly (float ph, Color color)[] IndicatorGradient =
    {
        (0f,  new Color(0.77f, 0.09f, 0.09f)), // strongly acidic
        (3f,  new Color(0.95f, 0.45f, 0.05f)),
        (5f,  new Color(0.95f, 0.85f, 0.15f)),
        (7f,  new Color(0.20f, 0.75f, 0.25f)), // neutral
        (9f,  new Color(0.10f, 0.55f, 0.75f)),
        (11f, new Color(0.15f, 0.25f, 0.85f)),
        (14f, new Color(0.45f, 0.10f, 0.65f)), // strongly basic
    };

    /// <summary>Universal-indicator colour for a pH of 0-14.</summary>
    public static Color ColorForPH(float ph)
    {
        ph = Mathf.Clamp(ph, 0f, 14f);
        for (int i = 0; i < IndicatorGradient.Length - 1; i++)
        {
            (float phA, Color colorA) = IndicatorGradient[i];
            (float phB, Color colorB) = IndicatorGradient[i + 1];
            if (ph <= phB)
                return Color.Lerp(colorA, colorB, Mathf.InverseLerp(phA, phB, ph));
        }
        return IndicatorGradient[IndicatorGradient.Length - 1].color;
    }

    private static readonly Dictionary<string, Chemical> _chemicals =
        new Dictionary<string, Chemical>
    {
        // Acids (approx. pH of lab-strength solutions)
        { "hcl",   new Chemical { id="hcl",   displayName="HCl",   type="acid",    phValue=1f,   color=ColorForPH(1f)   } },
        { "h2so4", new Chemical { id="h2so4", displayName="H₂SO₄", type="acid",    phValue=1f,   color=ColorForPH(1f)   } },
        { "hno3",  new Chemical { id="hno3",  displayName="HNO₃",  type="acid",    phValue=1.5f, color=ColorForPH(1.5f) } },
        // Bases
        { "naoh",  new Chemical { id="naoh",  displayName="NaOH",  type="base",    phValue=13f,  color=ColorForPH(13f)  } },
        { "koh",   new Chemical { id="koh",   displayName="KOH",   type="base",    phValue=13f,  color=ColorForPH(13f)  } },
        { "nh3",   new Chemical { id="nh3",   displayName="NH₃",   type="base",    phValue=11f,  color=ColorForPH(11f)  } },
        // Neutral
        { "h2o",   new Chemical { id="h2o",   displayName="H₂O",   type="neutral", phValue=7f,   color=ColorForPH(7f)   } },
        { "nacl",  new Chemical { id="nacl",  displayName="NaCl",  type="neutral", phValue=7f,   color=ColorForPH(7f)   } },
        // Salts used as precipitation reagents
        { "agno3", new Chemical { id="agno3", displayName="AgNO₃", type="salt",    phValue=7f,   color=ColorForPH(7f)   } },
        // Quicklime: reacts with water and heats itself, no burner needed
        { "cao",   new Chemical { id="cao",   displayName="CaO",   type="base",    phValue=12.5f,color=ColorForPH(12.5f)} },
        { "cuso4", new Chemical { id="cuso4", displayName="CuSO₄", type="salt",    phValue=4f,   color=ColorForPH(4f)   } },
    };

    // What the recognizer may say -> chemical id

    private static readonly Dictionary<string, string> _aliases =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "hcl" , "hcl" }, { "hydrochloric", "hcl" }, { "hydrochloric acid", "hcl" },
        { "h2so4", "h2so4" }, { "sulfuric", "h2so4" }, { "sulfuric acid", "h2so4" },
        { "hno3", "hno3" }, { "nitric", "hno3" }, { "nitric acid", "hno3" },
        { "naoh", "naoh" }, { "sodium hydroxide", "naoh" }, { "hydroxide", "naoh" },
        { "koh", "koh" }, { "potassium hydroxide", "koh" },
        { "nh3", "nh3" }, { "ammonia", "nh3" },
        { "water", "h2o" }, { "h2o", "h2o" },
        { "salt", "nacl" }, { "nacl", "nacl" }, { "sodium chloride", "nacl" },
        { "agno3", "agno3" }, { "silver nitrate", "agno3" }, { "silver", "agno3" },
        { "cao", "cao" }, { "quicklime", "cao" }, { "calcium oxide", "cao" },
        { "cuso4", "cuso4" }, { "copper sulfate", "cuso4" }, { "copper", "cuso4" },
    };

    // Pairs with a scripted effect on top of the pH colour: a precipitate as soon as both are present,
    // fumes after sustained burner heat, or an exothermic reaction that heats itself.
    // LabContainer checks these; nothing here reacts on its own.

    public enum ReactionEffect { None, Precipitate, Fumes, Exothermic }

    private static readonly (string a, string b, ReactionEffect effect, string product)[] _reactionEffects =
    {
        ("agno3", "nacl", ReactionEffect.Precipitate, "AgCl(s)↓ + NaNO₃"),
        ("hcl",   "nh3",  ReactionEffect.Fumes,       "NH₄Cl"),             // NH4Cl splits back into gases when heated
        ("cao",   "h2o",  ReactionEffect.Precipitate, "Ca(OH)₂(s)↓"),       // Ca(OH)2 is barely soluble, so the excess settles out
        ("cao",   "h2o",  ReactionEffect.Exothermic,  "Ca(OH)₂(s)↓"),       // same pair: slaking is hot enough to boil the water off
        ("hcl",   "naoh", ReactionEffect.None,        "NaCl + H₂O"),        // no effect; only the label changes, the pH blend already turns it neutral
    };

    /// <summary>True if the contents contain a pair registered for this effect.</summary>
    public static bool HasEffect(IEnumerable<string> contentIds, ReactionEffect effect)
    {
        if (contentIds == null) return false;
        var ids = new HashSet<string>(contentIds);
        foreach (var (a, b, e, _) in _reactionEffects)
            if (e == effect && ids.Contains(a) && ids.Contains(b))
                return true;
        return false;
    }

    /// <summary>Product formula of a registered pair in the contents, or null.</summary>
    public static string GetProductLabel(IEnumerable<string> contentIds)
    {
        if (contentIds == null) return null;
        var ids = new HashSet<string>(contentIds);
        foreach (var (a, b, _, product) in _reactionEffects)
            if (ids.Contains(a) && ids.Contains(b))
                return product;
        return null;
    }

    /// <summary>Chemical by id or alias, or null.</summary>
    public static Chemical GetChemical(string nameOrAlias)
    {
        if (string.IsNullOrEmpty(nameOrAlias)) return null;
        string key = nameOrAlias.Trim().ToLower();
        if (_aliases.TryGetValue(key, out string id) && _chemicals.TryGetValue(id, out Chemical c))
            return c;
        if (_chemicals.TryGetValue(key, out Chemical direct))
            return direct;
        return null;
    }

    /// <summary>Alias -> chemical id, e.g. "hydrochloric acid" -> "hcl". Feeds the engine's "chemical" slot.</summary>
    public static Dictionary<string, string> GetAliases() => new Dictionary<string, string>(_aliases);
}