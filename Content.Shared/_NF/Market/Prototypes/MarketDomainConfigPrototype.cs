using Robust.Shared.Prototypes;

namespace Content.Shared._NF.Market.Prototypes;

/// <summary>
/// Prototype to configure market domain pricing and per-category dynamic parameters.
/// One prototype per domain (Default, Syndicate, BlackMarket, etc.).
/// </summary>
[Prototype("marketDomainConfig")]
public sealed partial class MarketDomainConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Base price multiplier applied for this domain, on top of console-specific MarketModifier.
    /// Default 1.0.
    /// </summary>
    [DataField("baseMultiplier")]
    public float BaseMultiplier { get; private set; } = 1.0f;

    /// <summary>
    /// Parameters per category.
    /// Keys must match values from server-side category enum mapping.
    /// </summary>
    [DataField("categories")]
    public Dictionary<string, CategoryParamsPrototype> Categories { get; private set; } = new();

    [DataDefinition]
    public sealed partial class CategoryParamsPrototype
    {
        [DataField("decayPerStack")]
        public double DecayPerStack = 0.01;

        [DataField("bulkDecayPerStack")]
        public double BulkDecayPerStack = 0.002;

        [DataField("restorePerMinute")]
        public double RestorePerMinute = 0.01;

        [DataField("minAfterTaxBaseFraction")]
        public double MinAfterTaxBaseFraction = 0.25;
    }
}


