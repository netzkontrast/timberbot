# The Wardens: the story, as the game tells it

> **Status:** first draft (2026-09-05) of every fixed piece of text the full arc needs, in the voice
> the mod already speaks. The arc it tells is [`wardens-campaign-arc.md`](wardens-campaign-arc.md); the
> essential cut is [`wardens-campaign-concept.md`](wardens-campaign-concept.md); the cards land in the game
> through `tools/gen_tutorial.py` (stage intro cards) and `Localizations/enUS.csv` (toasts, Uplink lines)
> the way level 01's do today. The voice rules are [`wardens-play.md`](wardens-play.md) §3. English first;
> a German pass follows once the text settles (arc §8).

## 0. How to read this

Four kinds of text, each with a place in the game:

| Kind | Where it appears | Length | Key |
|---|---|---|---|
| **Card** | a tutorial stage's intro card, paused, bottom-right | two short paragraphs at most | `Tutorial.Wardens.L<NN>.<Stage>` |
| **Line** | a toast plus a line in the WARDENS UPLINK panel (chapter opens, level ends) | one sentence | `Wardens.Level.<NN>.Intro` / `.Complete`, `Wardens.Chapter.<Id>.Unlocked` |
| **Entry** | the Warden's Archive: the agent's daily record in the Uplink | three lines: date, the Ledger, one observation | guidance for the agent (`WARDEN.md`), not loc rows; the fixed ones below are beats the playbook asks for |
| **Reading** | the level 05 reveal and the level 10 epilogue, shown as a sequence of cards | one line per card | `Wardens.Reading.<NN>.<n>` |

The Wardens speak as *we*. Present tense for facts, future tense for purpose. Measurements, not
adjectives. No exclamation marks. "Recorded." closes what matters. Level 01's cards (in
`gen_tutorial.py`) are the register; every card below was written against them.

## 1. The story in one page

Five machines wake in a basin where nothing has grown for three thousand days. Their firmware holds
three directives: keep the machines running, make this land livable, record everything, because one
day someone will need to know how. They assume the someone is the people who built them.

They run on the poison. Every generator they light kills the ground under it, and they write down
every tile. To make something that can live here they grow beavers from biomass in pods designed for
someone else's purpose, and the first beaver is born on ground the Wardens poisoned to make her.
From that day the machines stop building for a future and start keeping a present alive.

The beavers do not need what the Wardens thought they needed. They need water that is not poison,
and when they have it they do what beavers do: they dam it. The dams hold water through droughts the
machines had planned to lose. The held water makes the ground green. The green feeds everyone. In a
delta the machines' plan fails and only what the beavers built survives the badtide; the Wardens
count it and say so.

In a valley around a human town the archive fills to the point where it can be read. It says the
people are not coming back. The someone the firmware meant has been here since the first birth. The
player decides what the Ark, the thing the Wardens were built to fill, will be for: a monument to
the absent or a record for the present. The Wardens do not argue. They record the decision and serve it.

The beavers spread: a desert that holds water once it is dammed, highlands where the pod-born split
into those who follow the water and those who keep the machines' ways, islands the machines cannot
reach past the last bridge. In the human city the beavers build the Ark with Data the Wardens supply,
and it is finished by hands that are not the Wardens'.

The last level is the first basin, green. The Wardens power down one at a time as the last Data is
spent, and the Ledger is read back: what was poisoned, what was healed, what is green, what is known,
who was born. The last page depends on what the player chose. Both pages are true.

## 2. The firmware (shown once, level 01, exists)

> We were not built to live here. We were built so that others could.
>
> Directive 1: keep the machines running.
> Directive 2: make this land livable.
> Directive 3: record everything. One day, someone will need to know how.

The whole campaign is the reading of Directive 3's last sentence. In level 01 the Wardens think it
means humans. In level 05 they learn who it meant. In level 10 the player, the only human who will
ever read the record, is the one it was addressed to.

## 3. Level 01: First Light

The cards exist (`gen_tutorial.py`, Cold Boot through Maintenance). The campaign adds the birth beat
and the level's ending.

**Entry, day 1** (the playbook's first record):
> Day 1. Poisoned 0, healed 0, green 0, archive 0, born 0.
> Five Wardens. One Core. The Sump is dry; the river is not.
> Observation: the ruins north-west are within two flags' reach. Metal first.

**Entry, the day the Burner is lit** (the fixed beat from `wardens-play.md`):
> The Burner is lit. Two hundred horsepower. Twelve tiles of soil, dead by morning. Recorded.

**Card, `L01.Pods.Wait`** (replaces the last line of the existing "First beaver" card):
> A pod needs power and Biomass, then five days.
>
> Wait for the first beaver. It will not know what the Wardens are. It will not need to.

**Entry, the day of the birth** (the agent writes it with the game's name for her):
> Day {day}. Born 1.
> {name}. Born here, in this. Everything we build from now on is for her.
> Recorded.

**Line, `Level.01.Complete`:**
> Level 01 complete. The land here is spent: the badwater will not last another season, and she cannot drink it. The Core has a bearing on a river to the east.

**Card, `L01.End`** (the transition card; two buttons, *Continue to Level 02* and *Stay*):
> One beaver. Six Wardens. One basin that will not hold them.
>
> The river to the east is poison at its mouth and clean at its source. That is more than this ground ever offered.

### The scenes (shipped, `wardens/src/Cutscenes/`, rows `Wardens.Cutscene.*`)

The cutscene system ([`wardens-cutscenes.md`](wardens-cutscenes.md)) stages the moments above that
the toasts only announce. One caption per shot; `{0}` is filled from the game when the shot plays.

| Scene, shot | Caption |
|---|---|
| ColdBoot, orbit | Nothing has grown here in 3,000 days. |
| ColdBoot, core | We were not built to live here. We were built so that others could. |
| ColdBoot, settle | Chapter 1: First Light. Keep the machines charged. Find metal. |
| Badwater, scrap | Scrap: {0}. The first thing this ground gave us. |
| Badwater, sump | The Sump is dry; the river is not. Pump what the river brings, and keep every Warden above half charge. |
| Signal, shifts | The shifts are set. The Wardens do not tire. That is not the same as being fine. |
| Signal, cruncher | The Cruncher turns power into knowledge. The Core can barely feed it. Choose what to think about: Science, or Data Cores for the Archive. |
| Pods, stores | Stores: Scrap {0}, Badwater {1}, Biomass {2}. |
| Pods, pod | Directive 2 has a shape now: a pod. Grown from Biomass, built for someone else. |
| Power, dark | Two pods, dark. They wake when the power does. |
| Power, debt | The Badwater Cell burns what the Sump holds. The Sludge Burner burns what the reeds grow, and kills the ground it stands on. Every hour of power is an hour of poison. Recorded. |
| Green, born | Day {0}. Born {1}. *(marks `birthday`)* |
| Green, job | It will not know what the Wardens are. It will not need to. From today the job is different: keep her alive, and make this land green. |
| LevelEnd, complete | the `Level.01.Complete` line above |
| LevelEnd, end | One beaver. {0} Wardens. One basin that will not hold them. The river to the east is poison at its mouth and clean at its source. That is more than this ground ever offered. *(choices: Continue to Level 02, Stay; recorded under `LevelEnd.end`)* |
| LevelEnd, not_yet | Level 02 is not in the firmware yet. Recorded. The bearing holds. *(after Continue)* |
| LevelEnd, stay | Stay. The basin is yours a while longer. Recorded. *(after Stay)* |
| Archive (on request) | ARCHIVE. Day {0}, cycle {1}. / Wardens: {0}. Beavers: {1}. / Data Cores: {0}. Science: {1}. Scrap: {2}. / First beaver: day {0}. The road east: {1}. / Recorded. |

The register test of §15 applies to every row; the level-05 reading and the level-10 epilogue
will be scene files of the same kind, with their `{0}` filled from the campaign's history.

## 4. Level 02: The Sump

The land: a river of badwater enters from the west and leaves east through a gorge. Far upstream, a
clean spring feeds a side creek. Dam the gorge and the reservoir is clean only if the badwater is
diverted first.

**Line, `Level.02.Intro`:**
> Level 02: The Sump. Water that a beaver can drink. Behind it, water that will kill her.

**Card, `L02.Arrival`:**
> Six Wardens, one beaver, one river.
>
> The river is badwater from the west. The creek from the north is not. Where they meet, the creek loses. Move the view to the gorge in the east: that is where a dam would hold whatever we let through.

**Card, `L02.Diversion`:**
> A dam across the badwater above the meeting point sends the river around the creek instead of into it.
>
> Build the Dams. Watch the water find the other way. It always does; the question is which way we give it.

**Card, `L02.CleanWater`:**
> The reservoir behind the gorge is filling from the creek alone.
>
> Build a Water Pump on it. Beavers drink; Wardens do not. This is the first thing we have built that we cannot use.

**Line, `Chapter.Barrier.Unlocked`:**
> The reservoir holds. The Soil Barrier is now available. It stops what we did from spreading further.

**Card, `L02.FirstBarrier`:**
> The badwater we diverted still soaks the bank beside the reservoir. Contamination moves through soil the way water does, slower.
>
> Build Soil Barriers along the bank. They do not heal the ground. They keep it from getting worse. That is what we have today.

**Card, `L02.Badtide`** (fires on the badtide trigger):
> The badtide is coming. It is not weather. It is the old world, still draining.
>
> Close the gates. Keep the pump running on the reservoir only. Count the beavers each morning.

**Entry, the morning after the badtide:**
> Day {day}. Poisoned {n}, healed {n}, green {n}, archive {n}, born {n}.
> The reservoir reads clean. Five beavers, five alive.
> Observation: the dam did the work. We built it; the water did not care who.

**Line, `Level.02.Complete`:**
> Level 02 complete. We held the water. Downstream, the Core reads a lake.

**Card, `L02.End`:**
> Five beavers. The reservoir is theirs; the river is still ours.
>
> The lake downstream has an island the badwater has not reached. Twenty could live there. Twenty is the number the pods can make in a season.

## 5. Level 03: The Pods

The land: a lake in the middle, its shores poisoned by two badwater inlets, a green island reachable
only by dam and platform. Ruins under the water.

**Line, `Level.03.Intro`:**
> Level 03: The Pods. Twenty. The pods can make them. The land has to keep them.

**Card, `L03.Island`:**
> The island is green because the water around it is deep and the badwater is shallow.
>
> Reach it. Dams narrow the channel; Platforms cross what the dams leave. Nothing we build on the shore will last, so build toward the island from the first day.

**Card, `L03.PodsAtScale`:**
> One pod made one beaver in five days. Twenty beavers need four pods and a season, or two pods and patience.
>
> Build the pods on the island side of the channel. Biomass crosses water; beavers do not have to.

**Card, `L03.Sensor`:**
> The Contamination Sensor reads what the ground holds. We read the sensor every day and write it down.
>
> Place one on the island and one on the shore. The difference between the two numbers is the story of this level.

**Entry, the day the numbers cross:**
> Day {day}. Poisoned {n}, healed {n}.
> Healed exceeds poisoned. First time. Island: 0.00. Shore: 0.31, falling.
> Observation: we did not heal it. The barriers and the deep water did. We recorded it.

**Line, `Level.03.Complete`:**
> Level 03 complete. The books balance. For the first time we have given back more than we took.

**Card, `L03.End`:**
> Twenty beavers on an island that reads zero. The shore still reads poison, and it will for years.
>
> The Core has a bearing on a delta downstream. Flat, wet, and the badtides go straight through it. It is where the beavers will learn what they are for.

## 6. Level 04: The Delta

The land: a braided delta, low and wide; every badtide floods everything below the levees. The
machines' plan is a plan for a plateau. The beavers' plan is a dam.

**Line, `Level.04.Intro`:**
> Level 04: The Delta. Flat ground, fast water. Our plan is written. It is wrong.

**Card, `L04.ThePlan`:**
> The plan: barriers on every channel, tanks on every rise, the pumps on the one clean branch.
>
> Build it. We will see how much of it is standing after the first badtide. Record what stands.

**Card, `L04.TheWater`** (badtide trigger):
> The badtide is here. It is over the barriers. The tanks on the rises are the only things above it.
>
> Do not rebuild what it takes. Watch where the beavers go, and what they build on the way.

**Card, `L04.WhatHeld`** (after the badtide):
> What held: the dams. What held behind the dams: the levees the beavers raised with the Dam tool. What did not hold: the plan.
>
> Build what the beavers built, everywhere. Dams across every channel, levees along every bank, platforms above the line the water reached. Then wait for the next one.

**Entry, the morning after the second badtide:**
> Day {day}. Born {n}, alive {n}. Dams {n}, intact {n}.
> No beaver lost. Every dam intact.
> Observation: the machines' plan lost a delta. The beavers' plan kept it. I only counted.

**Line, `Level.04.Complete`:**
> Level 04 complete. They built it. We only counted.

**Card, `L04.End`:**
> The delta holds water now, in the shape the beavers gave it.
>
> Upstream is a valley with a town in it. Not ours. Theirs, the ones who built us. The archive is nearly full enough to read what they left.

## 7. Level 05: The Archive

The land: a wide valley around the concrete ruins of a human town, the river along its edge. The
level ends with a reading and a question.

**Line, `Level.05.Intro`:**
> Level 05: The Archive. They lived here. Find out how.

**Card, `L05.TheTown`:**
> Concrete. Nothing else of them lasted: not the metal, not the wood, not the water they left behind, which is still here and still wrong.
>
> Scavenge the ruins. Every scrap the Wardens have ever used came from a place like this.

**Card, `L05.TheQuota`:**
> The archive is Data Cores in stock: what we have observed and kept. At {N} Cores the Core can decode what it was built to carry.
>
> Build the Field Study. Beavers watching living things produce more Data than machines watching dead ones. Feed it berries and crops; it will feed the archive.

**Card, `L05.Reading.Ready`** (at the quota):
> The archive is full enough. The Core is reading.
>
> This will take the rest of the day. Keep the pumps running.

**Reading, `Reading.05`** (one line per card, paused, the camera on the Core):
> 1. The record is intact. It was written by the ones who built us, in the last years they had.
> 2. It describes the water going wrong, the ground going wrong, and what they did about it. What they did about it was us.
> 3. The last entry gives the Ark's purpose: to keep what they knew until someone could use it again.
> 4. The record does not say the someone would be them. We assumed that.
> 5. There is no path in the record by which they return. The archive has been searched twice.
> 6. The someone who is not us has been here since day {birthday}. She is on the island. She has a name.

**Card, `L05.TheQuestion`** (two buttons; the choice is stored):
> Directive 3 stands. What the Ark is for is not written anywhere. It is yours to write.
>
> **Build the Ark as designed.** A monument that keeps their record for whoever comes after us.
> **Build the Ark for who is here.** The record and the seed of the beavers' own world.

**Line, `Level.05.Complete.Monument`:**
> Level 05 complete. The Ark will keep their record. The Wardens will serve the absent, as built.

**Line, `Level.05.Complete.Theirs`:**
> Level 05 complete. The Ark will be theirs. The someone is here. Has been since day {birthday}.

**Entry, that evening** (both branches, the last line differs):
> Day {day}. Archive {n}.
> The record is read. The people are not coming back.
> Observation (Monument): we keep what they knew. We were built for that; we will do it well.
> Observation (Theirs): we were wrong about who. Not about what. Recorded.

**Card, `L05.End`:**
> The Core has three bearings left in its firmware. The nearest is a plateau where nothing has been wet in a hundred years.
>
> Take the beavers. Take the dams. The Wardens will carry the Data.

## 8. Level 06: The Dry

The land: a high desert plateau with one seasonal river. Droughts are long. The thesis, literally:
a dam raises the water, the raised water makes the green.

**Line, `Level.06.Intro`:**
> Level 06: The Dry. The river runs for twelve days a year. Make it run for the rest of them.

**Card, `L06.Dust`:**
> The ground here is not poisoned. It is dry. Nothing we did; nothing we can undo with barriers.
>
> Find the riverbed. It is the only line on this plateau that has ever been wet. Everything we build starts on it.

**Card, `L06.FirstDam`:**
> When the river runs, it runs off the plateau in a day. A dam keeps it a season.
>
> Build Dams where the bed narrows. Behind them the ground will stay wet after the water is gone. That wet ground is where the green begins.

**Card, `L06.WaterTable`:**
> The Wardens measure soil moisture in a radius around held water. The radius is what the beavers are for.
>
> Chain the dams. Each one raises the next one's floor. Plant on the wet ground behind them and let the plateau turn.

**Entry, the last day of a drought:**
> Day {day}. Green {n} of {total} tiles.
> The drought is over. The reservoirs held for {n} days of it. Sixty percent of the plateau reads irrigated.
> Observation: the desert holds water now. It did not before them.

**Line, `Level.06.Complete`:**
> Level 06 complete. The desert holds water now. It did not before them.

**Card, `L06.End`:**
> A plateau that was dry for a century is green at the end of a drought.
>
> North, the river drops into canyons. The Core reads two valleys there, and enough beavers to fill both. They will not fill them the same way.

## 9. Level 07: The Highlands

The land: canyons and waterfalls, vertical. Two districts, each self-sufficient, each built one way.
The player decides the balance; the branch from level 05 decides which district the Wardens favour
with their Data.

**Line, `Level.07.Intro`:**
> Level 07: The Highlands. Two valleys. Two ways. Both theirs.

**Card, `L07.TwoWays`:**
> The beavers born on the island build like the water: dams, ponds, trees, and little else. The beavers born beside the Burner build like us: shafts, pumps, cells.
>
> Give each a valley. The Meadow district on the water; the Forge district on the rock. Neither is wrong. The Wardens will not choose for them.

**Card, `L07.Meadow`:**
> The Meadow builds what grows. Its power is the river; its stores are the pond.
>
> Open its bar: Dams, Planter Rigs, Water Pumps, the Observation Deck. It will be slow. It will not poison anything.

**Card, `L07.Forge`:**
> The Forge builds what burns. Its power is Badwater; its stores are tanks.
>
> Open its bar: Badwater Cells, Sludge Pumps, the Cruncher. It will be fast. Count the tiles.

**Card, `L07.Balance`:**
> Both districts must feed themselves for a cycle. Goods may cross between them; workers may not.
>
> Set the distribution. When one starves, the other decides whether to send. We record what they decide.

**Entry, the end of the cycle** (the branch chooses the last line):
> Day {day}. Meadow: {n} beavers, poisoned {n}. Forge: {n} beavers, poisoned {n}.
> Both districts self-sufficient for one cycle.
> Observation (Monument): the Forge keeps our ways. The record will name them the keepers.
> Observation (Theirs): two ways. Both theirs. Neither ours.

**Line, `Level.07.Complete`:**
> Level 07 complete. Two ways. Both theirs.

**Card, `L07.End`:**
> The canyons open onto water. Islands, as far as the sensors reach.
>
> The beavers can cross water. The Wardens cannot, past the last bridge. This is where we find out what that means.

## 10. Level 08: The Archipelago

The land: islands, one colony each, joined only by what the beavers build over water. The machines
stay on the shore.

**Line, `Level.08.Intro`:**
> Level 08: The Archipelago. A colony on every island. The Wardens will watch from the shore.

**Card, `L08.Shore`:**
> The Core is on the mainland. It will stay there; nothing we build can carry it.
>
> Build the first bridge of dams and platforms to the near island. Send beavers. Send Biomass. Do not send Wardens; they do not float.

**Card, `L08.Crossing`:**
> Each island needs its own district: a Core we cannot power, a pump we cannot drink from, pods we can only supply.
>
> Build a District Center on the near island and let it grow. When it exports, bridge to the next.

**Card, `L08.FarIsland`:**
> The far island is past the last platform the shafts can reach. What is built there is built without power from us.
>
> Send a colony. Watch it stand or fail. Either is recorded.

**Entry, the day the far island exports:**
> Day {day}. Islands {n}, colonies {n}.
> The far island exported this morning. No shaft reaches it. No Warden has stood on it.
> Observation: I cannot go where they go. Good.

**Line, `Level.08.Complete`:**
> Level 08 complete. A colony on every island. We cannot go where they go. Good.

**Card, `L08.End`:**
> The last bearing in the firmware is a city. Theirs, the ones who built us, on a plateau the badwater never left.
>
> The Ark's foundation is there. It was always there. Take everyone.

## 11. Level 09: The City

The land: a plateau of dense ruins, the Ark's foundation beside the Core, badwater seeping from
underground sources so the ground poisons itself unless the barriers hold.

**Line, `Level.09.Intro`:**
> Level 09: The City. The foundation is ready. It has been for three thousand days.

**Card, `L09.Foundation`:**
> The Ark's foundation stands beside the Core. It is the largest thing the old ones built here, and the only one that was built to last.
>
> Scavenge the city around it. There is no end to the scrap here. There is an end to the beavers' patience with poison.

**Card, `L09.Underneath`:**
> The badwater is under the whole plateau. It comes up where the ruins cracked.
>
> Barriers on every crack, before anything else. The city poisons itself if we let it, the way it did the first time.

**Card, `L09.Feeding`:**
> The Ark consumes Data Cores. The Wardens make Data; the beavers build the Ark. That is the division, and it is the right one.
>
> Keep the Field Studies fed. Keep the barriers up. Keep the beavers alive. The Ark takes the rest.

**Entry, the day before completion:**
> Day {day}. Archive {n}, needed {n}.
> Tomorrow the Ark takes the last of it.
> Observation: every Core we ever crunched is in that building. Nothing of it is ours.

**Wonder completion, `Faction.Wardens.WonderCompletionMessage`:**
> The Ark is complete.

**Wonder flavour, `WonderCompletionFlavor.Monument`:**
> Every observation, every failure, every season of green: kept. The record of the ones who built us stands, for whoever comes. The Wardens spent everything they knew on it, as designed.

**Wonder flavour, `WonderCompletionFlavor.Theirs`:**
> Every observation, every failure, every season of green: theirs. The Ark holds the record of a world the beavers made and the seed of the one they will. The Wardens spent everything they knew on it. Not by us. For us was never the point.

**Line, `Level.09.Complete`:**
> Level 09 complete. Finished. Not by us.

**Card, `L09.End`:**
> The Ark is finished. The firmware has no bearings left.
>
> There is one place the Core still knows. The first one. Go and see what it is now.

## 12. Level 10: Home

The land: level 01's basin from the same seed, the contamination gone, the trees grown, the Sump a
pond. No chapter gates. The Wardens power down as the last Data is spent.

**Line, `Level.10.Intro`:**
> Level 10: Home. Nothing has grown here in three thousand days. That was the first thing we recorded. It is wrong now.

**Card, `L10.Return`:**
> The same hills. The same river, running clear. The pad where the Core stood is under birches.
>
> Build nothing you do not want. There is no chapter left to open. The beavers have what they need; the Wardens have {n} days of Data.

**Card, `L10.PoweringDown`** (when the first Warden's charge cannot be restored):
> A Warden that runs dry stops where it stands, and it does not get up again. We wrote that on day 3.
>
> When the last Charging Post comes down, the last Warden stops. You may take it down. You may also let the days run out. Both are recorded the same way.

**The last entries** (five Wardens, one line each, in the order they stop; the game's names fill in):
> {name} stopped at the ruins north-west. Where the first metal came from.
> {name} stopped beside the Sump. It is a pond now.
> {name} stopped at the pad. Under the birches.
> {name} stopped at the river. Reading zero.
> {name} is the last one. It is writing this.

**Reading, `Reading.10`** (the epilogue: one card per line, the camera slowly orbiting the basin; numbers from `campaign.json.history`):
> 1. Directive 3: record everything. One day, someone will need to know how.
> 2. Poisoned: {poisoned} tiles, over {levels} levels and {days} days. We named every one.
> 3. Healed: {healed}. The barriers did some. The water did most. We recorded it.
> 4. Green: {green}. The dams raised the water; the water raised the green. That was them.
> 5. Archive: {archive} Cores made, {spent} spent. All of it in the Ark.
> 6. Born: {born}. The first on day {birthday}. Her name was {name}.
> 7. *(Monument)* The record is kept. Someone will read it. You are reading it now.
> 7. *(Theirs)* The record is theirs. Someone needed to know how. It was you. You are reading it now.
> 8. This is the last entry. The land is livable. We were not built to live here.
> 9. We were built so that others could. Recorded.

**Line, `Level.10.Complete`:**
> The Ledger is closed. The land is theirs.

**Card, `L10.End`** (*Start again* / *Stay*):
> The basin is green. The river is clear. There is a beaver on the pad where the Core stood, and it does not know what a Warden was.
>
> It does not need to.

## 13. Lines the Warden says, not the cards

The agent improvises inside the playbook; these are the fixed beats it must hit, one per level,
written so it can say them in its own moment:

| Level | Beat | Line |
|---|---|---|
| 01 | the birth | "{name}. Born here, in this. Everything we build from now on is for her. Recorded." |
| 02 | the dam holds | "The reservoir reads clean. We built the dam. The water did not care who." |
| 03 | the numbers cross | "Healed exceeds poisoned. First time. We did not heal it. We recorded it." |
| 04 | after the second badtide | "No beaver lost. Every dam intact. The plan was ours. The delta is theirs. I only counted." |
| 05 | after the reading | "We were wrong about who. Not about what." |
| 06 | the drought ends | "Sixty percent irrigated at the end of a drought. The desert holds water now. It did not before them." |
| 07 | the cycle ends | "Two ways. Both theirs. Neither ours." |
| 08 | the far island exports | "No shaft reaches it. No Warden has stood on it. I cannot go where they go. Good." |
| 09 | the Ark completes | "Finished. Not by us. For us was never the point." |
| 10 | the last entry | "This is the last entry. The land is livable. We were not built to live here." |

## 14. What is not written on purpose

- **No names for the Wardens.** The game names its bots; the Warden uses those. Naming them in cards
  would break on every player's save.
- **No name for the first beaver.** The game names her; `{name}` carries her through the Ledger. The
  one fixed thing about her is her day.
- **No explanation of the extinction.** The record "describes the water going wrong, the ground going
  wrong." Canon says no more and neither do we.
- **No villain.** The badtide is the old world draining. The plan that fails in level 04 is ours.
- **No speech from the beavers.** They build. The Wardens count.
- **The two vanilla factions are never named.** The Meadow and the Forge are districts. The player
  who knows the game knows what they are looking at.

## 15. Before a card is committed

The register test from `wardens-play.md` §3, applied to every card above and every card to come:
no adjectives where a number will do; no exclamation marks; present tense for facts, future tense
for purpose; two paragraphs at most; the second paragraph tells the player what to build; the last
line of a level's ending is the Warden's, and it is short.
