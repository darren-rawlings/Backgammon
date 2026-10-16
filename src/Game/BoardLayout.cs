using Godot;

namespace Backgammon.Game;

/// <summary>
/// The board stands upright on a portrait phone. Points 1-12 (White's numbering) run up the right
/// column from the near end, 13-24 down the left column, so White travels up the left and down the
/// right, home in the near-right quarter; Black is the mirror, home near-left. The bar is the strip
/// across the middle; the bear-off trays sit past the near end.
/// </summary>
public static class BoardLayout
{
	public const float Pitch = 0.78f;
	public const float BarHalf = 0.42f;
	public const float PointLength = 2.75f;
	public const float Gutter = 0.75f;
	public const float HalfX = Gutter + PointLength + 0.25f;
	public const float EndZ = BarHalf + 6 * Pitch;
	public const float HalfZ = EndZ + 0.3f;
	public const float Thickness = 0.4f;

	public const float CheckerRadius = 0.34f;
	public const float CheckerHeight = 0.13f;

	public const float TrayZ = HalfZ + 0.75f;

	/// <summary>Where the White-numbered point lies along the board: its z, and which column (+1 right, −1 left).</summary>
	public static (float Z, int Side) Point(int whitePoint)
	{
		int side = whitePoint <= 12 ? 1 : -1;
		int k = whitePoint <= 12 ? whitePoint : 25 - whitePoint;
		float z = k <= 6 ? BarHalf + (6 - k + 0.5f) * Pitch : -(BarHalf + (k - 7 + 0.5f) * Pitch);
		return (z, side);
	}

	/// <summary>The position of the n-th checker (from the rim) on a point holding <paramref name="count"/>.</summary>
	public static Vector3 Slot(int whitePoint, int index, int count)
	{
		var (z, side) = Point(whitePoint);
		// Five fit in a row; more squeeze up so the stack never crosses into the gutter.
		float spacing = count <= 5 ? CheckerRadius * 2 + 0.02f : (PointLength - CheckerRadius * 2) / (count - 1);
		float x = side * (HalfX - 0.25f - CheckerRadius - index * spacing);
		return new Vector3(x, CheckerHeight / 2 + 0.01f, z);
	}

	public static Vector3 BarSlot(bool white, int index) =>
		new((white ? 1 : -1) * (1.1f + index * (CheckerRadius * 2 + 0.04f)), CheckerHeight / 2 + 0.02f, 0);

	/// <summary>Borne-off checkers stand on edge in a row in the tray, like coins in a rack.</summary>
	public static Vector3 OffSlot(bool white, int index) =>
		new((white ? 1 : -1) * (0.8f + index * 0.17f), CheckerRadius, TrayZ);

	/// <summary>The White-numbered point under a board-space point, 25/−25 for the bar (sign = side), 0 for a tray, or −1.</summary>
	public static int Hit(Vector3 local, out bool rightSide)
	{
		rightSide = local.X > 0;
		if (Mathf.Abs(local.X) > HalfX + 0.3f)
			return -1;
		if (local.Z > HalfZ + 0.1f && local.Z < TrayZ + 0.7f)
			return 0;
		if (Mathf.Abs(local.Z) > EndZ)
			return -1;
		if (Mathf.Abs(local.Z) < BarHalf)
			return rightSide ? 25 : -25;
		if (Mathf.Abs(local.X) < Gutter - 0.1f)
			return -1;
		int k = local.Z > 0
			? 6 - Mathf.Clamp(Mathf.FloorToInt((local.Z - BarHalf) / Pitch), 0, 5)
			: 7 + Mathf.Clamp(Mathf.FloorToInt((-local.Z - BarHalf) / Pitch), 0, 5);
		return rightSide ? k : 25 - k;
	}
}
