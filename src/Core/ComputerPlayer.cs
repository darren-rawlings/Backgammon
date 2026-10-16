using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Core;

namespace Backgammon.Core;

/// <summary>
/// The computer opponent. It looks at every legal way to play its roll and scores the position
/// each one leaves, from its own side: the pip race, blots (lone checkers) that the opponent could
/// hit (weighted by how many of the 36 rolls hit them), points made (two or more checkers),
/// especially in its home board and in a row (a prime), checkers sent to the bar, and bearing off.
/// Easy plays a random legal turn a third of the time; Medium uses the evaluation; Hard also looks
/// one roll ahead, averaging over all 21 possible opponent rolls.
/// </summary>
public sealed class ComputerPlayer
{
	private readonly Random _random;

	public Difficulty Difficulty { get; }

	public ComputerPlayer(Difficulty difficulty, int? seed = null)
	{
		Difficulty = difficulty;
		_random = seed is { } s ? new Random(s) : new Random();
	}

	public List<Step> ChooseTurn(BackgammonGame game)
	{
		var turns = game.LegalTurns();
		if (turns.Count == 0)
			return new List<Step>();
		if (turns.Count == 1)
			return turns[0];
		if (Difficulty == Difficulty.Easy && _random.NextDouble() < 0.33)
			return turns[_random.Next(turns.Count)];

		bool me = game.WhiteToMove;
		List<Step> best = turns[0];
		double bestScore = double.MinValue;
		// Hard: shortlist by the quick evaluation, then look a roll ahead on the best few.
		var scored = turns.Select(turn => (Turn: turn, Score: Evaluate(After(game, turn), me))).OrderByDescending(t => t.Score).ToList();
		if (Difficulty != Difficulty.Hard)
			return scored[0].Turn;
		foreach (var (turn, _) in scored.Take(6))
		{
			double score = LookAhead(After(game, turn), me);
			if (score > bestScore)
			{
				bestScore = score;
				best = turn;
			}
		}
		return best;
	}

	private static BackgammonGame After(BackgammonGame game, List<Step> turn)
	{
		var next = game.Clone();
		foreach (var step in turn)
			next.ApplyStep(step);
		next.EndTurn();
		return next;
	}

	/// <summary>The average, over the opponent's 21 distinct rolls, of the position after their best reply.</summary>
	private static double LookAhead(BackgammonGame game, bool me)
	{
		if (game.IsOver)
			return Evaluate(game, me);
		double total = 0;
		int weight = 0;
		for (int a = 1; a <= 6; a++)
		{
			for (int b = a; b <= 6; b++)
			{
				int chance = a == b ? 1 : 2;
				var roll = game.Clone();
				roll.Roll(a, b);
				var replies = roll.LegalTurns();
				double worst = replies.Count == 0
					? Evaluate(After(roll, new List<Step>()), me)
					: replies.Min(reply => Evaluate(After(roll, reply), me));
				total += worst * chance;
				weight += chance;
			}
		}
		return total / weight;
	}

	/// <summary>Position score from <paramref name="white"/>'s side; higher is better for them.</summary>
	public static double Evaluate(BackgammonGame game, bool white)
	{
		if (game.IsOver)
			return game.WhiteWon == white ? 10_000 * game.Points : -10_000 * game.Points;
		double score = 0;
		// The race: fewer pips to go is better.
		score += (game.Pips(!white) - game.Pips(white)) * 1.0;
		score += (game.OffCount(white) - game.OffCount(!white)) * 6;
		score += (game.BarCount(!white) - game.BarCount(white)) * 12;
		bool contact = Contact(game);
		int run = 0, longest = 0;
		for (int p = 1; p <= 24; p++)
		{
			// p is this side's point number; convert to White's numbering for the board.
			int count = game.PointCount(white ? p : 25 - p) * (white ? 1 : -1);
			if (count >= 2)
			{
				score += p <= 6 ? 6 : p <= 9 ? 3 : 1.5;
				run++;
				longest = Math.Max(longest, run);
			}
			else
			{
				run = 0;
			}
			if (count == 1 && contact)
				score -= BlotRisk(game, white, p) * (p <= 6 ? 0.9 : 0.6) * 18;
		}
		score += longest >= 3 ? longest * longest * 1.5 : 0;
		return score;
	}

	/// <summary>Whether the two sides can still hit each other (once they've passed, it's a pure race).</summary>
	private static bool Contact(BackgammonGame game)
	{
		int whiteBack = 0, blackBack = 25;
		for (int p = 1; p <= 24; p++)
		{
			if (game.PointCount(p) > 0)
				whiteBack = Math.Max(whiteBack, p);
			if (game.PointCount(p) < 0)
				blackBack = Math.Min(blackBack, p);
		}
		if (game.BarCount(true) > 0)
			whiteBack = 25;
		if (game.BarCount(false) > 0)
			blackBack = 0;
		return whiteBack > blackBack;
	}

	/// <summary>
	/// Roughly the chance (0-1) that an enemy checker can hit a blot on this side's point p: direct
	/// shots within 6 are likely, combination shots up to 12 less so.
	/// </summary>
	private static double BlotRisk(BackgammonGame game, bool white, int p)
	{
		double risk = 0;
		// Enemy checkers sit on higher numbers in the enemy's travel direction, i.e. lower p for us.
		for (int distance = 1; distance <= 12; distance++)
		{
			int from = p - distance;
			int enemyCount;
			if (from <= 0)
				enemyCount = from == 0 ? game.BarCount(!white) : 0;
			else
				enemyCount = Math.Max(0, -game.PointCount(white ? from : 25 - from) * (white ? 1 : -1));
			if (enemyCount == 0)
				continue;
			risk += distance <= 6 ? 11.0 / 36 : distance <= 12 ? 3.0 / 36 : 0;
		}
		return Math.Min(risk, 1);
	}
}
