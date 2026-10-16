using System.Collections.Generic;
using Renegade.Ui;

namespace Backgammon.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Race home",
			"You and your opponent race your 15 checkers in opposite directions around the board, into your home corner, and then off the board. First to bear them all off wins.",
			"res://tutorial/roll.ogv"),
		new TutorialPage("Roll and move",
			"Press Roll (or tap the board). Each die moves one checker that many points: tap a glowing checker, then a glowing point. Doubles move four times. You can't land where your opponent has two or more.",
			"res://tutorial/roll.ogv"),
		new TutorialPage("Hit a lone checker",
			"Land on a point with just one of your opponent's checkers and it's knocked onto the bar in the middle. They must bring it back in from the start before moving anything else.",
			"res://tutorial/hit.ogv"),
		new TutorialPage("Bear off",
			"Once all your checkers are in your home corner, roll to take them off the board. Bear off all 15 first and your checkers catch fire. If your opponent hasn't borne any off, it's a gammon: 2 points.",
			"res://tutorial/bearoff.ogv"),
		new TutorialPage("Tips",
			"Keep checkers in pairs so they can't be hit. Drag sideways to spin the board, or freeze it in Settings. Against the computer, Undo (ad) takes back your last turn with the same dice.",
			"res://tutorial/hit.ogv"),
	};
}
