using System;
using System.Collections.Generic;
using Content.Server._NF.Market.Systems;
using Content.Server._NF.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Random;
using Robust.Shared.Log;
using Content.Server._NF.Market.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;
using Content.Server.Cargo.Systems;
using Content.Server.Cargo.Components;
using Robust.Shared.Map;
using Content.Server._NF.Atmos.Components;
using Robust.Shared.Network;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Server.Player;

namespace Content.Server._NF.Market.Systems;

/// <summary>
/// Tracks recent cargo sales per station and provides demand-based price multipliers.
/// </summary>
public sealed class DynamicPricingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, StationState> _stateByStation = new();

    // Default tuning (can be later moved to cvars or prototypes)
    private const double OnlineMin = 12.0;
    private const double OnlineMax = 120.0;

    private const double AlphaLowDefault = 0.18;   // stronger damping at low online
    private const double AlphaHighDefault = 0.06;  // weaker damping at high online
    private static readonly TimeSpan TauDefault = TimeSpan.FromMinutes(60); // recovery half-life-ish scale

    private const double FloorDefault = 0.15;      // min price fraction of base
    private const double CapDefault = 1.20;        // max price fraction of base

    // Optional step effect (disabled by default)
    private const int StepAmountDefault = 15;   // quantity per step; 0 disables
    private const double StepDropDefault = 0.05; // 5% drop per step

    private sealed class PriceState
    {
        public double Supply; // accumulated, decayed quantity sold
        public TimeSpan LastUpdate;
    }

    private sealed class StationState
    {
        public readonly Dictionary<string, PriceState> ByPrototype = new();
        public TimeSpan LastCleanup;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NFEntitySoldEvent>(OnEntitySold);
    }

    private StationState GetStationState(EntityUid station)
    {
        if (!_stateByStation.TryGetValue(station, out var st))
        {
            st = new StationState { LastCleanup = _timing.CurTime }; 
            _stateByStation[station] = st;
        }
        return st;
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private double GetOnlineNormalized()
    {
        var n = (double) _players.PlayerCount;
        var x = (n - OnlineMin) / (OnlineMax - OnlineMin);
        return Clamp(x, 0.0, 1.0);
    }

    private void Decay(ref PriceState ps, TimeSpan now, TimeSpan tau)
    {
        if (ps.LastUpdate == default)
        {
            ps.LastUpdate = now;
            return;
        }
        var dt = (now - ps.LastUpdate).TotalSeconds;
        if (dt <= 0)
            return;
        var factor = Math.Exp(-dt / tau.TotalSeconds);
        ps.Supply *= factor;
        ps.LastUpdate = now;
    }

    private PriceState GetProtoState(StationState stationState, string protoId)
    {
        if (!stationState.ByPrototype.TryGetValue(protoId, out var ps))
        {
            ps = new PriceState { Supply = 0, LastUpdate = _timing.CurTime };
            stationState.ByPrototype[protoId] = ps;
        }
        return ps;
    }

    private void OnEntitySold(ref NFEntitySoldEvent ev)
    {
        // Find station by grid
        var station = _stations.GetOwningStation(ev.Grid);
        if (station is not { } stationUid)
            return;

        var stationState = GetStationState(stationUid);
        var now = _timing.CurTime;

        foreach (var sold in ev.Sold)
        {
            if (!TryComp<MetaDataComponent>(sold, out var meta) || meta.EntityPrototype is null)
                continue;

            // Determine sold quantity (1 or stack count)
            var qty = 1;
            if (TryComp<StackComponent>(sold, out var stack))
            {
                qty = stack.Count;
            }

            var protoId = meta.EntityPrototype.ID;
            var ps = GetProtoState(stationState, protoId);
            Decay(ref ps, now, TauDefault);
            ps.Supply += qty;
        }
    }

    /// <summary>
    /// Compute demand-based multiplier for a given prototype on a station.
    /// </summary>
    private double ComputeMultiplier(EntityUid station, string protoId)
    {
        if (!_stateByStation.TryGetValue(station, out var stationState))
            return 1.0; // no data yet

        var ps = GetProtoState(stationState, protoId);
        // Decay to now before computing
        var now = _timing.CurTime;
        Decay(ref ps, now, TauDefault);

        // Online scaling: at low online use AlphaLow, at high online AlphaHigh
        var x = GetOnlineNormalized();
        var k = Lerp(AlphaHighDefault, AlphaLowDefault, 1.0 - x);

        var supplyEffect = Math.Exp(-k * ps.Supply);

        // Optional step effect
        var stepEffect = 1.0;
        if (StepAmountDefault > 0)
        {
            var steps = Math.Floor(ps.Supply / StepAmountDefault);
            stepEffect = Math.Pow(1.0 - StepDropDefault, steps);
        }

        var baseEffect = Math.Max(supplyEffect, stepEffect);
        var clamped = Clamp(baseEffect, FloorDefault, CapDefault);
        return clamped;
    }

    /// <summary>
    /// Adjust an entity's base price by demand factor for the station that owns the grid.
    /// </summary>
    public double AdjustPriceForGrid(EntityUid grid, EntityUid entity, double basePrice)
    {
        var station = _stations.GetOwningStation(grid);
        if (station is not { } stationUid)
            return basePrice;

        if (!TryComp<MetaDataComponent>(entity, out var meta) || meta.EntityPrototype is null)
            return basePrice;

        var protoId = meta.EntityPrototype.ID;
        var mult = ComputeMultiplier(stationUid, protoId);
        return basePrice * mult;
    }
}