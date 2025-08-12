using Content.Server._NF.Cargo.Systems;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Cargo.Components;

[RegisterComponent]
[Access(typeof(NFCargoSystem))]
public sealed partial class NFCargoPalletConsoleComponent : Component
{
    // The stack prototype to spawn as currency for items sold from the pallet.
    // RU: Прототип стака, который спавнится как деньги за проданные предметы.
    [DataField]
    public ProtoId<StackPrototype> CashType = "Credit";

    // The radius around the console to check for cargo pallets (mappable per console).
    // RU: Радиус вокруг консоли для поиска паллет (можно задать на карте).
    [DataField]
    public int PalletDistance = 8;

    // Whitelist that determines what goods can be sold. Accepts everything if null.
    // RU: Белый список продаваемых сущностей; если null — разрешено всё.
    [DataField]
    public EntityWhitelist? Whitelist;

    // Whether this console's sales feed into the dynamic market system.
    // Pirate/freelancer consoles set this to false to avoid affecting global prices and UI.
    // RU: Влияет ли эта консоль на динамику рынка; пираты/фрилансеры обычно false.
    [DataField]
    public bool ContributesToMarket = true;
}
