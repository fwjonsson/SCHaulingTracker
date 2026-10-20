# Hauling Session Tracker

Reads Star Citizen `Game.log` files and fills in your hauling session sheet.
Only **Station to Ruin** hauling contracts are used. Ruin to Station missions are ignored.

## Build and run

You need the .NET 10 SDK (Windows).

    cd HaulingTracker
    dotnet run

or open `HaulingTracker.csproj` in Visual Studio and press F5.
To make a single exe: `dotnet publish -c Release -r win-x64 --self-contained false`.

## Use

1. **Options → Open Log File(s)…** to read old logs (pick several at once, for example logs split by a crash).
   Or **Options → Watch Live Game.log** while you play. The window updates every 1.5 seconds.
2. Press **Start Session** on the front page to begin a session (and **Stop Session** to end it). The drop-down lists your sessions. Missions outside every session are not shown.
3. Start and End times are filled from the log. Type over them if you want. **Use log times** resets them.
4. Rename each pickup location once (click the name). It is remembered for every future session.
5. Pick the worker for each location, write notes. Payouts can be changed in the reward table.
6. **Copy table for Google Sheets** copies the pickup table, ready to paste into the sheet.

Your data is saved in `%AppData%\HaulingTracker\data.json`. The first time the program starts each day it also keeps a dated copy (`data-2026-10-20.json`) in a `Backups` folder next to it, the newest 30 days (**Options → Open Backup Folder**). To go back, close the program and copy a backup over `data.json`. On the Debts tab, **Data File…** moves it to another folder, for example in OneDrive, so it follows you between PCs. Run the tracker on only one PC at a time then, because it rewrites the whole file on every change.

## What the log can and cannot tell

- Several missions with the same pickup are grouped into one row with a count.
- **From Ruin** (right of the location name) is the distance in Gm from Ruin Station to that pickup, taken from the Pyro distance table. It stays empty for a location the table does not know.
- The pickup table only shows missions that are not completed yet. While you still have open missions, completed ones are gone from it, so after you finish a batch of contracts and accept new ones you only see the new ones. When everything is completed and you have not accepted anything new, the finished locations stay on the table until you start adding new missions. Nothing else forgets them: the run board, both leaderboards, the totals (Batches Delivered, Missions Completed, aUEC) and **Copy table for Google Sheets** still cover the whole session. The worker and notes you typed for a location are kept and come back if the location shows up again.
- The Missions column shows missions still waiting / missions with cargo at that location, so 5 missions start as 5/5 and count down as you load. Yellow = partly loaded, green = all loaded.
  The log has no SCU numbers, so it only knows loaded, partly loaded or not loaded.
- Loaded and delivered times are shown on the run board. "Copy table for Google Sheets" still fills them in per location.
- If the log misses a final "completed" push, stops of a finished mission count as loaded.
- Not in the log: location names, SCU per location, rewards. These are typed by you.

## Runs, players and two computers

- The lower table lists every run. A run ends at each hand-in at Ruin, also a partial one.
- Rewards: completed 2, 3 and 4 stop contracts are counted from the log. Type the payout of each type and the totals, revenue and hourly rate calculate.
  Hand-ins less than 3 minutes apart count as one hand-in. Run #1 starts at the first accepted contract.
- Each Game.log belongs to one player (the character name in the log). The run board shows the runs of all loaded players side by side, the top table follows the player picked in the drop-down.
- **Options → Add Live Source…** adds the StarCitizen folder (the one holding LIVE and HOTFIX), also on another computer through a shared folder (UNC path). **Log Channel** in the same menu picks LIVE, HOTFIX or Auto (the log written most recently). All added sources are watched at once.
- When watching starts (and when the game starts a new Game.log) the logs in the `logbackups` folder that are less than 24 hours old are read first, so a session that was already going on, for example before a crash, continues.
- **Options → Sources…** lists every added live source, shows if it can be reached right now, and lets you remove one. Untick **Enabled** to switch a source off without losing its address; tick it again to use it later. A switched-off source is not read at all.
- Mission updates reach the whole party, so the logs of all accounts are merged and a shared mission counts once.
- **Options → Party…** opens a table where you type names and tick **Active**. Only active names show up in the Assigned Worker dropdown (someone already assigned to a location stays visible there).
- When all your missions are completed and you then accept new ones, the assigned workers are cleared (a clean slate for the new set). The old runs keep their workers, they are remembered per set, and the leaderboards do not change. Notes stay. Locations are assigned again automatically as soon as somebody picks up there.
- A location nobody is assigned to is assigned automatically to the player the logs show picking up there. You can change it, and bildog123 (no log) has to be assigned by hand.
- The run board makes runs per worker. The Assigned Worker of a location always decides who did a pickup there. Only for locations with nobody assigned, the logs are used: only the player who takes cargo out of a freight elevator has those lines in his own log, a few seconds before the mission update (needs that account's log).
- A run is leaving the base, collecting cargo from any number of missions and handing it in at Ruin. A hand-in ends only the run of the player who did it, never somebody else's run, whatever missions it was for. Who did it is read from the logs (the loading platform lines). If no log names anybody, it is credited to a worker without a log (like bildog123).
- Any pickup signal counts a stop as picked up. If you had to leave cargo, write it in Notes.
- The reward table counts every accepted 2, 3 and 4 stop mission. Payouts start at 1,268,250 (2 stops) and 1,691,000 (3 stops); the 4 stop payout (Master rank) starts empty, type it in.

- **Value** on the upper board is what the missions at that location are worth: every mission pays its reward split over its stops (reward / 2 for a 2 stop mission, reward / 3 for a 3 stop mission, reward / 4 for a 4 stop mission), times the stops at that location. It follows the payouts in the reward table.

## Runs, lost cargo and history

- **Lost Cargo** on a run is a drop-down: None, Little or All (All also marks the run as failed, see below). A run row is yellow while the run is on the way, green when it is completed with everything delivered, orange for Little lost and red for All lost.
- **Completed** is the time the run ended, at the hand-in at Ruin Station. It stays empty while the run is on the way. **Missions** is how many missions were handed in then. **Pickup Location & Time** lists where cargo was picked up and when. The time columns sit together after Missions: **Started**, **Completed** and **Run Time** (with seconds). A run starts at the previous hand-in, but never before the missions in it were accepted, so a worker's first run, or new missions after a long break, do not count the waiting as run time. For a run that is on the way, Run Time counts up from Started while you watch live.
- **Lost Cargo** is a drop-down: None, Little or All. **All** marks the run as failed: a run still on the way ends there, it does not count as a run, and its value is shown as lost. Pick None or Little again to bring it back.
- **History tab:** every session is saved with its date, missions, revenue and what each worker carried, so earlier sessions stay after their logs are gone.

## Layout

Every board has a title. At the top: **Hauling Tracker** (the pickup board) on the left, and on the right the contract table with the clean table (times, missions, revenue, hourly rate) below it. Under them the **Session Leaderboard** runs over the full width. **Cargo Runs** takes what is left of the window and scrolls, with the run that had the latest activity on top: a run that gets completed (handed in) jumps to the top, a run still on the way sits by its last pickup. Not sorted by run number. The pickup board and the leaderboard are always shown in full.

The program starts watching the logs by itself when it opens, and reads the backup logs of the last 24 hours first.

## Dark mode

**Options → Dark Mode** switches between the light and the dark look. The choice is saved in the data file and used the next time the program starts. The dark look covers all tabs, tables, buttons, dialogs and the title bar (Windows 10 or later).

## Trip Planner


**Distances and flying time.** The distances between the Pyro stations are calculated from the real positions of the stations in the game data (they match the distances you read in the game within 0.1 Gm). Pick each worker's **QT Drive** in the Party table (all 57 drives of the game data) and the planner estimates the flying time of every jump like the star map does: the drive speeds up with its stage 1 acceleration until the engage speed, then with its stage 2 acceleration up to the top speed, flies at the top speed and slows down the same way (a short jump never reaches the top speed). The spool time is not part of the time. The planner plans so the crew finishes at about the same time (a worker with a faster drive gets more of the flying). A run in the tables shows the time it really took, a planned trip shows ~ the estimate. Loading time is not included. Without a drive set, an Odyssey (S2) is assumed. The calculation was checked against SPViewer with a TS-2 (S3, 12 jumps from 1 to 150 Gm, all within 2 seconds) and an XL-1 (S2) and a VK-00 (S1) (1, 15 and 100 Gm, all within 1 second); the other drives of those sizes use the same formula. Size 4 drives (Allegro, Mauler) are only approximate: an Allegro is up to 10 seconds off.
The **Trip Planner** tab divides the open batches over the whole crew, so everybody flies as little as possible and you finish at about the same time.
- The crew is the active party. In **Options → Party…** tick **Active** for who flies and type each member's **Batch Capacity**: how many batches his ship holds per trip (for example 3 for a C2 and 2 for a smaller ship). It is saved per member.
- The tab has an **Overview** page with everybody and a page for every crew member. A page has a **map of Pyro** on the left and the tables on the right. On the map every planned trip is a line from Ruin through the stops and back (arrows show the direction); on the Overview every worker has his own colour and his next trip is bold, on a worker's page every trip has its own colour. Click a planned trip in a worker's table to make it bold on the map.
- A table lists the **runs so far** in this session, coloured like Cargo Runs (green = done, orange = some cargo lost, red = all lost, yellow = on the way), followed by the **planned trips** (**Next** is the one to fly now). A line shows the route in flying order, the batches (out of the ship's capacity for a planned trip), the distance in Gm and Gm per batch. A title above each table adds up the runs and the plan.
- The plan ignores who is assigned to a location now. A location can be split over several trips and several workers: take what fits and fill the last seats with a small pickup that is close by, or come back for the rest.
- The plan is the one where the longest worker total is as short as possible, with a smaller weight on the combined Gm. It tries many ways of dividing the batches, and the suggestion is always the same for the same situation. It follows the batches you pick up while the tab is open, or press **Plan Trips**.
- It uses the Pyro distance table and map positions, so a location that is not in it is left out and named under the summary. Only batches that are not picked up yet are planned.

## Leaderboards

The **Session Leaderboard** has one row per worker who started a cargo run in the session, and a total row, the highest earner on top (marked with a star on each side of the name and a gold row; the other names are indented to line up):
- **Avg Batches/Run** and **Avg Run Time**: averages over the completed runs (ended by a hand-in, not marked as failed). Batches/run counts the batches picked up at each location, like the Batches column of the run board.
- **Distance Travelled**: the distance in Gm flown in the worker's completed runs. A run counts Ruin to the first pickup, between the pickups in the order they were made, and back to Ruin. The distances are a fixed table for the Pyro stations (`Distances.cs`: positions from a Pyro map, Ruin legs read in the game). A pickup location the table does not know (a station you named differently, or one that is not in the table) is skipped. Sessions saved before this column existed get their distance filled in when you open them. **Gm / Hour** is that distance divided by the session duration (the same hours as Run Value / Hour).
- **Completed Runs** and **Perfect Runs** (completed runs with lost cargo None).
- **Delivered Batches**: the batches of the worker's finished runs. Nothing counts (also not the earned aUEC) until the run is done.
- **Total Run Value**: the value of the batches in the worker's finished runs (reward / stops of each mission), counted as soon as the run is done. Run counts (Completed Runs, Perfect Runs, Delivered Batches) also count at once; a perfect run stays perfect whatever happens to the server afterwards.
- **Total Earned aUEC**: the part of the run value whose mission is completed. **Lost Value**: run value minus earned, shown once the session has been quiet for 6 hours (until then the rest is just queued). Failed or abandoned missions never pay. **Run Value/Hour** is the run value divided by the session duration, **Share of Run Value** is his part of the total run value. The History leaderboard has the same columns over all saved sessions.
- The clocks only run while you have a contract that is not completed: **Session Time**, the per-hour numbers that use it, and the **Run Time** of a run that is still on the way stop as soon as everything is completed (the session time freezes at the last activity).
- **Start Session / Stop Session** (top bar) is one button. Press **Start Session** (for example every morning), confirm with **Are you sure**, and the program reads the logs only from that moment until you press the same button again (now **Stop Session**, also with a confirmation). It can go on over several days, and the start is remembered when you close the program. Missions count for the session that was running when they ended; a mission that is still going belongs to the running session. The session clock does not count a stretch of an hour or more with no activity (the nights, or you walked away); it only stops counting, the session itself goes on.
- **Party** (top right, next to the times and contracts): type a name and press **Add** (or Enter) to put someone in the party, click a name and press **Remove** to take him out. **Active** is ticked while he is in the party, and **Since** shows when that last changed (it is saved with the time). **Batches** is how many batches his ship holds and **QT Drive** is the quantum drive in his ship (the Trip Planner uses both). You can type a new time in **Since** (like 12:30 or 19 Oct 22:15) if you forgot to tick him when he joined. Only active members are choices in Assigned Worker.
- **Debts follow the party in time.** When a mission pays out, its reward is split equally between whoever was active in the party at that moment, and everybody is owed the value of the batches he carried. So if Bileh joins, flies two runs and no mission pays out while he is in the party, you owe him for his runs. If a payout comes while he is there, his share of it is already counted as paid to him. Missions that paid out before this table started tracking times keep the old split (the whole session party); a **Session Party…** typed in the Debts tab still overrides everything for that session.
- The two boards on the left only show **active missions** (not completed yet). **Hauling Tracker** lists the pickup locations with cargo still to deliver; when every batch of a location is handed in, the location moves to **Finished Stops** below it (shown when there is something in it). When a mission is completed it leaves both boards. **Cargo Runs** at the bottom keeps every run since the session started, so it is the log.
- The start/end table shows **Session Time** (ticks every second while you watch live), **Batches Delivered** (finished runs / all batches), **Active Missions** (accepted, not completed yet), **Missions Completed** (exactly the number completed in the session), **Total aUEC Earned** (what completed missions actually paid) and **aUEC/Hour** (earned per session hour).

The **History** tab has the same table for **all saved sessions** (with an extra Sessions column). Under it a line shows the best session (most revenue) and the best aUEC/hour. It adds up what each session saved, so it keeps working after the logs are gone. Sessions saved by an older version have earnings but no run counts until their logs are opened once.

## Debts tab

- **Earnings:** every session saves how much each worker carried (the Value of the locations assigned to him). The table shows this session, all sessions and the net balance per worker.
- **Split:** the reward of a session is split equally between everybody in the party, but each one is owed the value he carried. So somebody who carried nothing owes his whole share to the others, and somebody who carried more than his share is owed the difference. Lost cargo changes nothing.
- **Session Party…:** by default the party of a session is everyone who carried something. Use this button to add someone who was in the party but carried nothing (or to remove someone).
- **Balance:** pick a party member and see the balance with each other member. + means they owe the selected member, − means the selected member owes them. Type over a number to correct it, your correction is kept.
- **Money transfers:** `You sent <name>: <amount> aUEC` and `<name> has sent you: <amount> aUEC` are read from the logs (a transfer seen in both accounts' logs counts once). The list shows the transfers of the selected member. Tick **Payment** on the ones that pay off a debt, other transfers (like buying a ship) are ignored.
- Balances add up over all saved sessions, so earlier money you settled by hand can be fixed with the typed corrections.

## Live Web View (let somebody watch)

**Options → Live Web View** switches on a small read-only web server inside the program. It shows all the tabs (Session, Trip Planner with its map and pages, Debts and History) with buttons on the page to switch between them, in the colours of your theme, and refreshes every few seconds. It only listens on your own computer (`http://localhost:8765`), and it can only show things: there is nothing to click or change, all changes are made in the program. The setting is remembered.
- To let somebody else watch, install `cloudflared` (`winget install Cloudflare.cloudflared`) and run `cloudflared tunnel --url http://localhost:8765` in a terminal (the program copies this command when you switch the web view on, so you can paste it with Ctrl+V). It prints an `https://...trycloudflare.com` address (no account needed). Send that address to whoever should watch. It changes every time you start the tunnel, and anybody with the address can look, so share it only with people you trust.
- The program and the tunnel have to be running on your computer for the page to work. Stop the tunnel (Ctrl+C) and the page is gone for everybody.

## ScApiTool (Star Citizen Wiki API)

A separate small program, `ScApiTool.exe` (in the same Releases download, `ScApiTool.zip`). It asks the [Star Citizen Wiki API](https://api.star-citizen.wiki) for game data (locations, missions, commodities, ships, items, and the API's own list of addresses) and saves the raw answers as `.json` files in a `ScApiData` folder next to it, with a `summary.txt` and a zip of everything.
- Double-click it with no arguments for the standard set. `--all` removes the page limit. Give it an address like `ScApiTool.exe /api/missions` to download anything else the API has.
- **UEX** (uexcorp.space): create an app on the UEX site (My Apps) and copy its access token. Put the token alone on the first line of a file called `uex-token.txt` next to `ScApiTool.exe` (or set the environment variable `UEX_TOKEN`). The tool then also downloads the UEX places, terminals, commodities and prices, saved as `uex-....json`. `ScApiTool.exe --uex` does only UEX, `ScApiTool.exe uex:/terminals` any other UEX address. The token is a secret: it is sent only to the UEX site and is never written to the results, so the zip is safe to hand over. Never put the token in the repository.
- Pages are asked for by number, so a list is never downloaded twice (the API's own "next" link repeated a page and filled the mission list with the same 270 missions).
- The data comes from fan sites that are not affiliated with Cloud Imperium Games. Credit api.star-citizen.wiki and uexcorp.space if you publish anything made from it.
