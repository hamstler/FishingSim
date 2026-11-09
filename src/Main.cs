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

    private static readonly string[] SpeciesPaths =
    {
        "res://data/fish/ploc.tres", "res://data/fish/leszcz.tres", "res://data/fish/karp.tres", "res://data/fish/szczupak.tres",
    };

    private Profile _profile = null!;
    private Water _water = null!;
    private SkyController _sky = null!;
    private WorldState _world = null!;
    private LakeBed _lake = null!;
    private TackleLoadout _loadout = null!;
    private GearCatalog _gear = null!;

    public FishingRod Rod { get; private set; } = null!;
    public Player Player { get; private set; } = null!;
    public TournamentManager Tournaments { get; private set; } = null!;
    public Scenery Scenery { get; private set; } = null!;
    public InteractionManager Interactions { get; private set; } = null!;
    public FishPopulation Fish { get; private set; } = null!;
    public GearCatalog Gear => _gear;

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

        _sky = new SkyController { World = _world, Water = _water, Follow = Player.Camera, Bed = _lake };
        AddChild(_sky);

        var bobber = new Bobber { WaterY = WaterY, WaterHeight = _water.HeightAt };
        AddChild(bobber);

        _loadout = new TackleLoadout();
        _loadout.Attach(_profile);

        _gear = new GearCatalog();
        _gear.Attach(_profile);

        var species = LoadSpecies();
        Rod = new FishingRod();
        Player.AddChild(Rod);
        Rod.Setup(new RodDependencies(
            Player.Camera, Player.RodTip, Player.Rig, bobber, species, _loadout, _profile, _world, _lake, WaterY, _gear));
        Player.ActionSource = () => ActionFor(Rod.State);
        Player.Body.RaiseSource = () => Rod.Lifting && Rod.State is RodState.Nibble or RodState.Bite or RodState.Fight or RodState.Close or RodState.Waiting ? 1f : 0f;
        Player.Body.ReelingSource = () => Rod.Reeling && Rod.State is RodState.Waiting or RodState.Fight or RodState.Nibble or RodState.Bite;

        // Medieval pixel-art look: the world is drawn at low resolution and scaled up (HUD stays sharp).
        AddChild(new Graphics.PixelArtView(Player.Camera));

        var grass = new Graphics.GrassField();
        grass.Setup(_lake, Player);
        AddChild(grass);

        Fish = new FishPopulation();
        Fish.Setup(_lake, Player, species, WaterY);
        AddChild(Fish);
        _gear.GearChanged += ApplyGearEffects;
        ApplyGearEffects();
        _world.HoursAdvanced += _profile.PassTime;

        Tournaments = new TournamentManager();
        AddChild(Tournaments);
        Tournaments.Setup(Rod);

        Interactions = new InteractionManager();
        AddChild(Interactions);
        Interactions.Setup(Player, Scenery, Rod);

        var hud = new Hud();
        hud.Setup(Rod, Tournaments, Interactions);
        AddChild(hud);

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

        Interactions.Activated += spot => OnInteract(spot, shop, gameUi);

        // The mood of the map (water colour, mist) follows the location; a new map also means a new start on the pier.
        ApplyMood();
        _lake.Rebuilt += ApplyMood;
        _lake.Rebuilt += RespawnPlayer;
        _profile.Changed += Save;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private static BodyAction ActionFor(RodState state) => state switch
    {
        RodState.Charging or RodState.Flying => BodyAction.Cast,
        RodState.Fight or RodState.Close => BodyAction.Fight,
        RodState.Landed => BodyAction.Land,
        _ => BodyAction.None,
    };

    /// <summary>Accessories that change the world: polarized glasses show fish further away and deeper.</summary>
    private void ApplyGearEffects()
    {
        var glasses = _gear.Accessory(AccessoryKind.PolarizedGlasses);
        Fish.VisibleRange = FishPopulation.BaseVisibleRange + (glasses?.Power ?? 0f);
        Fish.VisibleDepth = FishPopulation.BaseVisibleDepth + (glasses != null ? 1.2f : 0f);
        _water.SetGlareCut(glasses != null ? 1f : 0f);
    }

    private void ApplyMood()
    {
        var loc = _lake.Location;
        _water.SetTint(loc.WaterShallowColor, loc.WaterDeepColor);
        _sky.FogBoost = loc.FogBoost;
    }

    private static void OnInteract(Interactable spot, Shop shop, GameUI gameUi)
    {
        switch (spot.Kind)
        {
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
        foreach (var path in SpeciesPaths)
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

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            Save();
    }

    public override void _ExitTree()
    {
        _profile.Changed -= Save;
        _world.HoursAdvanced -= _profile.PassTime;
        _gear.GearChanged -= ApplyGearEffects;
        _lake.Rebuilt -= RespawnPlayer;
        _lake.Rebuilt -= ApplyMood;
    }
}
