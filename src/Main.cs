using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using FishingSim.Accounts;
using FishingSim.Core.Animation;
using FishingSim.Data;
using FishingSim.Fishing;
using FishingSim.Progress;
using FishingSim.Tournaments;
using FishingSim.UI;
using FishingSim.World;

namespace FishingSim;

/// <summary>Wires the world together: terrain, water, scenery, sky and weather, player, tackle, HUD and menus.</summary>
public partial class Main : Node3D
{
    public const float WaterY = 0f;

    /// <summary>Lets tools and tests open any map regardless of the player's level.</summary>
    public static bool UnlockAllMaps { get; set; }


    private Profile _profile = null!;
    private Water _water = null!;

    private ulong _rowRefusedAt;

    /// <summary>The echo sounder screen (shown in the boat with the accessory).</summary>
    public UI.EchoSounder Echo { get; private set; } = null!;

    /// <summary>The angler's rowboat at the pier.</summary>
    public World.Rowboat Boat { get; private set; } = null!;
    private SkyController _sky = null!;
    private WorldState _world = null!;
    private float _appliedMurk;
    private LakeBed _lake = null!;
    private TackleLoadout _loadout = null!;
    private GearCatalog _gear = null!;

    private FishingRod _firstRod = null!;

    /// <summary>The rod in use (the rod set's active one; the first rod until the set is made).</summary>
    public FishingRod Rod => Rods?.Active ?? _firstRod;

    /// <summary>The angler's rods (hotbar slots 1-3; up to three fishing at once).</summary>
    public RodSet Rods { get; private set; } = null!;
    public Player Player { get; private set; } = null!;
    public TournamentManager Tournaments { get; private set; } = null!;
    public Scenery Scenery { get; private set; } = null!;
    public InteractionManager Interactions { get; private set; } = null!;
    public FishPopulation Fish { get; private set; } = null!;

    /// <summary>Rings of fish rising to the surface.</summary>
    public RiseRings Rises { get; private set; } = null!;
    public GearCatalog Gear => _gear;
    public HandTools Tools { get; private set; } = null!;
    public CookingPanel Cooking { get; private set; } = null!;
    public SmokehousePanel Smokehouse { get; private set; } = null!;
    public NoticeBoardPanel Board { get; private set; } = null!;
    private readonly Core.IRng _rng = new Core.SystemRng();
    public InsectSwarms Swarms { get; private set; } = null!;
    public DigMarks DigMarks { get; private set; } = null!;

    public override void _Ready()
    {
        GD.Randomize();

        string user = Session.IsLoggedIn ? Session.Username : "admin";
        _profile = Profile.LoadFor(user);
        if (Session.MapIndex >= 0)
            _profile.LocationIdx = Session.MapIndex;

        _world = new WorldState { Hour = _profile.Hour };
        AddChild(_world);

        _lake = new LakeBed();
        foreach (var path in LakeBed.LocationPaths)
            _lake.Locations.Add(GD.Load<LocationData>(path));
        AddChild(_lake);
        int wanted = System.Math.Clamp(_profile.LocationIdx, 0, _lake.Locations.Count - 1);
        if (UnlockAllMaps || _lake.Locations[wanted].RequiredLevel <= _profile.Level)
            _lake.SelectInitial(wanted);
        _lake.Rebuild();

        _water = new Water();
        AddChild(_water);
        _water.SetBed(_lake);

        Scenery = new Scenery();
        AddChild(Scenery);
        Scenery.Build(_lake, _world);

        Player = new Player { Position = Player.SpawnPoint, Bed = _lake };
        AddChild(Player);

        // The rowboat at the pier (E boards it): its E spot joins the scenery's after every rebuild.
        Boat = new World.Rowboat { Name = "Rowboat" };
        AddChild(Boat);
        Boat.Setup(_lake, _water);
        Scenery.Interactables.Add(Boat.Spot);
        // The same wind as the rod's (FishingRod.WindVelocity): it drifts an unanchored boat.
        Boat.Wind = () => new Vector2(0.8f, 0.6f) * (_world.WindTarget * 3f);
        Boat.Said += msg => Rod.Notify(msg, 2f);
        _lake.Rebuilt += () =>
        {
            Boat.ResetToMooring();
            if (!Scenery.Interactables.Contains(Boat.Spot))
                Scenery.Interactables.Add(Boat.Spot);
        };

        _sky = new SkyController { World = _world, Water = _water, Follow = Player.Camera, Bed = _lake };
        AddChild(_sky);

        var bobber = new Bobber { WaterY = WaterY, WaterHeight = _water.HeightAt };
        AddChild(bobber);

        _loadout = new TackleLoadout();
        _loadout.Attach(_profile);

        _gear = new GearCatalog();
        _gear.Attach(_profile);

        var species = LoadSpecies();
        _firstRod = new FishingRod();
        Player.AddChild(Rod);
        Rod.Setup(new RodDependencies(
            Player.Camera, Player.RodTip, Player.Rig, bobber, species, _loadout, _profile, _world, _lake, WaterY, _gear));
        Player.ActionSource = () => ActionFor(Rod.State);
        Player.Body.RaiseSource = () => Rod.Lifting && Rod.State is RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close or RodState.Waiting ? 1f : 0f;
        Player.Body.ReelingSource = () => Rod.Reeling && Rod.State is RodState.Waiting or RodState.Fight or RodState.Nibble or RodState.Bite;
        Player.Body.RigOnRest = () => Rod.OnRest;

        // Camp life: the shovel and the insect net (Q), the holes they leave, the mayflies over the shore at dusk.
        Swarms = new InsectSwarms();
        AddChild(Swarms);
        Swarms.Setup(_lake, _world);
        var marks = new DigMarks();
        AddChild(marks);
        marks.Setup(_lake);
        DigMarks = marks;
        Tools = new HandTools();
        Player.AddChild(Tools);
        Tools.Setup(Player, Rod, _gear, _profile, _lake, _world, Swarms, marks);
        // Rowing takes both hands: an idle rod goes into the backpack; one fishing keeps the boat where it is.
        Boat.HandsFree = () =>
        {
            if (Tools.Current == HandTool.None)
                return true;
            if (Tools.Current == HandTool.Rod && Rod.State == RodState.Idle && Tools.Select(HandTool.None))
                return true;
            if (_rowRefusedAt + 3000 < Time.GetTicksMsec())
            {
                Rod.Notify(Rod.State == RodState.Idle ? "Odłóż narzędzie (Q), żeby wiosłować" : "Zwiń zestaw, żeby wiosłować", 2.5f);
                _rowRefusedAt = Time.GetTicksMsec();
            }
            return false;
        };
        Player.Body.ToolPose = Tools.HandPose;
        Player.Body.ToolBodyFrame = Tools.BodyFrame;

        var grass = new Graphics.GrassField();
        grass.Setup(_lake, Player);
        AddChild(grass);

        Fish = new FishPopulation();
        Fish.Setup(_lake, Player, species, WaterY);
        AddChild(Fish);
        Rod.Population = Fish;
        Rises = new RiseRings();
        Rises.Setup(Fish, _world, Player, WaterY);
        AddChild(Rises);
        Rises.Risen += Rod.NoteRise;
        Rod.WeedBeds = Scenery.WeedBeds;
        Rod.SunkenWood = Scenery.SunkenWood;
        Rod.OtherAnglers = Scenery.Anglers;
        // Rain muddies the water: refresh the water and the fish's visibility when the murk has changed noticeably.
        _world.HoursAdvanced += _ =>
        {
            if (MathF.Abs(_world.Murk - _appliedMurk) > 0.05f)
                ApplyGearEffects();
        };
        // The other anglers catch a fish now and then: the water in front of them gets fished.
        var anglerLuck = new Random(77);
        _world.HoursAdvanced += hours =>
        {
            for (int i = 0; i < Scenery.Anglers.Count; i++)
                if (anglerLuck.NextDouble() < Core.Methods.OtherAnglers.CatchesPerHour * hours)
                {
                    var at = Scenery.Anglers[i].Float;
                    Rod.Swim.Add(at.X, at.Z, _profile.Clock);
                    Scenery.AnglerCatches(i);
                }
        };
        AddChild(new Graphics.Gulls { Daylight = () => _world.Daylight });

        var sound = new Audio.SoundEngine();
        sound.Setup(Rod, Player, _world, _lake);
        AddChild(sound);
        _gear.GearChanged += ApplyGearEffects;
        ApplyGearEffects();
        _profile.WaterTempNow = () => Rod.WaterTemp;
        _world.HoursAdvanced += _profile.PassTime;

        Tournaments = new TournamentManager();
        AddChild(Tournaments);
        Tournaments.Setup(Rod);

        Interactions = new InteractionManager();
        AddChild(Interactions);
        Interactions.Setup(Player, Scenery, Rod);

        var hud = new Hud { Tools = Tools };
        hud.Setup(Rod, Tournaments, Interactions, Player, Scenery);
        AddChild(hud);
        var echo = new UI.EchoSounder { Boat = Boat, Lake = _lake, Fish = Fish, Gear = _gear, Name = "EchoSounder" };
        echo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterLeft);
        echo.OffsetLeft = 24;
        echo.OffsetRight = 324;
        echo.OffsetTop = -95;
        echo.OffsetBottom = 95;
        hud.AddChild(echo);
        Echo = echo;

        var shop = new Shop();
        shop.Setup(Rod);
        AddChild(shop);

        var gameUi = new GameUI();
        gameUi.Setup(Rod, Player, Tournaments, Scenery);
        AddChild(gameUi);

        var results = new TournamentResultsScreen();
        results.Setup(Rod, Tournaments);
        AddChild(results);

        var pause = new PauseMenu();
        pause.Setup(Rod, Tournaments, Save, shop, gameUi, results);
        AddChild(pause);

        Cooking = new CookingPanel();
        Cooking.Setup(Rod, _gear, species);
        AddChild(Cooking);
        _world.HoursAdvanced += hours =>
        {
            foreach (var fire in World.Props.Campfire.All)
                fire.PassTime(hours);
        };

        Smokehouse = new SmokehousePanel();
        Smokehouse.Setup(Rod, species);
        AddChild(Smokehouse);
        Board = new NoticeBoardPanel { Notices = () => _profile.NoticesFor(_lake.LocationIndex, RequestFishHere(species), Scenery.HasSmokehouse) };
        Board.Setup(Rod, species);
        AddChild(Board);
        // Picked mushrooms and berries grow back a day later.
        _world.HoursAdvanced += _ => Scenery.RegrowForage(_profile.Clock);
        // The moon's phase follows the profile's clock.
        _world.MoonPhase = Core.Methods.Moon.Phase(_profile.Clock);
        _world.HoursAdvanced += _ => _world.MoonPhase = Core.Methods.Moon.Phase(_profile.Clock);

        Interactions.Activated += spot => OnInteract(spot, shop, gameUi, Cooking);

        // The mood of the map (water colour, mist) follows the location; a new map also means a new start on the pier.
        ApplyMood();
        _lake.Rebuilt += ApplyMood;
        _lake.Rebuilt += _firstRod.Groundbait.Clear;
        _lake.Rebuilt += _firstRod.Disturbance.Clear;
        _lake.Rebuilt += _firstRod.Swim.Clear;
        _lake.Rebuilt += _firstRod.Shoal.Clear;
        _lake.Rebuilt += RespawnPlayer;

        // The rod set: rods 2 and 3 are made when first taken out (keys 2 and 3); they share the swim's state with the
        // first (groundbait, disturbance, fished-out spots, shoals) and have their own rig, float and tackle.
        Rods = new RodSet(_firstRod, Player, slot =>
        {
            var bob = new Bobber { WaterY = WaterY, WaterHeight = _water.HeightAt };
            AddChild(bob);
            var rig = new RodRig { Visible = false };
            Player.Camera.AddChild(rig);
            rig.Transform = _firstRod.Rig.Transform;
            var loadout = new TackleLoadout();
            loadout.Attach(_profile, slot);
            var extra = new FishingRod { Active = false };
            Player.AddChild(extra);
            extra.Setup(new RodDependencies(Player.Camera, rig.Tip, rig, bob, species, loadout, _profile, _world, _lake, WaterY, _gear));
            extra.InHands = false;
            extra.Population = Fish;
            extra.WeedBeds = Scenery.WeedBeds;
            extra.SunkenWood = Scenery.SunkenWood;
            extra.OtherAnglers = Scenery.Anglers;
            extra.Swim = _firstRod.Swim;
            extra.Shoal = _firstRod.Shoal;
            extra.Disturbance = _firstRod.Disturbance;
            extra.Groundbait = _firstRod.Groundbait;
            return extra;
        });
        Rods.Made += extra =>
        {
            sound.Track(extra);
            Tournaments.Track(extra);
            Rises.Risen += extra.NoteRise;
        };
        Tools.Rods = Rods;
        _lake.Rebuilt += Rods.ResetAll;
        _lake.Rebuilt += ReleaseLoadingGarbage;
        _profile.Changed += Save;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ReleaseLoadingGarbage();
    }

    /// <summary>
    /// Building a map leaves hundreds of megabytes of garbage (texture files read into arrays, painted pictures, mesh
    /// data). Collected here, once, while the map loads (compacting the large-object heap so the memory goes back to
    /// the system), instead of in one long pause in the middle of play.
    /// </summary>
    private void ReleaseLoadingGarbage()
    {
        Callable.From(() =>
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }).CallDeferred();
    }

    private static BodyAction ActionFor(RodState state) => state switch
    {
        RodState.Charging or RodState.Flying => BodyAction.Cast,
        RodState.Fight or RodState.Close => BodyAction.Fight,
        RodState.Landed => BodyAction.Land,
        _ => BodyAction.None,
    };

    private OmniLight3D? _lantern;
    private float _restTimer;

    /// <summary>The bought lantern hangs at the angler's belt and lights up after dusk.</summary>
    public OmniLight3D? Lantern => _lantern;

    /// <summary>Accessories that change the world: the brimmed hat shows fish further and deeper, the lantern gives light.</summary>
    private void ApplyGearEffects()
    {
        bool hasLantern = _gear.Has(AccessoryKind.Headlamp);
        if (hasLantern && _lantern == null)
        {
            _lantern = new OmniLight3D
            {
                LightColor = new Color(1f, 0.72f, 0.4f),
                OmniRange = 9f,
                OmniAttenuation = 1.4f,
                ShadowEnabled = true,
                Position = new Vector3(-0.35f, 1.05f, -0.25f),
            };
            Player.AddChild(_lantern);
        }
        else if (!hasLantern && _lantern != null)
        {
            _lantern.QueueFree();
            _lantern = null;
        }
        _lake.WadeLimit = _gear.Accessory(AccessoryKind.Waders)?.Power ?? LakeBed.WadeDepth;
        foreach (var fire in Scenery.Campfires)
            fire.ShowPot(_gear.Has(AccessoryKind.CookingPot));
        if (Rod.State == RodState.Idle)
            Rod.ApplyTackle();     // reel grease and the like change the tackle's numbers
        var glasses = _gear.Accessory(AccessoryKind.PolarizedGlasses);
        float clarity = Rod.ClarityNow;
        _appliedMurk = _world.Murk;
        Fish.VisibleRange = FishPopulation.BaseVisibleRange + (glasses?.Power ?? 0f);
        Fish.VisibleDepth = clarity * 0.6f + (glasses != null ? 1.2f : 0f);
        Fish.Clarity = clarity;
        Fish.GlareCut = glasses != null ? 1f : 0f;
        _water.SetClarity(clarity);
        _water.SetGlareCut(glasses != null ? 1f : 0f);
    }

    private void ApplyMood()
    {
        var loc = _lake.Location;
        _water.SetTint(loc.WaterShallowColor, loc.WaterDeepColor);
        _sky.FogBoost = loc.FogBoost;
        if (Fish != null)
            ApplyGearEffects();
    }

    /// <summary>The species the village notices may ask for on this map: how often each is caught here.</summary>
    private List<Core.Camp.RequestFish> RequestFishHere(IReadOnlyList<FishSpecies> species) =>
        species.Where(sp => _lake.AbundanceOf(sp.SpeciesName) > 0f)
            .Select(sp => new Core.Camp.RequestFish(sp.SpeciesName, sp.LegalWeight, sp.PricePerKg, sp.Rarity * _lake.AbundanceOf(sp.SpeciesName)))
            .ToList();

    /// <summary>Picks a mushroom or blueberry patch (E): the pickings go to the pantry.</summary>
    public Core.Camp.Haul? Pick(World.Props.ForagePatch patch)
    {
        float now = _profile.Clock;
        if (!patch.Ripe)
        {
            Rod.Notify($"Już zebrane: odrośnie za {patch.HoursLeft(now):0} h gry.", 2f);
            return null;
        }
        var haul = Core.Camp.Foraging.Pick(patch.Kind, _world.Weather == Core.Weather.Rain, _rng);
        patch.Pick(now);
        _profile.AddPantry(haul.Baits);
        Rod.Notify(haul.Message + " (do spiżarni)", 2.5f);
        Rod.Cue(Core.Audio.OneShot.Click, 0.7f);
        return haul;
    }

    private void OnInteract(Interactable spot, Shop shop, GameUI gameUi, CookingPanel cooking)
    {
        switch (spot.Kind)
        {
            case InteractKind.Forage when spot.Target is World.Props.ForagePatch patch: Pick(patch); break;
            case InteractKind.Boat when spot.Target is World.Rowboat boat:
                if (boat.Toggle(Player) is string why)
                    Rod.Notify(why, 2.5f);
                break;
            case InteractKind.Smokehouse: Smokehouse.Open(); break;
            case InteractKind.NoticeBoard: Board.Open(); break;
            case InteractKind.Campfire when spot.Target is World.Props.Campfire fire: cooking.Open(fire); break;
            case InteractKind.Tackle: shop.OpenFor(ShopKind.Tackle); break;
            case InteractKind.Bait: shop.OpenFor(ShopKind.Bait); break;
            case InteractKind.FishBuyer: shop.OpenFor(ShopKind.FishBuyer); break;
            case InteractKind.Market: shop.OpenFor(ShopKind.Market); break;
            case InteractKind.General: shop.OpenFor(ShopKind.General); break;
            case InteractKind.BusStop: shop.OpenFor(ShopKind.Travel); break;
            case InteractKind.Office: gameUi.Open(3); break;
        }
    }

    private void RespawnPlayer()
    {
        Player.Respawn();
        Rod.ResetToIdle();
    }

    private static List<FishSpecies> LoadSpecies()
    {
        var list = new List<FishSpecies>();
        foreach (var path in SpeciesCatalog.Paths)
        {
            var species = GD.Load<FishSpecies>(path);
            if (species != null)
                list.Add(species);
        }
        return list;
    }

    private void Save()
    {
        _profile.Hour = _world.Hour;
        _profile.LocationIdx = _lake.LocationIndex;
        _loadout.SyncToProfile();
        _profile.Autosave();
    }

    /// <summary>True while the angler rests on the stool: waiting for a bite, standing still (time runs 4x).</summary>
    public bool Resting { get; private set; }

    public override void _Process(double delta)
    {
        using var perfScope = Graphics.Perf.Scope("main");
        bool still = new Vector2(Player.Velocity.X, Player.Velocity.Z).Length() < 0.1f;
        bool canRest = Rod.State == RodState.Waiting && still && _gear.Has(AccessoryKind.Chair) && Tournaments.Active == null;
        _restTimer = canRest ? _restTimer + (float)delta : 0f;
        Resting = _restTimer > 3f;
        if (!_world.Paused)
            _world.TimeScale = Resting ? 4f : 1f;
        if (_lantern == null)
            return;
        // Lit only when it is dark; the tallow flame flickers.
        float dark = Mathf.Clamp(1f - _world.Daylight * 2.5f, 0f, 1f);
        float t = Time.GetTicksMsec() / 1000f;
        _lantern.LightEnergy = dark * 1.6f * (0.88f + 0.08f * Mathf.Sin(t * 11f) + 0.04f * Mathf.Sin(t * 27f));
        _lantern.Visible = dark > 0.01f;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Save();
    }

    public override void _ExitTree()
    {
        _profile.Changed -= Save;
        _world.HoursAdvanced -= _profile.PassTime;
        _profile.WaterTempNow = null;
        _gear.GearChanged -= ApplyGearEffects;
        _lake.Rebuilt -= RespawnPlayer;
        _lake.Rebuilt -= _firstRod.Groundbait.Clear;
        _lake.Rebuilt -= _firstRod.Disturbance.Clear;
        _lake.Rebuilt -= _firstRod.Swim.Clear;
        _lake.Rebuilt -= _firstRod.Shoal.Clear;
        _lake.Rebuilt -= ReleaseLoadingGarbage;
        _lake.Rebuilt -= ApplyMood;
    }
}
