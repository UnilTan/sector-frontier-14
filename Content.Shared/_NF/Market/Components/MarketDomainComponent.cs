using Robust.Shared.GameStates;

namespace Content.Shared._NF.Market.Components
{
    /// <summary>
    /// Assigns a dynamic pricing domain to a console or entity. If Domain is Disabled, dynamic pricing is bypassed.
    /// </summary>
    [RegisterComponent, NetworkedComponent]
    public sealed partial class MarketDomainComponent : Component
    {
        [DataField("domain")]
        public _NF.Market.MarketDomain Domain = _NF.Market.MarketDomain.Default;
    }
}


