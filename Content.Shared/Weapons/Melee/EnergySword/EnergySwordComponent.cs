using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.EnergySword;

[RegisterComponent, NetworkedComponent, Access(typeof(EnergySwordSystem))]
[AutoGenerateComponentState]
public sealed partial class EnergySwordComponent : Component
{
    /// <summary>
    /// What color the blade will be when activated.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color ActivatedColor = Color.DodgerBlue;

    /// <summary>
    ///     A color option list for the random color picker.
    /// </summary>
    [DataField]
    public List<Color> ColorOptions = new()
    {
        Color.Tomato,
        Color.DodgerBlue,
        Color.Aqua,
        Color.MediumSpringGreen,
        Color.MediumOrchid,
        // Full HTML/CSS named colors (light/bright variants only)
        Color.AliceBlue,
        Color.AntiqueWhite,
        Color.Aqua,
        Color.Aquamarine,
        Color.Azure,
        Color.Beige,
        Color.Bisque,
        Color.BlanchedAlmond,
        Color.BurlyWood,
        Color.CadetBlue,
        Color.Chartreuse,
        Color.Coral,
        Color.CornflowerBlue,
        Color.Cornsilk,
        Color.Crimson,
        Color.Cyan,
        Color.DeepPink,
        Color.DeepSkyBlue,
        Color.DodgerBlue,
        Color.FloralWhite,
        Color.Fuchsia,
        Color.Gainsboro,
        Color.GhostWhite,
        Color.Gold,
        Color.Goldenrod,
        Color.GreenYellow,
        Color.Honeydew,
        Color.HotPink,
        Color.IndianRed,
        Color.Ivory,
        Color.Khaki,
        Color.Lavender,
        Color.LavenderBlush,
        Color.LawnGreen,
        Color.LemonChiffon,
        Color.LightBlue,
        Color.LightCoral,
        Color.LightCyan,
        Color.LightGoldenrodYellow,
        Color.LightGreen,
        Color.LightGray,
        Color.LightPink,
        Color.LightSalmon,
        Color.LightSeaGreen,
        Color.LightSkyBlue,
        Color.LightSlateGray,
        Color.LightSteelBlue,
        Color.LightYellow,
        Color.Lime,
        Color.LimeGreen,
        Color.Linen,
        Color.Magenta,
        Color.MediumAquamarine,
        Color.MediumOrchid,
        Color.MediumPurple,
        Color.MediumSeaGreen,
        Color.MediumSlateBlue,
        Color.MediumSpringGreen,
        Color.MediumTurquoise,
        Color.MediumVioletRed,
        Color.MintCream,
        Color.MistyRose,
        Color.Moccasin,
        Color.NavajoWhite,
        Color.OldLace,
        Color.Orange,
        Color.OrangeRed,
        Color.Orchid,
        Color.PaleGoldenrod,
        Color.PaleGreen,
        Color.PaleTurquoise,
        Color.PaleVioletRed,
        Color.PapayaWhip,
        Color.PeachPuff,
        Color.Pink,
        Color.Plum,
        Color.PowderBlue,
        Color.Red,
        Color.RosyBrown,
        Color.RoyalBlue,
        Color.Salmon,
        Color.SandyBrown,
        Color.SeaGreen,
        Color.SeaShell,
        Color.Silver,
        Color.SkyBlue,
        Color.Snow,
        Color.SpringGreen,
        Color.Tan,
        Color.Thistle,
        Color.Tomato,
        Color.Turquoise,
        Color.Violet,
        Color.Wheat,
        Color.White,
        Color.WhiteSmoke,
        Color.Yellow,
        Color.YellowGreen,
        // Non-standard but available named colors in this codebase
        Color.Ruber,
        Color.BetterViolet,
        Color.VividGamboge,
    };

    /// <summary>
    /// Whether the energy sword has been pulsed by a multitool,
    /// causing the blade to cycle RGB colors.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Hacked;

    /// <summary>
    ///     RGB cycle rate for hacked e-swords.
    /// </summary>
    [DataField]
    public float CycleRate = 1f;

    // Frontier: block changing colour
    /// <summary>
    ///     RGB cycle rate for hacked e-swords.
    /// </summary>
    [DataField]
    public bool BlockHacking = false;
    // End Frontier
}
