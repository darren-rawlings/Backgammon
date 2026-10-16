using System.Collections.Generic;
using Godot;
using Renegade.Looks;

namespace Backgammon.Game;

/// <summary>
/// A die: an ivory cube with dark pips, 1 opposite 6, 2 opposite 5, 3 opposite 4. A throw tumbles
/// it through the air and lands it showing the value the game rolled.
/// </summary>
public partial class Die : Node3D
{
	public const float Size = 0.48f;

	/// <summary>The face showing each value, by its outward normal.</summary>
	private static readonly Dictionary<int, Vector3> Faces = new()
	{
		[1] = Vector3.Up, [6] = Vector3.Down, [2] = Vector3.Right, [5] = Vector3.Left, [3] = Vector3.Back, [4] = Vector3.Forward,
	};

	private static readonly Dictionary<int, Vector2[]> Pips = new()
	{
		[1] = new[] { Vector2.Zero },
		[2] = new[] { new Vector2(-1, -1), new Vector2(1, 1) },
		[3] = new[] { new Vector2(-1, -1), Vector2.Zero, new Vector2(1, 1) },
		[4] = new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) },
		[5] = new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1), Vector2.Zero },
		[6] = new[] { new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1) },
	};

	public int Value { get; private set; } = 1;

	public override void _Ready()
	{
		var body = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * Size },
			MaterialOverride = Materials.For(new Look(Look.PlasticPattern, new Color("f7f1e3"), new Color("d9cfb8"), 0.3f)),
		};
		AddChild(body);
		var pipMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 10, Rings = 5 };
		var pipMaterial = new StandardMaterial3D { AlbedoColor = new Color("1a1a1f"), Roughness = 0.4f };
		foreach (var (value, normal) in Faces)
		{
			// Two axes lying in the face.
			var u = Mathf.Abs(normal.Y) > 0.5f ? Vector3.Right : Vector3.Up;
			var v = normal.Cross(u);
			foreach (var pip in Pips[value])
			{
				var position = normal * (Size / 2 - 0.012f) + (u * pip.X + v * pip.Y) * 0.13f;
				AddChild(new MeshInstance3D { Mesh = pipMesh, MaterialOverride = pipMaterial, Position = position, Scale = new Vector3(1, 1, 1) * 0.9f });
			}
		}
	}

	/// <summary>The orientation that puts <paramref name="value"/> on top, turned by <paramref name="yaw"/>.</summary>
	public static Basis Showing(int value, float yaw)
	{
		var face = value switch
		{
			6 => new Basis(Vector3.Right, Mathf.Pi),
			2 => new Basis(Vector3.Back, Mathf.Pi / 2),
			5 => new Basis(Vector3.Back, -Mathf.Pi / 2),
			3 => new Basis(Vector3.Right, -Mathf.Pi / 2),
			4 => new Basis(Vector3.Right, Mathf.Pi / 2),
			_ => Basis.Identity,
		};
		return new Basis(Vector3.Up, yaw) * face;
	}

	/// <summary>
	/// Throws the die from <paramref name="from"/> to land at <paramref name="to"/> showing
	/// <paramref name="value"/>: an arc with two bounces, spinning down to rest.
	/// </summary>
	public Tween Throw(Vector3 from, Vector3 to, int value, float delay)
	{
		Value = value;
		var final = Showing(value, (float)GD.RandRange(-0.4, 0.4));
		var axis = new Vector3((float)GD.RandRange(-1, 1), 0.3f, (float)GD.RandRange(-1, 1)).Normalized();
		float spins = (float)GD.RandRange(3, 5) * Mathf.Tau;
		var rest = to + new Vector3(0, Size / 2, 0);
		var tween = CreateTween();
		tween.TweenInterval(delay);
		tween.TweenMethod(Callable.From<float>(t =>
		{
			// Three arcs, each lower: the throw and two bounces.
			float height = t < 0.6f ? 2.2f * Mathf.Sin(t / 0.6f * Mathf.Pi) : t < 0.85f ? 0.45f * Mathf.Sin((t - 0.6f) / 0.25f * Mathf.Pi) : 0.12f * Mathf.Sin((t - 0.85f) / 0.15f * Mathf.Pi);
			Position = from.Lerp(rest, Mathf.Min(1f, t * 1.15f)) + new Vector3(0, height, 0);
			Basis = new Basis(axis, (1 - t) * (1 - t) * spins) * final;
		}), 0f, 1f, 0.9f);
		return tween;
	}
}
