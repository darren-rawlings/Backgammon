# Backgammon: design

Store title: **Backgammon**. Package `com.renegadegames.backgammon`.

## The game
Standard backgammon: 15 checkers each, two dice, doubles play four times. Both dice must be used
when possible (the larger if only one can be); checkers on the bar enter first; two or more enemy
checkers block a point; landing on a single one hits it to the bar; bear off once all fifteen are
home. A gammon (the loser bore off none) scores 2 points, otherwise 1. No doubling cube in v1.

## Feel
- The board stands upright on the phone: White (you, against the computer) travels up the left
  column and down the right, home in the near-right quarter; Black mirrors it, home near-left.
  Triangle points in two tones; checkers in the chosen set (Onyx & Ivory marble by default).
- Roll (button, or tap the board): two dice tumble from your end and bounce to rest showing the
  roll. Points you can move from glow green; tap one, and the points it can reach glow gold.
- **Signature effect, the hit:** a hit checker is knocked flying in a high spinning arc onto the
  bar with a burst of sparks and a hard clack. Borne-off checkers stand on edge in the tray, with
  sparks.
- Drag sideways to spin the board. The score line shows points and both pip counts.

## Modes
Player vs player on one phone; vs computer: Easy (a third of its turns random), Medium (best turn
by an evaluation of race, blots at risk, points made, primes, the bar and bearing off), Hard
(Medium's best six, each looked at one roll ahead over all 21 possible replies).

## Ads
Between-games interstitial every 5th game; Undo (ad) vs the computer returns to the start of your
last turn with the same dice, so it can't buy a re-roll.

## Later
The doubling cube and match play; a tap-to-move-both-dice shortcut; move hints.
