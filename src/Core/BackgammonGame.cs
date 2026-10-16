using System;
using System.Collections.Generic;
using System.Linq;

namespace Backgammon.Core;

/// <summary>One checker move: from a point to a point. 25 is the bar, 0 is borne off.</summary>
public readonly record struct Step(int From, int To, int Die, bool Hit);

/// <summary>
/// Backgammon, seen from the side to move. Points are numbered 1-24 in that player's direction of
/// travel: they move from 24 down to 1 and bear off past 1; 25 is their bar. The board is kept for
/// White; Black's view is the mirror (point p for Black is 25 − p for White). Counts: positive are
/// White's checkers, negative Black's.
///
/// Rules: both dice must be used if possible, else the larger; doubles give four moves; checkers on
/// the bar must enter first; a point with two or more enemy checkers is blocked; landing on a single
/// enemy checker hits it to the bar; bearing off only once all fifteen are home (points 1-6). A
/// gammon (loser bore off none) counts double; this version scores single games as one point and
/// gammons as two, with no doubling cube.
/// </summary>
public sealed class BackgammonGame
{
	public const int Bar = 25;
	public const int Off = 0;

	/// <summary>points[1..24] from White's view; positive White, negative Black.</summary>
	private readonly int[] _points = new int[26];
	private readonly int[] _bar = new int[2];
	private readonly int[] _off = new int[2];
	private readonly Stack<(int[] Points, int[] Bar, int[] Off, bool WhiteToMove, int[] Dice, bool Over)> _history = new();

	public bool WhiteToMove { get; private set; } = true;

	/// <summary>The dice still to be used this turn (four entries for doubles).</summary>
	public List<int> Dice { get; private set; } = new();

	public bool IsOver { get; private set; }

	public BackgammonGame() => Reset();

	private BackgammonGame(BackgammonGame other)
	{
		Array.Copy(other._points, _points, 26);
		Array.Copy(other._bar, _bar, 2);
		Array.Copy(other._off, _off, 2);
		WhiteToMove = other.WhiteToMove;
		Dice = new List<int>(other.Dice);
		IsOver = other.IsOver;
	}

	public BackgammonGame Clone() => new(this);

	public void Reset()
	{
		Array.Clear(_points);
		_bar[0] = _bar[1] = _off[0] = _off[1] = 0;
		// The standard start, from White's view (White moves 24 → 1).
		Set(24, 2); Set(13, 5); Set(8, 3); Set(6, 5);
		Set(1, -2); Set(12, -5); Set(17, -3); Set(19, -5);
		WhiteToMove = true;
		Dice.Clear();
		IsOver = false;
		_history.Clear();
	}

	private void Set(int point, int count) => _points[point] = count;

	/// <summary>
	/// Sets up a position (for tutorial clips and tests): checkers by White's point numbers,
	/// positive for White and negative for Black, plus any on the bars or borne off.
	/// </summary>
	public void Load(IReadOnlyDictionary<int, int> points, bool whiteToMove, int whiteOff = 0, int blackOff = 0, int whiteBar = 0, int blackBar = 0)
	{
		Array.Clear(_points);
		foreach (var (point, count) in points)
			_points[point] = count;
		_bar[0] = whiteBar;
		_bar[1] = blackBar;
		_off[0] = whiteOff;
		_off[1] = blackOff;
		WhiteToMove = whiteToMove;
		Dice.Clear();
		IsOver = false;
		_history.Clear();
	}

	/// <summary>Checkers on a point as seen from White: positive White, negative Black.</summary>
	public int PointCount(int whitePoint) => _points[whitePoint];

	public int BarCount(bool white) => _bar[white ? 0 : 1];

	public int OffCount(bool white) => _off[white ? 0 : 1];

	/// <summary>Converts the mover's point number to White's numbering.</summary>
	public int ToWhite(int point) => WhiteToMove ? point : 25 - point;

	/// <summary>How many of the mover's checkers are on one of their points (0 if none or enemy).</summary>
	private int Mine(int point)
	{
		int count = _points[ToWhite(point)];
		return WhiteToMove ? Math.Max(count, 0) : Math.Max(-count, 0);
	}

	private int Theirs(int point)
	{
		int count = _points[ToWhite(point)];
		return WhiteToMove ? Math.Max(-count, 0) : Math.Max(count, 0);
	}

	private bool AllHome() => _bar[WhiteToMove ? 0 : 1] == 0 && Enumerable.Range(7, 18).All(p => Mine(p) == 0);

	private int HighestOccupied()
	{
		for (int p = 24; p >= 1; p--)
		{
			if (Mine(p) > 0)
				return p;
		}
		return 0;
	}

	/// <summary>Pip count for a side: how far its checkers must still travel.</summary>
	public int Pips(bool white)
	{
		int total = _bar[white ? 0 : 1] * 25;
		for (int p = 1; p <= 24; p++)
		{
			int count = _points[p];
			if (white && count > 0)
				total += count * p;
			else if (!white && count < 0)
				total += -count * (25 - p);
		}
		return total;
	}

	public void Roll(int a, int b)
	{
		if (Dice.Count > 0 || IsOver)
			throw new InvalidOperationException("Dice already rolled.");
		Dice = a == b ? new List<int> { a, a, a, a } : new List<int> { Math.Max(a, b), Math.Min(a, b) };
		// Undo goes back to here: the dice stay as rolled, so undoing can't be used to re-roll.
		_history.Push(((int[])_points.Clone(), (int[])_bar.Clone(), (int[])_off.Clone(), WhiteToMove, Dice.ToArray(), IsOver));
	}

	/// <summary>The single steps legal with one die right now, ignoring the rule about using both dice.</summary>
	private IEnumerable<Step> SingleSteps(int die)
	{
		if (_bar[WhiteToMove ? 0 : 1] > 0)
		{
			int entry = 25 - die;
			if (Theirs(entry) < 2)
				yield return new Step(Bar, entry, die, Theirs(entry) == 1);
			yield break;
		}
		bool home = AllHome();
		int highest = HighestOccupied();
		for (int from = 24; from >= 1; from--)
		{
			if (Mine(from) == 0)
				continue;
			int to = from - die;
			if (to >= 1)
			{
				if (Theirs(to) < 2)
					yield return new Step(from, to, die, Theirs(to) == 1);
			}
			else if (home && (to == 0 || from == highest))
			{
				// Bearing off: exactly, or with a larger die from the highest point.
				yield return new Step(from, Off, die, false);
			}
		}
	}

	/// <summary>
	/// Every legal full turn, as sequences of steps, after the rule that as many dice as possible must
	/// be used (and the larger die if only one can be). Duplicates that end in the same position are
	/// removed, so the computer doesn't search the same thing twice.
	/// </summary>
	public List<List<Step>> LegalTurns()
	{
		var results = new List<List<Step>>();
		var seen = new HashSet<string>();
		Explore(this, new List<Step>(), results, seen);
		if (results.Count == 0)
			return results;
		int most = results.Max(t => t.Count);
		var full = results.Where(t => t.Count == most).ToList();
		if (most == 1 && Dice.Count == 2 && Dice[0] != Dice[1])
		{
			var withLarger = full.Where(t => t[0].Die == Dice.Max()).ToList();
			if (withLarger.Count > 0)
				full = withLarger;
		}
		return full;
	}

	private static void Explore(BackgammonGame game, List<Step> path, List<List<Step>> results, HashSet<string> seen)
	{
		bool moved = false;
		foreach (int die in game.Dice.Distinct().ToList())
		{
			foreach (var step in game.SingleSteps(die).ToList())
			{
				var next = game.Clone();
				next.ApplyStep(step);
				path.Add(step);
				string key = next.Key();
				if (next.Dice.Count > 0 && !next.IsOver)
				{
					if (seen.Add(key + "|" + path.Count))
						Explore(next, path, results, seen);
				}
				else if (seen.Add(key + "|" + path.Count))
				{
					results.Add(new List<Step>(path));
				}
				path.RemoveAt(path.Count - 1);
				moved = true;
			}
		}
		if (!moved && path.Count > 0)
			results.Add(new List<Step>(path));
	}

	private string Key() => string.Join(",", _points) + ";" + _bar[0] + "," + _bar[1] + ";" + _off[0] + "," + _off[1];

	/// <summary>The steps playable next, as part of some legal full turn.</summary>
	public List<Step> PlayableSteps()
	{
		// Checked step by step rather than from LegalTurns, whose de-duplication keeps only one
		// order of moves that reach the same position (13/11 then 13/8 hides 13/11 as a first step).
		if (IsOver || Dice.Count == 0)
			return new List<Step>();
		var candidates = new List<(Step Step, int Use)>();
		foreach (int die in Dice.Distinct())
		{
			foreach (var step in SingleSteps(die))
			{
				var next = Clone();
				next.ApplyStep(step);
				candidates.Add((step, 1 + next.MaxUse()));
			}
		}
		if (candidates.Count == 0)
			return new List<Step>();
		int most = candidates.Max(c => c.Use);
		var playable = candidates.Where(c => c.Use == most).Select(c => c.Step).ToList();
		// When only one die can be used, it must be the larger one if that's possible.
		if (most == 1 && Dice.Count == 2 && Dice[0] != Dice[1] && playable.Any(s => s.Die == Dice.Max()))
			playable = playable.Where(s => s.Die == Dice.Max()).ToList();
		return playable.Distinct().ToList();
	}

	/// <summary>The most dice that can still be used from here.</summary>
	private int MaxUse()
	{
		if (IsOver || Dice.Count == 0)
			return 0;
		int best = 0;
		foreach (int die in Dice.Distinct())
		{
			foreach (var step in SingleSteps(die))
			{
				var next = Clone();
				next.ApplyStep(step);
				best = Math.Max(best, 1 + next.MaxUse());
				if (best == Dice.Count)
					return best;
			}
		}
		return best;
	}

	/// <summary>Plays one step of the turn (it must start a legal full turn).</summary>
	public void Play(Step step)
	{
		if (!PlayableSteps().Contains(step))
			throw new InvalidOperationException($"Can't move {step.From} → {step.To}.");
		ApplyStep(step);
	}

	/// <summary>True when the mover has no more legal steps (the turn is finished, or they're stuck).</summary>
	public bool TurnDone => IsOver || Dice.Count == 0 || PlayableSteps().Count == 0;

	/// <summary>Hands the dice to the other side once the turn is done.</summary>
	public void EndTurn()
	{
		Dice.Clear();
		if (!IsOver)
			WhiteToMove = !WhiteToMove;
	}

	/// <summary>Goes back to the start of the most recent turn, just after its dice were rolled.</summary>
	public bool UndoTurn()
	{
		if (_history.Count == 0)
			return false;
		var (points, bar, off, white, dice, over) = _history.Pop();
		Array.Copy(points, _points, 26);
		Array.Copy(bar, _bar, 2);
		Array.Copy(off, _off, 2);
		WhiteToMove = white;
		Dice = dice.ToList();
		IsOver = over;
		return true;
	}

	public int TurnsMade => _history.Count;

	/// <summary>Who rolled for the most recent turn on record.</summary>
	public bool? LastRollerWhite => _history.Count == 0 ? null : _history.Peek().WhiteToMove;

	internal void ApplyStep(Step step)
	{
		int side = WhiteToMove ? 0 : 1;
		int sign = WhiteToMove ? 1 : -1;
		if (step.From == Bar)
			_bar[side]--;
		else
			_points[ToWhite(step.From)] -= sign;
		if (step.To == Off)
		{
			_off[side]++;
		}
		else
		{
			int to = ToWhite(step.To);
			if (step.Hit)
			{
				_points[to] = 0;
				_bar[1 - side]++;
			}
			_points[to] += sign;
		}
		Dice.Remove(step.Die);
		if (_off[side] == 15)
			IsOver = true;
	}

	/// <summary>The winner once over: true for White.</summary>
	public bool? WhiteWon => IsOver ? _off[0] == 15 : null;

	/// <summary>1 for a single game, 2 for a gammon (the loser bore off none).</summary>
	public int Points => !IsOver ? 0 : _off[WhiteWon == true ? 1 : 0] == 0 ? 2 : 1;
}
