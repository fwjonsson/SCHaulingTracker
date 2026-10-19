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

1. **Open log file(s)** to read old logs (pick several at once, for example logs split by a crash).
   Or **Watch Live Game.log** while you play. The window updates every 1.5 seconds.
2. Pick the session in the drop-down. Missions less than 30 minutes apart form one session.
3. Start and End times are filled from the log. Type over them if you want. **Use log times** resets them.
4. Rename each pickup location once (click the name). It is remembered for every future session.
5. Pick the worker for each location, tick Partial pickup if needed, write notes. Payouts can be changed in the reward table.
6. **Copy table for Google Sheets** copies the pickup table, ready to paste into the sheet.

Your data is saved in `%AppData%\HaulingTracker\data.json`. On the Debts tab, **Data File…** moves it to another folder, for example in OneDrive, so it follows you between PCs. Run the tracker on only one PC at a time then, because it rewrites the whole file on every change.

## What the log can and cannot tell

- Several missions with the same pickup are grouped into one row with a count.
- The Missions column shows missions still waiting / missions with cargo at that location, so 5 missions start as 5/5 and count down as you load. Yellow = partly loaded, green = all loaded.
  The log has no SCU numbers, so it only knows loaded, partly loaded or not loaded.
- Loaded and delivered times are shown on the run board. "Copy table for Google Sheets" still fills them in per location.
- If the log misses a final "completed" push, stops of a finished mission count as loaded.
- Not in the log: location names, SCU per location, rewards. These are typed by you.

## Runs, players and two computers

- The lower table lists every run. A run ends at each hand-in at Ruin, also a partial one.
- Rewards: completed 2 stop and 3 stop contracts are counted from the log. Type the payout of each type and the totals, revenue and hourly rate calculate.
  Hand-ins less than 3 minutes apart count as one hand-in. Run #1 starts at the first accepted contract.
- Each Game.log belongs to one player (the character name in the log). The run board shows the runs of all loaded players side by side, the top table follows the player picked in the drop-down.
- **Add live source…** adds the StarCitizen folder (the one holding LIVE and HOTFIX), also on another computer through a shared folder (UNC path). The drop-down next to it picks LIVE, HOTFIX or Auto (the log written most recently). All added sources are watched at once.
- When watching starts (and when the game starts a new Game.log) the logs in the `logbackups` folder that are less than 24 hours old are read first, so a session that was already going on, for example before a crash, continues.
- **Sources…** lists every added live source, shows if it can be reached right now, and lets you remove one.
- Mission updates reach the whole party, so the logs of all accounts are merged and a shared mission counts once.
- **Party…** is the list of names in the Assigned Worker dropdown.
- A location nobody is assigned to is assigned automatically to the player the logs show picking up there. You can change it, and bildog123 (no log) has to be assigned by hand.
- The run board makes runs per worker. The Assigned Worker of a location always decides who did a pickup there. Only for locations with nobody assigned, the logs are used: only the player who takes cargo out of a freight elevator has those lines in his own log, a few seconds before the mission update (needs that account's log).
- A run is leaving the base, collecting cargo from any number of missions and handing it in at Ruin. A hand-in ends only the run of the player who did it, never somebody else's run, whatever missions it was for. Who did it is read from the logs (the loading platform lines). If no log names anybody, it is credited to a worker without a log (like bildog123).
- Any pickup signal counts a stop as picked up. Tick **Partial Pickup** yourself if you had to leave cargo (the cell turns yellow) and write what you left in Notes.
- **Lost Cargo** on a run is set by hand. It does not change the revenue.
- The reward table counts every accepted 2 and 3 stop mission, payouts start at 1,268,250 and 1,691,000.

- **Value** on the upper board is what the missions at that location are worth: every mission pays its reward split over its stops (reward / 2 for a 2 stop mission, reward / 3 for a 3 stop mission), times the stops at that location. It follows the payouts in the reward table.

## Runs, lost cargo and history

- **Lost Cargo** on a run is a drop-down: None, Little or All. It is only for your own notes and never changes who owes what. A run row is yellow while the run is on the way, green when it is completed with everything delivered, orange for Little lost and red for All lost.
- **Completed** is the time the run ended, at the hand-in at Ruin Station. It stays empty while the run is on the way. **Missions** is how many missions were handed in then. **Pickup Location & Time** lists where cargo was picked up and when. The time columns sit together after Missions: **Started**, **Completed** and **Duration**. For a run that is on the way, Duration counts up from Started while you watch live.
- **End Run** (last column) is a button that ends the run of that worker by hand, for example when all cargo was lost. You get an "are you sure" box. On a completed run the button is greyed out. A run ended by hand shows **Undo End** to bring it back.
- **History tab:** every session is saved with its date, missions, revenue and what each worker carried, so earlier sessions stay after their logs are gone.

## Layout

Every board has a title. At the top: **Hauling Tracker** (the pickup board) on the left, and on the right the contract table with the clean table (times, missions, revenue, hourly rate) below it. Under them the **Session Leaderboard** runs over the full width. **Cargo Runs** takes what is left of the window and scrolls, with the newest runs on top. The pickup board and the leaderboard are always shown in full.

The program starts watching the logs by itself when it opens, and reads the backup logs of the last 24 hours first.

## Dark mode

The **Dark Mode** button in the toolbar switches between the light and the dark look. The choice is saved in the data file and used the next time the program starts. The dark look covers all tabs, tables, buttons, dialogs and the title bar (Windows 10 or later).

## Leaderboards

The **Session Leaderboard** has one row per worker who started a cargo run in the session, and a total row, the highest earner on top (marked with a star on each side of the name and a gold row; the other names are indented to line up):
- **Avg Batches/Run** and **Avg Run Time**: averages over the completed runs (ended by a hand-in, not ended by hand). Batches/run counts the batches picked up at each location, like the Batches column of the run board.
- **Completed Runs** and **Perfect Runs** (completed runs with lost cargo None).
- **Batches Delivered**: the batches of the worker's finished runs. **Total Run Value** and **Run Value/Hour** are the values of the pickup locations of the finished runs. The real income is the **Total Revenue** (completed missions) in the time table. The History tab counts completed missions only.
- **Earned aUEC**: the value of the locations assigned to him this session. **Share of value** is his part of the value carried by everyone this session. **aUEC/hour** is that divided by the session duration (Start Time to End Time).

The **History** tab has the same table for **all saved sessions** (with an extra Sessions column). Under it a line shows the best session (most revenue) and the best aUEC/hour. It adds up what each session saved, so it keeps working after the logs are gone. Sessions saved by an older version have earnings but no run counts until their logs are opened once.

## Debts tab

- **Earnings:** every session saves how much each worker carried (the Value of the locations assigned to him). The table shows this session, all sessions and the net balance per worker.
- **Split:** the reward of a session is split equally between everybody in the party, but each one is owed the value he carried. So somebody who carried nothing owes his whole share to the others, and somebody who carried more than his share is owed the difference. Lost cargo changes nothing.
- **Session Party…:** by default the party of a session is everyone who carried something. Use this button to add someone who was in the party but carried nothing (or to remove someone).
- **Balance:** pick a party member and see the balance with each other member. + means they owe the selected member, − means the selected member owes them. Type over a number to correct it, your correction is kept.
- **Money transfers:** `You sent <name>: <amount> aUEC` and `<name> has sent you: <amount> aUEC` are read from the logs (a transfer seen in both accounts' logs counts once). The list shows the transfers of the selected member. Tick **Payment** on the ones that pay off a debt, other transfers (like buying a ship) are ignored.
- Balances add up over all saved sessions, so earlier money you settled by hand can be fixed with the typed corrections.
