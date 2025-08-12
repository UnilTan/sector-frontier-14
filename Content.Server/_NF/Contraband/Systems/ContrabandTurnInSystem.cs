using Content.Server._NF.Contraband.Components;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._NF.Contraband;
using Content.Shared._NF.Contraband.BUI;
using Content.Shared._NF.Contraband.Components;
using Content.Shared._NF.Contraband.Events;
using Content.Shared.Contraband;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Content.Shared.Coordinates;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Content.Server._NF.Cargo.Systems;
using Content.Server.Hands.Systems;

namespace Content.Server._NF.Contraband.Systems;

// Handles contraband turn-in via pallet consoles.
// Mirrors cargo pallet logic but reads values from ContrabandComponent.
// RU: Приём контрабанды через паллеты; логика как в карго, но цены из ContrabandComponent.
public sealed partial class ContrabandTurnInSystem : SharedContrabandTurnInSystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<CargoSellBlacklistComponent> _blacklistQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _blacklistQuery = GetEntityQuery<CargoSellBlacklistComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();

        SubscribeLocalEvent<ContrabandPalletConsoleComponent, ContrabandPalletSellMessage>(OnPalletSale);
        SubscribeLocalEvent<ContrabandPalletConsoleComponent, ContrabandPalletAppraiseMessage>(OnPalletAppraise);
        SubscribeLocalEvent<ContrabandPalletConsoleComponent, BoundUIOpenedEvent>(OnPalletUIOpen);
    }

    private void UpdatePalletConsoleInterface(EntityUid uid, ContrabandPalletConsoleComponent comp)
    {
        // Ensure UI exists and update with current appraisal.
        var bui = _uiSystem.HasUi(uid, ContrabandPalletConsoleUiKey.Contraband);
        if (Transform(uid).GridUid is not EntityUid gridUid)
        {
            _uiSystem.SetUiState(uid, ContrabandPalletConsoleUiKey.Contraband,
                new ContrabandPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        GetPalletGoods(gridUid, comp, out var toSell, out var amount);

        _uiSystem.SetUiState(uid, ContrabandPalletConsoleUiKey.Contraband,
            new ContrabandPalletConsoleInterfaceState((int) amount, toSell.Count, true));
    }

    private void OnPalletUIOpen(EntityUid uid, ContrabandPalletConsoleComponent component, BoundUIOpenedEvent args)
    {
        var player = args.Actor;

        UpdatePalletConsoleInterface(uid, component);
    }

    // Appraisal is a lightweight refresh, same as opening the UI.
    // Avoid real-time recalculation on every content change to reduce load.
    // RU: Оценка — это лёгкое обновление как при открытии UI, без частого пересчёта.

    private void OnPalletAppraise(EntityUid uid, ContrabandPalletConsoleComponent component, ContrabandPalletAppraiseMessage args)
    {
        var player = args.Actor;

        UpdatePalletConsoleInterface(uid, component);
    }

    private List<(EntityUid Entity, ContrabandPalletComponent Component)> GetContrabandPallets(EntityUid gridUid)
    {
        var pads = new List<(EntityUid, ContrabandPalletComponent)>();
        var query = AllEntityQuery<ContrabandPalletComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var compXform))
        {
            if (compXform.ParentUid != gridUid ||
                !compXform.Anchored)
            {
                continue;
            }

            pads.Add((uid, comp));
        }

        return pads;
    }

    private void SellPallets(EntityUid consoleUid, EntityUid gridUid, ContrabandPalletConsoleComponent component, EntityUid? station, out int amount)
    {
        station ??= _station.GetOwningStation(gridUid);
        GetPalletGoods(gridUid, component, out var toSell, out amount);

        Log.Debug($"{component.Faction} sold {toSell.Count} contraband items for {amount}");

        if (station != null)
        {
            var ev = new NFEntitySoldEvent(toSell, gridUid, consoleUid);
            RaiseLocalEvent(ref ev);
        }

        foreach (var ent in toSell)
        {
            Del(ent);
        }
    }

    private void GetPalletGoods(EntityUid gridUid, ContrabandPalletConsoleComponent console, out HashSet<EntityUid> toSell, out int amount)
    {
        amount = 0;
        toSell = new HashSet<EntityUid>();

        foreach (var (palletUid, _) in GetContrabandPallets(gridUid))
        {
            foreach (var ent in _lookup.GetEntitiesIntersecting(palletUid,
                         LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate))
            {
                // Dont sell:
                // - anything already being sold
                // - anything anchored (e.g. light fixtures)
                // - anything blacklisted (e.g. players).
                if (toSell.Contains(ent) ||
                    _xformQuery.TryGetComponent(ent, out var xform) &&
                    (xform.Anchored || !CanSell(ent, xform)))
                {
                    continue;
                }

                if (_blacklistQuery.HasComponent(ent))
                    continue;

                if (TryComp<ContrabandComponent>(ent, out var comp))
                {
                    if (!comp.TurnInValues.ContainsKey(console.RewardType))
                        continue;

                    toSell.Add(ent);
                    var value = comp.TurnInValues[console.RewardType];
                    if (value <= 0)
                        continue;
                    amount += value;
                }
            }
        }
    }

    private bool CanSell(EntityUid uid, TransformComponent xform)
    {
        if (_mobQuery.HasComponent(uid))
        {
            if (_mobQuery.GetComponent(uid).CurrentState == MobState.Dead) // Allow selling alive prisoners
            {
                return false;
            }
            return true;
        }

        // Recursively check for mobs at any point.
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!CanSell(child, _xformQuery.GetComponent(child)))
                return false;
        }
        // Look for blacklisted items and stop the selling of the container.
        if (_blacklistQuery.HasComponent(uid))
        {
            return false;
        }
        return true;
    }

    private void OnPalletSale(EntityUid uid, ContrabandPalletConsoleComponent component, ContrabandPalletSellMessage args)
    {
        var player = args.Actor;

        if (Transform(uid).GridUid is not EntityUid gridUid)
        {
            _uiSystem.SetUiState(uid, ContrabandPalletConsoleUiKey.Contraband,
                new ContrabandPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        SellPallets(uid, gridUid, component, null, out var price);

        var stackPrototype = _protoMan.Index<StackPrototype>(component.RewardType);
        var stackUid = _stack.Spawn(price, stackPrototype, args.Actor.ToCoordinates());
        if (!_hands.TryPickupAnyHand(args.Actor, stackUid))
            _transform.SetLocalRotation(stackUid, Angle.Zero); // Orient these to grid north instead of map north
        UpdatePalletConsoleInterface(uid, component);
    }
}
