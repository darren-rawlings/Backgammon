using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backgammon.Core;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using static Backgammon.Game.BoardLayout;

namespace Backgammon.Game;

/// <summary>
/// Backgammon. Roll, tap one of your checkers (the points you can move from glow), then a glowing
/// point to move it there. Hitting a lone checker knocks it flying onto the bar in a burst of sparks
/// (the signature "hit"); bearing off stands your checkers on edge in the tray. White (you, against
/// the computer) moves up the left column and down the right, home in the near-right corner. Drag
/// sideways to spin the board.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Backgammon";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	private const bool HumanIsWhite = true;

	/// <summary>Location keys for checker stacks: 1-24 points (White numbering), bars and trays.</summary>
	private const int WhiteBar = 100, BlackBar = 101, WhiteOff = 200, BlackOff = 201;

	private readonly BackgammonGame _game = new();
	private readonly Dictionary<int, List<Node3D>> _stacks = new();
	private readonly Dictionary<int, MeshInstance3D> _glows = new();
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly ComputerTurn _computer = new();
	private readonly System.Random _dice = new();
	private readonly Die[] _dieNodes = new Die[4];

	private GameSettings _settings = null!;
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private MeshInstance3D _frame = null!;
	private readonly List<MeshInstance3D> _woodParts = new();
	private Button _roll = null!;
	private StandardMaterial3D _glowMaterial = null!;
	private StandardMaterial3D _sourceMaterial = null!;
	private int _selected = -1;
	private bool _busy;
	private bool _rolled;
	private bool _counted;
	private int _scoreWhite;
	private int _scoreBlack;
	private List<Step>? _pendingTurn;

	private bool _demo;
	private bool _quitAfterDemo;
	private float _initialSpin;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.WhiteToMove != HumanIsWhite));

	private PieceSet Colours => Styles.Pieces(_settings.Pieces);

	public override void _Ready()
	{
		DevLog.Tag = "bg";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Marble);
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);

		_camera = new Camera3D { Position = new Vector3(0, 12.6f, 5.2f), Fov = 50, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.4f, 0.75f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));
		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);
		BuildBoard();
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch to undo your turn",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Roll, tap a checker, then where to go",
			PiecesLabel: "Checkers", PieceName: s => $"{Styles.Pieces(s).NameOne} & {Styles.Pieces(s).NameTwo}"),
			_settings.Muted, _settings.Mode, _settings.Difficulty, _settings.Board, _settings.Pieces);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.MuteToggled += muted =>
		{
			_settings.Muted = muted;
			AudioServer.SetBusMute(0, muted);
			_settings.Save();
		};
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
			_scoreWhite = _scoreBlack = 0;
			StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			ApplyBoardStyle();
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			RebuildCheckers();
		};

		var layer = new CanvasLayer();
		AddChild(layer);
		_roll = new Button { Text = "Roll", FocusMode = Control.FocusModeEnum.None, Visible = false };
		_roll.AddThemeFontSizeOverride("font_size", 32);
		foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
		{
			var box = new StyleBoxFlat { BgColor = state.StartsWith("pressed") ? theme.Accent.Darkened(0.2f) : theme.Accent, AntiAliasing = true };
			box.SetCornerRadiusAll(32);
			box.SetContentMarginAll(12);
			_roll.AddThemeStyleboxOverride(state, box);
		}
		_roll.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_roll.OffsetLeft = -130;
		_roll.OffsetRight = 130;
		_roll.OffsetTop = -192;
		_roll.OffsetBottom = -128;
		_roll.Pressed += () => _ = Roll();
		layer.AddChild(_roll);

		_spin = new SpinControl(_rig, () => !_busy);
		_spin.Tapped += OnTapped;
		StartGame();
	}

	// ---------------------------------------------------------------- Board

	private void BuildBoard()
	{
		_frame = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(HalfX * 2 + 0.4f, Thickness, HalfZ * 2 + 0.4f) }, Position = new Vector3(0, -Thickness / 2, 0) };
		_rig.AddChild(_frame);
		// The bar and the trays: raised strips of the board material.
		var bar = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(HalfX * 2 + 0.4f, 0.12f, BarHalf * 2 - 0.1f) }, Position = new Vector3(0, 0.06f - 0.06f, 0) };
		var tray = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(HalfX * 2 + 0.4f, Thickness, 1.4f) }, Position = new Vector3(0, -Thickness / 2, TrayZ) };
		_rig.AddChild(bar);
		_rig.AddChild(tray);
		_woodParts.AddRange(new[] { _frame, bar, tray });

		// The 24 points: long triangles from each rim toward the middle, alternating colours.
		var dark = new StandardMaterial3D { AlbedoColor = new Color("5a2a1a"), Roughness = 0.6f };
		var light = new StandardMaterial3D { AlbedoColor = new Color("e8d5b0"), Roughness = 0.6f };
		for (int point = 1; point <= 24; point++)
		{
			var (z, side) = Point(point);
			var mesh = new ArrayMesh();
			float rim = side * (HalfX - 0.25f), tip = side * (HalfX - 0.25f - PointLength);
			float w = Pitch * 0.46f;
			var verts = new[] { new Vector3(rim, 0.005f, z - w), new Vector3(rim, 0.005f, z + w), new Vector3(tip, 0.005f, z) };
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			// Wind the triangle so it faces up whichever side it's on.
			arrays[(int)Mesh.ArrayType.Vertex] = side > 0 ? new[] { verts[0], verts[1], verts[2] } : new[] { verts[1], verts[0], verts[2] };
			arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Up, Vector3.Up, Vector3.Up };
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			_rig.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = point % 2 == 0 ? dark : light, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		}

		_glowMaterial = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		_sourceMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 1f, 0.7f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		for (int point = 1; point <= 24; point++)
		{
			var (z, side) = Point(point);
			AddGlow(point, new Vector2(PointLength, Pitch * 0.9f), new Vector3(side * (HalfX - 0.25f - PointLength / 2), 0.012f, z));
		}
		AddGlow(WhiteBar, new Vector2(HalfX - 0.6f, BarHalf * 1.6f), new Vector3((HalfX + 0.6f) / 2, 0.08f, 0));
		AddGlow(BlackBar, new Vector2(HalfX - 0.6f, BarHalf * 1.6f), new Vector3(-(HalfX + 0.6f) / 2, 0.08f, 0));
		AddGlow(WhiteOff, new Vector2(HalfX, 1.2f), new Vector3(HalfX / 2 + 0.2f, 0.01f, TrayZ));
		AddGlow(BlackOff, new Vector2(HalfX, 1.2f), new Vector3(-HalfX / 2 - 0.2f, 0.01f, TrayZ));
		ApplyBoardStyle();

		for (int i = 0; i < _dieNodes.Length; i++)
		{
			_dieNodes[i] = new Die { Visible = false };
			_rig.AddChild(_dieNodes[i]);
		}
	}

	private void AddGlow(int key, Vector2 size, Vector3 at)
	{
		var glow = new MeshInstance3D { Mesh = new PlaneMesh { Size = size }, Position = at, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_rig.AddChild(glow);
		_glows[key] = glow;
	}

	private void ApplyBoardStyle()
	{
		var material = Materials.For(Styles.BoardLook(_settings.Board));
		foreach (var part in _woodParts)
			part.MaterialOverride = material;
	}

	private Look LookFor(bool white) => Colours.LookOf(!white);

	private Node3D MakeChecker(bool white)
	{
		var node = new Node3D();
		node.SetMeta("white", white);
		var material = Materials.For(LookFor(white), isDisc: true, variant: (int)(GD.Randi() % 6));
		node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = CheckerRadius, BottomRadius = CheckerRadius * 0.96f, Height = CheckerHeight, RadialSegments = 32 }, MaterialOverride = material });
		node.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = CheckerRadius * 0.55f, OuterRadius = CheckerRadius * 0.68f, Rings = 24, RingSegments = 6 }, MaterialOverride = material, Position = new Vector3(0, CheckerHeight / 2, 0), Scale = new Vector3(1, 0.3f, 1) });
		_rig.AddChild(node);
		return node;
	}

	private static Vector3 SlotFor(int key, int index, int count) => key switch
	{
		WhiteBar => BarSlot(true, index),
		BlackBar => BarSlot(false, index),
		WhiteOff => OffSlot(true, index),
		BlackOff => OffSlot(false, index),
		_ => Slot(key, index, count),
	};

	private List<Node3D> Stack(int key) => _stacks.TryGetValue(key, out var list) ? list : _stacks[key] = new List<Node3D>();

	/// <summary>Moves every checker on a location to its slot (stacks squeeze up as they grow).</summary>
	private void Restack(int key, float time = 0.15f)
	{
		var stack = Stack(key);
		for (int i = 0; i < stack.Count; i++)
		{
			var target = SlotFor(key, i, stack.Count);
			var basis = key is WhiteOff or BlackOff ? new Basis(Vector3.Forward, Mathf.Pi / 2) : Basis.Identity;
			if (time <= 0)
			{
				stack[i].Position = target;
				stack[i].Basis = basis;
			}
			else
			{
				var tween = CreateTween().SetParallel();
				tween.TweenProperty(stack[i], "position", target, time);
				tween.TweenProperty(stack[i], "basis", basis, time);
			}
		}
	}

	/// <summary>Clears the board and lays out the checkers exactly as the rules have them.</summary>
	private void RebuildCheckers()
	{
		foreach (var stack in _stacks.Values)
		{
			foreach (var node in stack)
				node.QueueFree();
		}
		_stacks.Clear();
		for (int point = 1; point <= 24; point++)
		{
			int count = _game.PointCount(point);
			for (int i = 0; i < System.Math.Abs(count); i++)
				Stack(point).Add(MakeChecker(count > 0));
			Restack(point, 0);
		}
		foreach (var (key, white) in new[] { (WhiteBar, true), (BlackBar, false) })
		{
			for (int i = 0; i < _game.BarCount(white); i++)
				Stack(key).Add(MakeChecker(white));
			Restack(key, 0);
		}
		foreach (var (key, white) in new[] { (WhiteOff, true), (BlackOff, false) })
		{
			for (int i = 0; i < _game.OffCount(white); i++)
				Stack(key).Add(MakeChecker(white));
			Restack(key, 0);
		}
	}

	// ---------------------------------------------------------------- Dice

	private async Task Roll()
	{
		if (_busy || _rolled || _game.IsOver)
			return;
		_busy = true;
		_roll.Visible = false;
		_hud.HideHint();
		int a = _dice.Next(1, 7), b = _dice.Next(1, 7);
		_game.Roll(a, b);
		_rolled = true;
		DevLog.Write($"{(_game.WhiteToMove ? "White" : "Black")} rolls {a}-{b}");
		await ThrowDice(a, b);
		_busy = false;
		await AfterStep();
	}

	/// <summary>Both dice tumble from the mover's end of the board into the gutter.</summary>
	private async Task ThrowDice(int a, int b)
	{
		float direction = _game.WhiteToMove ? 1 : -1;
		var from = new Vector3(0, 1.2f, direction * (HalfZ + 1.5f));
		Tween? last = null;
		for (int i = 0; i < 2; i++)
		{
			var die = _dieNodes[i];
			die.Visible = true;
			var to = new Vector3((i - 0.5f) * 0.9f, 0, direction * 2.4f + (float)GD.RandRange(-0.3, 0.3));
			last = die.Throw(from + new Vector3((i - 0.5f) * 0.6f, 0, 0), to, i == 0 ? a : b, i * 0.08f);
			int index = i;
			last.TweenCallback(Callable.From(() => _sound.PlayImpact(_impacts.Random(Style.Plastic), _dieNodes[index].GlobalPosition, 0.9f, 2f)));
		}
		GetTree().CreateTimer(0.6).Timeout += () => _sound.PlayImpact(_impacts.Random(Style.Plastic), _dieNodes[0].GlobalPosition, 0.6f);
		if (last != null)
			await ToSignal(last, Tween.SignalName.Finished);
		await Wait(0.2f);
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	private void OnTapped(Vector2 screen)
	{
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		if (!_rolled)
		{
			_ = Roll();
			return;
		}
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		var local = _rig.ToLocal(from + direction * ((CheckerHeight - from.Y) / direction.Y));
		int hit = Hit(local, out bool right);
		if (hit == -1)
			return;
		var steps = _game.PlayableSteps();
		// Translate the tap into the mover's numbering: their bar is 25, borne off is 0.
		int moverPoint = hit switch
		{
			0 => BackgammonGame.Off,
			25 or -25 => BackgammonGame.Bar,
			_ => _game.WhiteToMove ? hit : 25 - hit,
		};
		if (_selected != -1)
		{
			var step = steps.Where(s => s.From == _selected && s.To == moverPoint).OrderBy(s => s.Die).FirstOrDefault();
			if (step != default)
			{
				_ = PlayStep(step);
				return;
			}
		}
		_selected = steps.Any(s => s.From == moverPoint) ? moverPoint : -1;
		ShowGlows();
	}

	private int KeyFor(int moverPoint, bool white) => moverPoint switch
	{
		BackgammonGame.Bar => white ? WhiteBar : BlackBar,
		BackgammonGame.Off => white ? WhiteOff : BlackOff,
		_ => white ? moverPoint : 25 - moverPoint,
	};

	// ---------------------------------------------------------------- Moves

	private async Task PlayStep(Step step)
	{
		_busy = true;
		_spin.Stop();
		bool white = _game.WhiteToMove;
		_selected = -1;
		ShowGlows();
		_game.Play(step);
		DevLog.Write($"{(white ? "White" : "Black")} {step.From}->{step.To} (die {step.Die}){(step.Hit ? " hit" : "")}");

		int fromKey = KeyFor(step.From, white), toKey = KeyFor(step.To, white);
		var checker = Stack(fromKey)[^1];
		Stack(fromKey).RemoveAt(Stack(fromKey).Count - 1);

		if (step.Hit)
		{
			var victim = Stack(toKey)[0];
			Stack(toKey).Clear();
			KnockToBar(victim, !white);
		}
		Stack(toKey).Add(checker);
		var target = SlotFor(toKey, Stack(toKey).Count - 1, Stack(toKey).Count);
		var start = checker.Position;
		var hop = CreateTween();
		hop.TweenMethod(Callable.From<float>(t =>
		{
			checker.Position = start.Lerp(target, t) + new Vector3(0, 1.1f * 4 * t * (1 - t), 0);
		}), 0f, 1f, 0.38f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		if (toKey is WhiteOff or BlackOff)
			hop.Parallel().TweenProperty(checker, "basis", new Basis(Vector3.Forward, Mathf.Pi / 2), 0.38f);
		await ToSignal(hop, Tween.SignalName.Finished);
		Restack(fromKey);
		Restack(toKey);
		Clack(checker.GlobalPosition, 0.8f);
		if (toKey is WhiteOff or BlackOff)
			Sparkle.Burst(_rig, checker.Position + new Vector3(0, 0.3f, 0));
		_busy = false;
		await AfterStep();
	}

	/// <summary>
	/// The signature: a hit checker is knocked flying in a high arc onto the bar, spinning, with a
	/// burst of sparks where it was hit and a hard clack as it lands.
	/// </summary>
	private void KnockToBar(Node3D victim, bool victimWhite)
	{
		int barKey = victimWhite ? WhiteBar : BlackBar;
		Stack(barKey).Add(victim);
		var start = victim.Position;
		var target = SlotFor(barKey, Stack(barKey).Count - 1, Stack(barKey).Count);
		Sparkle.Burst(_rig, start + new Vector3(0, 0.25f, 0));
		_sound.PlayImpact(_impacts.Random(_settings.Pieces), victim.GlobalPosition, 1f, Styles.GainDb(_settings.Pieces) + 4f);
		foreach (var mesh in victim.GetChildren().OfType<MeshInstance3D>())
			mesh.MaterialOverlay = Sparkle.Halo;
		var axis = new Vector3(1, 0, 0.3f).Normalized();
		var flight = CreateTween();
		flight.TweenInterval(0.15f);
		flight.TweenMethod(Callable.From<float>(t =>
		{
			victim.Position = start.Lerp(target, t) + new Vector3(0, 2.4f * 4 * t * (1 - t), 0);
			victim.Basis = new Basis(axis, t * Mathf.Tau * 2);
		}), 0f, 1f, 0.6f);
		flight.TweenCallback(Callable.From(() =>
		{
			victim.Basis = Basis.Identity;
			foreach (var mesh in victim.GetChildren().OfType<MeshInstance3D>())
				mesh.MaterialOverlay = null;
			Clack(victim.GlobalPosition, 1f);
			Restack(barKey);
		}));
		_hud.SetStatus("Hit!", Colours.UiColorOf(victimWhite));
	}

	/// <summary>After every roll or step: end the turn if it's done, otherwise wait for the next step.</summary>
	private async Task AfterStep()
	{
		if (_game.IsOver)
		{
			await GameOver();
			return;
		}
		if (_game.TurnDone)
		{
			if (_game.Dice.Count > 0)
			{
				_hud.SetStatus(NameOf(_game.WhiteToMove) == "You" ? "No moves you can play" : $"{NameOf(_game.WhiteToMove)} can't move", Colors.White);
				await Wait(1.2f);
			}
			else
			{
				await Wait(0.3f);
			}
			_game.EndTurn();
			_rolled = false;
			foreach (var die in _dieNodes)
				die.Visible = false;
		}
		RefreshUi();
	}

	private async Task GameOver()
	{
		if (!_counted)
		{
			_counted = true;
			if (_game.WhiteWon == true)
				_scoreWhite += _game.Points;
			else
				_scoreBlack += _game.Points;
		}
		await Wait(0.3f);
		RefreshUi();
	}

	private void Clack(Vector3 at, float strength)
	{
		_sound.PlayImpact(_impacts.Random(_settings.Pieces), at, strength, Styles.GainDb(_settings.Pieces) + 1f);
		_sound.PlayImpact(_impacts.Random(_settings.Board), at, strength * 0.6f, Styles.GainDb(_settings.Board));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		if (_busy || !IsComputerTurn || _game.IsOver)
			return;
		if (!_rolled)
		{
			_ = Roll();
			return;
		}
		if (!_computer.Thinking && _pendingTurn == null)
		{
			var player = new ComputerPlayer(_demo ? Difficulty.Medium : _settings.Difficulty);
			var snapshot = _game.Clone();
			_computer.MinThinkTime = _demo ? 0.15f : 0.5f;
			_computer.Start(() =>
			{
				_pendingTurn = player.ChooseTurn(snapshot);
				return _pendingTurn.Count;
			}, $"computer ({player.Difficulty})");
			return;
		}
		if (_computer.Thinking)
		{
			if (_computer.Poll(dt) == null)
				return;
		}
		if (_pendingTurn is { Count: > 0 } turn)
		{
			var step = turn[0];
			turn.RemoveAt(0);
			if (_game.PlayableSteps().Contains(step))
				_ = PlayStep(step);
			else
				_pendingTurn = null;
		}
		else
		{
			_pendingTurn = null;
		}
	}

	// ---------------------------------------------------------------- Games, undo, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(StartGame);
		}
		else
		{
			StartGame();
		}
	}

	private void StartGame()
	{
		_computer.Cancel();
		_pendingTurn = null;
		_game.Reset();
		_rolled = false;
		_counted = false;
		_selected = -1;
		foreach (var die in _dieNodes)
			die.Visible = false;
		RebuildCheckers();
		RefreshUi();
	}

	private bool CanUndo => VsComputer && !_demo && !_busy && _game.TurnsMade > 0 && (_game.IsOver || _game.WhiteToMove == HumanIsWhite);

	private void OnUndoPressed()
	{
		if (!CanUndo)
			return;
		DevLog.Write("ad: rewarded (undo)");
		_hud.SetUndoVisible(false);
		_ads.ShowRewarded(rewarded: UndoTurn, closed: () => RefreshUi());
	}

	/// <summary>Back to the start of your last turn, with the same dice (so undo can't buy a re-roll).</summary>
	private void UndoTurn()
	{
		_computer.Cancel();
		_pendingTurn = null;
		if (_game.IsOver && _counted)
		{
			if (_game.WhiteWon == true)
				_scoreWhite -= _game.Points;
			else
				_scoreBlack -= _game.Points;
			_counted = false;
		}
		while (_game.LastRollerWhite == !HumanIsWhite)
			_game.UndoTurn();
		if (_game.LastRollerWhite == HumanIsWhite)
			_game.UndoTurn();
		_rolled = _game.Dice.Count > 0;
		_selected = -1;
		RebuildCheckers();
		for (int i = 0; i < 2; i++)
		{
			_dieNodes[i].Visible = _rolled;
			if (_rolled)
			{
				_dieNodes[i].Position = new Vector3((i - 0.5f) * 0.9f, Die.Size / 2, 2.4f);
				_dieNodes[i].Basis = Die.Showing(i == 0 ? _game.Dice[0] : _game.Dice[^1], 0);
			}
		}
		RefreshUi();
	}

	// ---------------------------------------------------------------- UI

	private string NameOf(bool white) => VsComputer && !_demo
		? (white == HumanIsWhite ? "You" : "Computer")
		: Colours.NameOf(!white);

	private void ShowGlows()
	{
		foreach (var glow in _glows.Values)
			glow.Visible = false;
		if (_busy || !_rolled || _game.IsOver || (IsComputerTurn && !_demo))
			return;
		bool white = _game.WhiteToMove;
		var steps = _game.PlayableSteps();
		if (_selected == -1)
		{
			foreach (int from in steps.Select(s => s.From).Distinct())
				Glow(KeyFor(from, white), _sourceMaterial);
		}
		else
		{
			Glow(KeyFor(_selected, white), _sourceMaterial);
			foreach (var step in steps.Where(s => s.From == _selected))
				Glow(KeyFor(step.To, white), _glowMaterial);
		}
	}

	private void Glow(int key, Material material)
	{
		if (!_glows.TryGetValue(key, out var glow))
			return;
		glow.MaterialOverride = material;
		glow.Visible = true;
	}

	private void RefreshUi()
	{
		_hud.SetScore($"{NameOf(true)}  {_scoreWhite}   —   {_scoreBlack}  {NameOf(false)}   ·   pips {_game.Pips(true)}–{_game.Pips(false)}");
		_hud.SetNewGameVisible(_game.IsOver && !_busy);
		_hud.SetUndoVisible(CanUndo);
		_roll.Visible = !_busy && !_rolled && !_game.IsOver && !IsComputerTurn;
		ShowGlows();
		if (_game.IsOver)
		{
			if (_busy)
				return;
			bool whiteWon = _game.WhiteWon == true;
			string gammon = _game.Points == 2 ? " A gammon: 2 points!" : "";
			_hud.SetStatus(NameOf(whiteWon) == "You" ? $"You win!{gammon}" : $"{NameOf(whiteWon)} wins.{gammon}", Colours.UiColorOf(!whiteWon));
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}
		bool turn = _game.WhiteToMove;
		string name = NameOf(turn);
		string status = !_rolled
			? (name == "You" ? "Your turn: roll" : $"{name} to roll")
			: name == "You" ? $"Move: {string.Join(" ", _game.Dice)}" : $"{name}: {string.Join(" ", _game.Dice)}";
		_hud.SetStatus(status, Colours.UiColorOf(!turn));
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`, `--board=Wood`,
	/// `--pieces=Marble`, `--spin=30` (degrees). Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--pieces" when System.Enum.TryParse(value, out Style pieces):
					_settings.Pieces = pieces;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
			}
		}
	}
}
