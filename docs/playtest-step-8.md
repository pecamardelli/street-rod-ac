# Step 8 playtest checklist

What nobody has seen in the game yet (see `docs/roadmap.md`, "Step 8"). Tick a box when it works. When it doesn't,
write what happened under it: what you did, what you expected, what you saw. A screenshot or the time helps: the logs
are `%AppData%\StreetRodAC\Logs` and `Documents\Assetto Corsa\logs\custom_shaders_patch.log` (lines tagged
`[Street Corsa]`).

Already tested (2026-09-25): collisions, a police chase. Left for the first release: the end of the game (victory
screen).

## 1. Race rules

Races start from the diner: pick a rival under **OPPONENTS AT THE DINER**, choose **DRAG** or **ROAD**, then
**CHALLENGE TO A RACE**.

- [ ] **A clean drag race to the finish.** Race without touching the rival. The race ends past the line, AC quits on
      its own, and the result (win or loss, money) is right.
- [ ] **A timeslip after a drag race.** A dialog shows R/T, 60', 330', ⅛, 1000', ¼ and the trap speeds. The times
      look believable for the car (`ks_drag` is 1000 m long, so the ¼ mile falls inside the lap).
- [ ] **A contact disqualification, rival at fault.** In a drag race, hold your lane and let the rival drift into you
      (or be touched while they're out of their lane). Expected: the rival is disqualified and you win.
- [ ] **A contact disqualification, you at fault.** Steer out of your lane into the rival. Expected: you're
      disqualified, and it counts as a loss.
- [ ] **A crashed rival being held.** In a road race, get the rival to hit something hard. Expected: it stays where it
      stopped, and you drive on to finish.
- [ ] **A false start on a road race.** Move more than 1 m before the green. Expected: no contest, nothing changes
      hands, and it costs 3 reputation.
- [ ] **`assists.ini` coming back.** Before a race, note your assist settings in AC or Content Manager (or copy
      `Documents\Assetto Corsa\cfg\assists.ini`). After the race they should be back to how they were: damage and
      tyre wear aren't left on.

## 2. Damage

- [ ] **The tow to the garage.** Crash hard in a race. Expected: a "Towed Home" message with what the crash did, and
      you land in the garage.
- [ ] **Dents carried over.** With a damaged body, race again. The car should start the race with its scratches and
      dents, not clean.
- [ ] **Your breakdown.** Break your car during a race: over-rev the engine well past the limiter until it blows, or
      hit hard enough to break a corner. Expected: you're out, and it counts as a loss.
- [ ] **The rival's breakdown.** Harder to set up: it takes a rival whose car breaks mid-race, so it may only turn up
      over several races. Expected: they're out and you win. Both out is a draw.
- [ ] **A broken car stays home.** With a blown engine, a wrecked gearbox or a broken corner, the game refuses to race
      or free-run it and says why.
- [ ] **Repairs.** In the garage, **Repairs** fixes it for money and garage time, and the car can race again.
- [ ] **How damage drives.** With a bent corner the car pulls to one side. With a worn gearbox the shifts are slower.
      It should feel like damage, not like a broken car.

## 3. Police

A road race after 20:00 has the best chance of police (up to 50% with a pink slip or a big wager).

- [ ] **The diner's police note.** When you set up a road race, the matchup shows "Police: …", with "(night
      patrols)" after 20:00. It doesn't show for drag races.
- [ ] **The lights and the siren** during a chase.
- [ ] **The impound.** Get busted. In the garage, the busted car shows the impound note and the **Collect** button.
      You can't collect before the days are up, then paying brings the car home. Check the fine and the daily fee
      add up.

## 4. Career

- [ ] **End Day.** In the garage's calendar panel, **End Day** asks first, then the clock jumps to the next day, the
      night's tasks run (new listings, rivals' news) and the game is saved (load it to check).
- [ ] **A road-race event.** In the newspaper, accept one of the road-race invitations (Classic Showdown, Fifties
      Fever, Sixties Showdown, European Invasion or Chevy Challenge). It runs on a circuit, not the strip, and pays
      out when you win. Bonus: win one with a camshaft prize, and a real camshaft lands on your shelf.

## 5. Economy

Car prices were all $5,000 until the 2026-09-25 audit, so this is the first time they mean anything.

- [ ] **Prices make sense.** At the dealers and in the paper, a big-block muscle car costs clearly more than a
      small six, and nothing looks absurdly cheap or dear.
- [ ] **Selling.** What a car sells for is close to what it's worth, less than a dealer asks.
- [ ] **Rivals can keep up.** Over a few days, rivals still buy cars and parts (the word on the street says so), and
      don't all go broke. Rivals' starting money was set against the old flat $5,000, so this is the likeliest thing
      to be off.
- Your $1M starting money stays until the first release, so don't judge the player's side of the balance by it.

## Notes

Write anything else you notice here, even if it isn't on the list.
