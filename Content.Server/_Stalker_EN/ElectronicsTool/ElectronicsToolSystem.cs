using Content.Server.ElectronicsTool.Components;
using Content.Server.Popups;
using Robust.Shared.Random;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Server.Audio;
using Content.Shared.Popups;
using Content.Shared.Interaction;
using Content.Server._Stalker_EN.ElectronicsSearchable;
using Content.Shared.TrashDetector;
using Robust.Shared.Timing; // ST:OW

namespace Content.Server._Stalker_EN.ElectronicsTool
{
    public sealed partial class TrashDetectorSystem : EntitySystem
    {
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
        [Dependency] internal readonly IEntityManager _entityManager = default!;
        [Dependency] internal readonly IMapManager _mapManager = default!;
        [Dependency] protected readonly AudioSystem Audio = default!;
        [Dependency] private readonly IGameTiming _timing = default!; // ST:OW

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ElectronicsToolComponent, BeforeRangedInteractEvent>(OnUseInHand);
            SubscribeLocalEvent<ElectronicsToolComponent, GetTrashDoAfterEvent>(OnDoAfter);
        }

        public void OnUseInHand(EntityUid uid, ElectronicsToolComponent comp, BeforeRangedInteractEvent args)
        {
            if (!args.CanReach)
                return;
            OnUse(uid, comp, args.Target, args.User);
        }

        public void OnUse(EntityUid? uid, ElectronicsToolComponent comp, EntityUid? target, EntityUid user)
        {
            if (target == null)
                return;
            if (TryComp<ElectronicsSearchableComponent>(target, out var electronics) && electronics != null)
            {
                if (_timing.CurTime >= electronics.NextSearchTime)
                {
                    var doAfterArgs = new DoAfterArgs(_entityManager, user, comp.SearchTime, new GetTrashDoAfterEvent(), uid, target: target, used: uid)
                    {
                        BreakOnDamage = true,
                        NeedHand = true,
                        DistanceThreshold = 2f,
                    };

                    _doAfterSystem.TryStartDoAfter(doAfterArgs);
                }
                else
                {
                    _popupSystem.PopupEntity("This was searched recently.", user, PopupType.LargeCaution);
                }
            }

        }

        public void OnDoAfter(EntityUid uid, ElectronicsToolComponent comp, GetTrashDoAfterEvent args)
        {

            if (args.Handled || args.Cancelled || args.Args.Target == null || !TryComp<ElectronicsSearchableComponent>(args.Args.Target.Value, out var trash))
                return;
            var target = args.Args.Target.Value;
            // ST:OW begin
            if (_timing.CurTime < trash.NextSearchTime)
            {
                args.Handled = true;
                _popupSystem.PopupEntity("This was searched recently.", args.Args.User, PopupType.LargeCaution);
                return;
            }
            args.Handled = true;
            trash.NextSearchTime = _timing.CurTime + trash.SearchCooldown;

            if (!_random.Prob(comp.Probability))
            {
                _popupSystem.PopupEntity("Nothing of value.", args.Args.User, PopupType.LargeCaution);
                args.Handled = true;
                return;
            }

            _popupSystem.PopupEntity("Something was found!", args.Args.User, PopupType.LargeCaution);

            var xform = Transform(args.Args.User);
            var coords = xform.Coordinates;

            var hardCap = Math.Max(1, comp.RollsHardCap);
            var minRolls = Math.Clamp(comp.RollsMin, 1, hardCap);
            var maxRolls = Math.Clamp(comp.RollsMax, minRolls, hardCap);

            var rolls = _random.Next(minRolls, maxRolls + 1);

            for (var i = 0; i < rolls; i++)
            {
                Spawn(comp.Loot, coords);
            }
            // ST:OW end
            args.Handled = true;
        }

    }
}
