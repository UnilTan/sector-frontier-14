using Content.Server.Cargo.Components;
using Content.Server._NF.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Cargo.BUI;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Events;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using System.Numerics;
using Content.Shared.Coordinates;
using Content.Shared.Stacks; //Lua: for StackComponent

namespace Content.Server._NF.Cargo.Systems;

// Handles cargo pallet mechanics (UI updates, appraisal, selling).
// Based on Wizden's CargoSystem with Frontier dynamic pricing integration.
// RU: Механики паллет карго: UI, оценка, продажа; интеграция динамики Frontier.
public sealed partial class NFCargoSystem
{
    // The maximum distance from the console to look for pallets.
    private const int DefaultPalletDistance = 8;

    private static readonly SoundPathSpecifier ApproveSound = new("/Audio/Effects/Cargo/ping.ogg");

    // Subscribes to relevant events for pallet consoles and round lifecycle.
    // RU: Подписка на события консоли паллет и жизненного цикла раунда.
    private void InitializeShuttle()
    {
        SubscribeLocalEvent<NFCargoPalletConsoleComponent, CargoPalletSellMessage>(OnPalletSale);
        SubscribeLocalEvent<NFCargoPalletConsoleComponent, CargoPalletAppraiseMessage>(OnPalletAppraise);
        SubscribeLocalEvent<NFCargoPalletConsoleComponent, BoundUIOpenedEvent>(OnPalletUIOpen);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    #region Console

    // Recomputes the UI state, including dynamic pricing preview and reduction summary.
    // For non-contributing consoles, shows a minimal UI.
    // RU: Пересчитывает состояние UI; для не-влияющих на рынок консолей — упрощённый UI.
    private void UpdatePalletConsoleInterface(Entity<NFCargoPalletConsoleComponent> ent)
    {
        if (Transform(ent).GridUid is not EntityUid gridUid)
        {
            _ui.SetUiState(ent.Owner, CargoPalletConsoleUiKey.Sale,
            new NFCargoPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        // Modify preview price with tax and dynamic pricing.
        // RU: Корректирует предварительную цену с учётом налога и динамики.
        GetPalletGoods(ent, gridUid, out var toSell, out var amount, out var noModAmount);

        // Compute dynamic reduction summary and tax for UI
        double dynamicMultiplierAvg = 1.0;
        double taxMultiplier = 1.0;
        if (TryComp<MarketModifierComponent>(ent, out var priceMod))
        {
            taxMultiplier = priceMod.Mod;
        }

        // Estimate dynamic effect by averaging multipliers of unique prototypes present.
        // This is an approximation for a compact UI summary.
        var uniqueProtos = new HashSet<string>();
        foreach (var uid in toSell)
        {
            if (_market.TryGetDynamicPrototypeId(uid, out var pid))
                uniqueProtos.Add(pid);
        }
        if (uniqueProtos.Count > 0)
        {
            double sum = 0;
            foreach (var pid in uniqueProtos)
                sum += _market.GetCurrentDynamicMultiplier(pid);
            dynamicMultiplierAvg = sum / uniqueProtos.Count;
        }

        // Apply tax to taxable amount
        amount *= taxMultiplier;
        // Add immune amount after tax (immune to tax and dynamic)
        amount += noModAmount;
        //Lua End

        // Build UI reduction text. For consoles that do not contribute to market, hide system UI.
        var reductionText = "";
        var contributes = ent.Comp.ContributesToMarket;
        // Compute per-prototype batch size (stacks count as 1)
        var previewBatchByProto = new Dictionary<string, int>();
        if (contributes)
        {
            foreach (var uid in toSell)
            {
                if (!_market.TryGetDynamicPrototypeId(uid, out var pid))
                    continue;

                // Count 1 per stack
                var units = 1;
                if (!previewBatchByProto.TryAdd(pid, units))
                    previewBatchByProto[pid] += units;
            }
        }

        // Weighted average of projected multipliers by batch count
        double afterWeighted = 1.0;
        int dynPercentInt = 0;
        if (contributes && previewBatchByProto.Count > 0)
        {
            double sumWeighted = 0;
            double totalUnits = 0;
            foreach (var (pid, count) in previewBatchByProto)
            {
                var system = ResolveRoutingSystem(ent.Owner);
                var projected = (system?.GetProjectedMultiplierAfterSale(pid, count)) ?? 1.0;
                sumWeighted += projected * count;
                totalUnits += count;
            }
            afterWeighted = totalUnits > 0 ? sumWeighted / totalUnits : 1.0;
            var dynPercent = (1.0 - afterWeighted) * 100.0;
            dynPercentInt = Math.Max(0, (int)Math.Round(dynPercent));
            reductionText = $"-{dynPercentInt}%";
        }
        //Lua End

        // Compute real preview price with tax and dynamics (including bulk preview effect).
        // RU: Считает реальную цену предпросмотра с налогом и динамикой (с учётом эффекта партии).
        double real = 0.0;
        foreach (var uid in toSell)
        {
            var basePrice = _pricing.GetPrice(uid);
            if (basePrice <= 0)
                continue;

            if (HasComp<IgnoreMarketModifierComponent>(uid))
            {
                real += basePrice;
                continue;
            }

            if (contributes && _market.TryGetDynamicPrototypeId(uid, out var pid))
            {
                var units = 1; // Stacks treated as 1. RU: Стек считается за 1.
                var system = ResolveRoutingSystem(ent.Owner);
                var dyn = (system?.GetEffectiveMultiplierForBatch(pid, previewBatchByProto.GetValueOrDefault(pid, units))) ?? 1.0;
                var taxed = basePrice * taxMultiplier * dyn;
                var minAfterTax = basePrice * _market.GetDynamicMinAfterTaxBaseFraction(pid);
                real += Math.Max(minAfterTax, taxed);
            }
            else
            {
                var taxed = basePrice * taxMultiplier;
                // For non-contributing consoles, do not clamp by dynamic min.
                // RU: Для консоли, не влияющей на рынок, без минимального порога динамики.
                if (contributes)
                {
                    var minAfterTax = basePrice * _market.GetDynamicMinAfterTaxBaseFraction();
                    real += Math.Max(minAfterTax, taxed);
                }
                else
                {
                    real += taxed;
                }
            }
        }

        var minimalUi = !ent.Comp.ContributesToMarket;
        _ui.SetUiState(ent.Owner, CargoPalletConsoleUiKey.Sale,
            new NFCargoPalletConsoleInterfaceState((int)amount, toSell.Count, true, reductionText, (int)real, dynPercentInt, minimalUi));
    }

    private void OnPalletUIOpen(Entity<NFCargoPalletConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdatePalletConsoleInterface(ent);
    }

    /// <summary>
    /// Ok so this is just the same thing as opening the UI, its a refresh button.
    /// I know this would probably feel better if it were like predicted and dynamic as pallet contents change
    /// However.
    /// I dont want it to explode if cargo uses a conveyor to move 8000 pineapple slices or whatever, they are
    /// known for their entity spam i wouldnt put it past them
    /// </summary>

    private void OnPalletAppraise(Entity<NFCargoPalletConsoleComponent> ent, ref CargoPalletAppraiseMessage args)
    {
        UpdatePalletConsoleInterface(ent);
    }

    #endregion

    #region Shuttle

    // Calculates Euclidean distance between two coordinates (for world-space checks).
    // RU: Евклидово расстояние между координатами; для проверки близости паллет.
    public static double CalculateDistance(EntityCoordinates point1, EntityCoordinates point2)
    {
        var xDifference = point2.X - point1.X;
        var yDifference = point2.Y - point1.Y;

        return Math.Sqrt(xDifference * xDifference + yDifference * yDifference);
    }

    /// GetCargoPallets(gridUid, BuySellType.Sell) to return only Sell pads
    /// GetCargoPallets(gridUid, BuySellType.Buy) to return only Buy pads
    private List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent PalletXform)> GetCargoPallets(EntityUid consoleUid, EntityUid gridUid, BuySellType requestType = BuySellType.All)
    {
        _pads.Clear();

        if (!TryComp(consoleUid, out TransformComponent? consoleXform))
            return _pads;

        var query = AllEntityQuery<CargoPalletComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var compXform))
        {
            // Short-path easy checks
            if (compXform.ParentUid != gridUid
                || !compXform.Anchored
                || (requestType & comp.PalletType) == 0)
            {
                continue;
            }

            // Check distance to pallets. RU: Проверка дистанции до паллетов.
            var distance = CalculateDistance(compXform.Coordinates, consoleXform.Coordinates);
            var maxPalletDistance = DefaultPalletDistance;

            // Read the mapped checking distance from the console component.
            // RU: Берём радиус проверки из компонента консоли (если задан).
            if (TryComp<NFCargoPalletConsoleComponent>(consoleUid, out var cargoShuttleComponent))
                maxPalletDistance = cargoShuttleComponent.PalletDistance;

            if (distance > maxPalletDistance)
                continue;

            _pads.Add((uid, comp, compXform));

        }

        return _pads;
    }
    #endregion

    #region Station

        // Deletes items from pallets and raises sale events after computing final price.
        // Returns whether any items were actually sold.
        // RU: Удаляет предметы с паллет и шлёт события продажи после расчёта финальной цены.
        private bool SellPallets(Entity<NFCargoPalletConsoleComponent> consoleUid, EntityUid gridUid, out double amount, out double noMultiplierAmount)
    {
        GetPalletGoods(consoleUid, gridUid, out var toSell, out amount, out noMultiplierAmount);

        Log.Debug($"Cargo sold {toSell.Count} entities for {amount} (plus {noMultiplierAmount} without mods)");

        if (toSell.Count == 0)
            return false;

            var ev = new NFEntitySoldEvent(toSell, gridUid, consoleUid.Owner);
        RaiseLocalEvent(ref ev);

        foreach (var ent in toSell)
            Del(ent);

        return true;
    }

    // Enumerates sellable entities on pallets within range and sums their prices.
    // Splits amounts into taxable/dynamic and immune buckets.
    // RU: Перечисляет продаваемые сущности на паллетах и суммирует их цены,
    // RU: разделяя на облагаемые/динамические и иммунные суммы.
    private void GetPalletGoods(Entity<NFCargoPalletConsoleComponent> consoleUid, EntityUid gridUid, out HashSet<EntityUid> toSell, out double amount, out double noMultiplierAmount)
    {
        amount = 0;
        noMultiplierAmount = 0;
        toSell = new HashSet<EntityUid>();

        foreach (var (palletUid, _, _) in GetCargoPallets(consoleUid, gridUid, BuySellType.Sell))
        {
            // Containers should already get the sell price of their children so can skip those.
            _setEnts.Clear();

            _lookup.GetEntitiesIntersecting(palletUid, _setEnts,
                LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var ent in _setEnts)
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

                if (_whitelist.IsWhitelistFail(consoleUid.Comp.Whitelist, ent))
                    continue;

                if (_blacklistQuery.HasComponent(ent))
                    continue;

                var price = _pricing.GetPrice(ent);
                if (price == 0)
                    continue;
                toSell.Add(ent);

                // Items immune to market modifiers (e.g. cash) are counted separately.
                // RU: Иммунные к модификаторам (например, деньги) учитываются отдельно.
                if (HasComp<IgnoreMarketModifierComponent>(ent))
                    noMultiplierAmount += price;
                else
                    amount += price;
            }
        }
    }

    private bool CanSell(EntityUid uid, TransformComponent xform)
    {
        // Look for blacklisted items and stop the selling of the container.
        if (_blacklistQuery.HasComponent(uid))
            return false;

        // Allow selling dead mobs
        if (_mobQuery.TryComp(uid, out var mob) && mob.CurrentState != MobState.Dead)
            return false;

        // NOTE: no bounties for now
        // var complete = IsBountyComplete(uid, out var bountyEntities);

        // Recursively check for mobs at any point.
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            // NOTE: no bounties for now
            // if (complete && bountyEntities.Contains(child))
            //     continue;

            if (!CanSell(child, _xformQuery.GetComponent(child)))
                return false;
        }

        return true;
    }

    // Handles the sell action: computes price with dynamic preview batch logic,
    // deletes items, spawns cash, then applies bulk dynamic effects.
    // RU: Обработка продажи: расчёт цены с батч-превью, удаление, выдача денег, оптовой эффект.
    private void OnPalletSale(Entity<NFCargoPalletConsoleComponent> ent, ref CargoPalletSellMessage args)
    {
        if (!TryComp(ent, out TransformComponent? xform))
            return;

        if (xform.GridUid is not EntityUid gridUid)
        {
            _ui.SetUiState(ent.Owner, CargoPalletConsoleUiKey.Sale,
            new NFCargoPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        // Compute price before deletion (prevents zero after deletion).
        GetPalletGoods(ent, gridUid, out var toSellNow, out _, out _);
        if (toSellNow.Count == 0)
            return;

            // Handle market modifiers, immune objects and dynamic pricing.
            // RU: Учёт налога, иммунных объектов и динамики.
        double taxMultiplier = 1.0;
        if (TryComp<MarketModifierComponent>(ent, out var priceMod))
            taxMultiplier = priceMod.Mod;

            // Build preview-batch by prototype (stacks count as 1) to match UI pricing preview.
            // RU: Формируем размеры партий по прототипам (стек=1) как в превью UI.
            var previewBatchByProto = new Dictionary<string, int>();
            var contributesSale = ent.Comp.ContributesToMarket;
            if (contributesSale)
            {
                foreach (var uid in toSellNow)
                {
                    if (!_market.TryGetDynamicPrototypeId(uid, out var pid))
                        continue;
                    var units = 1; // Stacks count as 1 for pricing preview. RU: Стек=1 для превью.
                    if (!previewBatchByProto.TryAdd(pid, units))
                        previewBatchByProto[pid] += units;
                }
            }

            double finalPrice = 0.0;
            foreach (var uid in toSellNow)
            {
                var basePrice = _pricing.GetPrice(uid);
                if (basePrice <= 0)
                    continue;

                if (HasComp<IgnoreMarketModifierComponent>(uid))
                {
                    finalPrice += basePrice; // Immune to tax & dynamic.
                    continue;
                }

                if (contributesSale && _market.TryGetDynamicPrototypeId(uid, out var pid))
                {
                    var system = ResolveRoutingSystem(ent.Owner);
                    var batchUnits = previewBatchByProto.GetValueOrDefault(pid, 1);
                    var dyn = (system?.GetEffectiveMultiplierForBatch(pid, batchUnits)) ?? 1.0;
                    var taxed = basePrice * taxMultiplier * dyn;
                    var minAfterTax = basePrice * _market.GetDynamicMinAfterTaxBaseFraction(pid);
                    finalPrice += Math.Max(minAfterTax, taxed);
                }
                else
                {
                    var taxed = basePrice * taxMultiplier;
                    if (contributesSale)
                    {
                        var minAfterTax = basePrice * _market.GetDynamicMinAfterTaxBaseFraction();
                        finalPrice += Math.Max(minAfterTax, taxed);
                    }
                    else
                    {
                        finalPrice += taxed;
                    }
                }
            }

        // Compute batch sizes by prototype (for post-sale bulk effect).
        // RU: Размеры партий по прототипам для последующего оптового эффекта.
            var bulkByProto = new Dictionary<string, int>();
        if (contributesSale)
        {
            foreach (var uid in toSellNow)
            {
                if (_market.TryGetDynamicPrototypeId(uid, out var pid))
                {
                    var units = 1;
                    if (TryComp<StackComponent>(uid, out var stack))
                        units = int.Max(1, stack.Count);

                    if (!bulkByProto.TryAdd(pid, units))
                        bulkByProto[pid] += units;
                }
            }
        }

        // Now delete and send events. RU: Теперь удаляем и шлём события.
        if (!SellPallets(ent, gridUid, out _, out _))
            return; // someone snatched items
        var price = finalPrice;

        var stackPrototype = _proto.Index(ent.Comp.CashType);
        var stackUid = _stack.Spawn((int)price, stackPrototype, args.Actor.ToCoordinates());
        if (!_hands.TryPickupAnyHand(args.Actor, stackUid))
            _transform.SetLocalRotation(stackUid, Angle.Zero); // Orient these to grid north instead of map north
        _audio.PlayPvs(ApproveSound, ent);
        // Apply bulk effect to dynamic state after sale. RU: Применяем оптовое снижение.
        if (contributesSale)
        {
            var sys = ResolveRoutingSystem(ent.Owner);
            foreach (var (pid, count) in bulkByProto)
                sys?.ApplyBulkSaleEffect(pid, count);
        }

        UpdatePalletConsoleInterface(ent);
    }

    #endregion
}

/// <summary>
/// Event broadcast raised by-ref before it is sold and
/// deleted but after the price has been calculated.
/// </summary>
[ByRefEvent]
public readonly record struct NFEntitySoldEvent(HashSet<EntityUid> Sold, EntityUid Grid, EntityUid SourceConsole);
