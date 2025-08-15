using Content.Server.GameTicking;
using Content.Server.Medical.SuitSensors;
using Content.Server.Access.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Roles;
using Robust.Shared.Timing;

namespace Content.Server._NF.Medical.SuitSensors;

/// <summary>
/// Sets default behavior for suit sensors:
/// - For any clothing with sensors spawned during the round, default to Coordinates mode.
/// - On roundstart starting gear equip, turn sensors ON for everyone except pirates, syndicates, and mercenaries.
/// </summary>
public sealed class AutoSuitSensorDefaultsSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SuitSensorSystem _suitSensors = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        // После экипировки стартового снаряжения включаем датчики для подходящих ролей.
        SubscribeLocalEvent<StartingGearEquippedEvent>(OnStartingGearEquipped);
    }

    private void OnStartingGearEquipped(ref StartingGearEquippedEvent args)
    {
        var wearer = args.Entity;

        // Делаем действие чуть позже, чтобы все job specials (включая компонент пиратов) успели примениться.
        Timer.Spawn(TimeSpan.FromMilliseconds(100), () =>
        {
            // Пираты: датчики должны быть выключены.
            if (HasComp<DisableSuitSensorsComponent>(wearer))
            {
                _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                return;
            }

            // Синдикаты (ядерные оперативники): датчики выключены.
            if (HasComp<Content.Shared.NukeOps.NukeOperativeComponent>(wearer))
            {
                _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                return;
            }

            // Синдикаты/пираты/мерки: проверяем иконку работы и теги доступа на ID.
            if (_idCard.TryFindIdCard(wearer, out var delayedId))
            {
                // Проверка иконок работ
                var jobIcon = delayedId.Comp.JobIcon;
                var jobIconStr = jobIcon.ToString();
                if (!string.IsNullOrEmpty(jobIconStr))
                {
                    // Mercenary
                    if (jobIconStr == "JobIconMercenary")
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                    // Syndicate (любые варианты, начинающиеся с JobIconSyndicate)
                    if (jobIconStr.StartsWith("JobIconSyndicate"))
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                    // Pirates (любой из JobIconNFPirate*)
                    if (jobIconStr.StartsWith("JobIconNFPirate"))
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                }

                // Проверка тегов доступа
                if (TryComp<AccessComponent>(delayedId.Owner, out var delayedAccess))
                {
                    // Mercenary
                    if (delayedAccess.Tags.Contains("Mercenary"))
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                    // Syndicate/NFSyndicate
                    if (delayedAccess.Tags.Contains("Syndicate") || delayedAccess.Tags.Contains("NFSyndicate"))
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                    // Pirates
                    if (delayedAccess.Tags.Contains("Pirate"))
                    {
                        _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorOff, SlotFlags.All);
                        return;
                    }
                }
            }

            // По умолчанию на раундстарте включаем датчики (Vitals) всем остальным.
            _suitSensors.SetAllSensors(wearer, SuitSensorMode.SensorVitals, SlotFlags.All);
        });
    }
}


