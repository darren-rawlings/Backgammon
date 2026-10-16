using System.Linq;
using Backgammon.Core;
using Renegade.Core;
using Xunit;

namespace Backgammon.Tests;

public class BackgammonTests
{
	[Fact]
	public void StartsWithFifteenEachAndEqualPips()
	{
		var game = new BackgammonGame();
		int white = Enumerable.Range(1, 24).Sum(p => System.Math.Max(game.PointCount(p), 0));
		int black = Enumerable.Range(1, 24).Sum(p => System.Math.Max(-game.PointCount(p), 0));
		Assert.Equal(15, white);
		Assert.Equal(15, black);
		Assert.Equal(167, game.Pips(true));
		Assert.Equal(167, game.Pips(false));
	}

	[Fact]
	public void AnOpeningRollUsesBothDice()
	{
		var game = new BackgammonGame();
		game.Roll(3, 1);
		var turns = game.LegalTurns();
		Assert.NotEmpty(turns);
		Assert.All(turns, turn => Assert.Equal(2, turn.Count));
		// The classic 3-1: make the 5 point (8/5, 6/5).
		Assert.Contains(turns, turn => turn.Select(s => (s.From, s.To)).OrderBy(x => x).SequenceEqual(new[] { (6, 5), (8, 5) }));
	}

	[Fact]
	public void DoublesGiveFourMoves()
	{
		var game = new BackgammonGame();
		game.Roll(6, 6);
		Assert.All(game.LegalTurns(), turn => Assert.Equal(4, turn.Count));
	}

	[Fact]
	public void BlockedPointsCantBeLandedOn()
	{
		var game = new BackgammonGame();
		game.Roll(5, 2);
		// From 24, a 5 lands on 19 (Black has five there): never legal.
		Assert.DoesNotContain(game.LegalTurns().SelectMany(t => t), s => s.From == 24 && s.To == 19);
	}

	[Fact]
	public void UndoReturnsToTheRoll()
	{
		var game = new BackgammonGame();
		game.Roll(3, 1);
		var turn = game.LegalTurns()[0];
		foreach (var step in turn)
			game.Play(step);
		game.EndTurn();
		Assert.False(game.WhiteToMove);
		Assert.True(game.UndoTurn());
		Assert.True(game.WhiteToMove);
		Assert.Equal(new[] { 3, 1 }, game.Dice);
		Assert.Equal(167, game.Pips(true));
	}

	private static bool? PlayOut(ComputerPlayer white, ComputerPlayer black, int seed)
	{
		var game = new BackgammonGame();
		var dice = new System.Random(seed);
		for (int turn = 0; turn < 600 && !game.IsOver; turn++)
		{
			game.Roll(dice.Next(1, 7), dice.Next(1, 7));
			foreach (var step in (game.WhiteToMove ? white : black).ChooseTurn(game))
				game.Play(step);
			game.EndTurn();
		}
		return game.WhiteWon;
	}

	[Fact]
	public void GamesFinishWithEveryCheckerOff()
	{
		for (int seed = 0; seed < 5; seed++)
			Assert.NotNull(PlayOut(new ComputerPlayer(Difficulty.Medium, seed), new ComputerPlayer(Difficulty.Easy, seed + 9), seed));
	}

	[Fact]
	public void MediumBeatsEasyMoreOftenThanNot()
	{
		int wins = 0;
		for (int seed = 0; seed < 20; seed++)
		{
			bool mediumWhite = seed % 2 == 0;
			var medium = new ComputerPlayer(Difficulty.Medium, seed);
			var easy = new ComputerPlayer(Difficulty.Easy, 100 + seed);
			bool? whiteWon = PlayOut(mediumWhite ? medium : easy, mediumWhite ? easy : medium, 1000 + seed);
			if (whiteWon == mediumWhite)
				wins++;
		}
		Assert.True(wins >= 13, $"Medium won only {wins} of 20.");
	}

	[Fact]
	public void HardThinksQuicklyEnoughForAPhone()
	{
		var game = new BackgammonGame();
		var hard = new ComputerPlayer(Difficulty.Hard, 1);
		var dice = new System.Random(4);
		long worst = 0;
		for (int turn = 0; turn < 40 && !game.IsOver; turn++)
		{
			game.Roll(dice.Next(1, 7), dice.Next(1, 7));
			var watch = System.Diagnostics.Stopwatch.StartNew();
			var chosen = hard.ChooseTurn(game);
			worst = System.Math.Max(worst, watch.ElapsedMilliseconds);
			foreach (var step in chosen)
				game.Play(step);
			game.EndTurn();
		}
		Assert.True(worst < 1500, $"Slowest turn took {worst} ms.");
	}
}
