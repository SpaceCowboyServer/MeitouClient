# Contract and task design: Anomaly reference

**Status: design research (2026-10-27).** Source: S.T.A.L.K.E.R. Anomaly 1.5.3 unpacked gamedata
(Tosox mirror, commit `6187cf7131134ca196feb3e1ecde7cded17ffb0b`), files
`gamedata/scripts/{task_manager,task_objects,axr_task_manager,dialogs,dialog_manager,tasks_bounty}.script`
and `gamedata/configs/misc/task/{task_manager,tm_dynamic}.ltx`. Claims are **Observed** (read from those files,
not runtime-tested). Concepts only; no Anomaly code or content is copied.

This document records what is worth borrowing for Meitou's bounty/contract layer. It is not a claim that
Meitou should reproduce Anomaly's engine or data format.

## What Anomaly separates

| Layer | Anomaly |
|---|---|
| Template | Task sections in `tm_*.ltx`: title/description, icon, priority, target and status functors, condlists, rewards, failure/cancel effects, repeat timeout, giver restrictions. |
| Instance | `CGeneralTask` holds id, stage, status, current title/description/target, giver id, timeout and reset time. |
| Generator | Per-NPC offer cache, availability roll, one job at a time with a "next" action, skip list, active-task limit, offered-task lifetime. |
| Lifecycle | offered → accepted → objective complete → turn-in, or fail/cancel/reverse; repeatable tasks return after a cooldown. |
| Presentation | Journal entry, map marker, update notification, giver conversation, money/item/stash/goodwill rewards. |

The important split is template vs instance: target NPCs, generated text, deadlines and rewards belong to the
instance; the task family and its rules belong to the template.

## The bounty loop (Observed)

- `task_manager.give_task` creates a task instance from an LTX section and records its giver. The template supplies
  `stage_complete`, target selection and status checks.
- `setup_bounty_task` chooses a random living simulation stalker from an allowed faction list. It excludes story
  objects, squads assigned to base smart terrains and blacklisted maps; it can prefer enemies over neutrals and
  marks the target squad for offline combat.
- `task_status_functor.bounty_task` sets `stage = 1` when the target dies. With `stage_complete = 1` the task is
  **finished but not paid**. The giver's completion dialogue calls `set_task_completed`, which runs `on_complete`,
  gives the reward and removes or cools down the task.
- Failure is data-driven: for bounty templates `!task_giver_alive(...) fail`; missing/invalid target also fails.
  `on_fail` and `on_cancel` run effects such as faction-goodwill loss.
- Rewards are condlists, typically random money, random items, a stash and faction goodwill. Rewards are given on
  completion, not on the killing blow.
- Bounty templates use `repeat_timeout = 21600` and are repeatable. The generic offer path stores a job for an NPC
  for a short time (`5400`), rolls a 75% chance to have a job, and simulation NPCs get an extra no-job roll.
- Giver conversations offer one job at a time, with Accept / Next / Leave. Finished tasks get their own turn-in
  conversation; ongoing tasks can be cancelled when `can_cancel` allows it. The default active-task limit per NPC
  comes from the gameplay option `max_tasks` (default 2).
- The journal uses title, description, icon, priority and map location; target changes update the journal and emit
  notifications.

## What Meitou already has

- Giver-scoped offers, a three-slot framework, turn-in and `ReadyToTurnIn`.
- Contract deadlines (`DeadlineHours`), `Failed`/`Expired`, saved failure reasons and giver-death failure.
- Ready/completed/failed/expired events and a journal view of open giver contracts with a Faction-tab detail panel.
- `WorldDayChanged` for daily refresh.
- Dynamic dialogue choices at the first conversation page.
- A reusable `frontier.contracts` foundation plugin with `IContractFramework`; `frontier.bounties` is now a content pack
  depending on the `frontier.contracts@1` capability.

The current sample replaces finished slots individually each day. That is a reasonable Meitou-specific cooldown;
Anomaly's `repeat_timeout` is the same idea expressed in game-time units. A ready contract does not expire; only an
unfinished accepted contract runs out of time.

## Useful changes, in priority order

### 1. Harden the contract lifecycle

Add failure and expiry to the core board, not just the sample:

- `BountyState.Failed` and an optional `BountyState.Expired`, with a saved reason.
- `ExpiresAtGameHours` and `AcceptedAtGameHours` on the contract instance; check deadlines in the board.
- `BountyReady`, `BountyFailed` and `BountyExpired` events; the existing `BountyCompleted` stays turn-in only.
- Fail a giver contract if its giver dies; allow cancel only when the template permits it.
- Show the deadline and progress in the journal/contract view.

Anomaly's split between "target dead" and "paid on turn-in" is the same split Meitou already has. Filling in
failure and expiry is low-risk and makes the three-slot loop meaningful.

### 2. Add a contract journal and detail page

Giver contracts are currently hidden from the Faction tab. That is correct for acquisition, but the player still
needs a place to see active/ready jobs, giver, progress, deadline and reward after walking away from the bar.

The dialogue also needs a detail step. Current `IDialogue` can only append choices to the first page; it cannot show
"Job: X / target / location / reward / Accept / Next / Leave" as a sub-page. Add a small dialogue-page abstraction
or let an option open a host-rendered contract detail panel. Anomaly's one-job-at-a-time + Next flow is a good
reference, but Meitou can keep its three-offer list and open details per offer.

### 3. Generate contracts from templates

Move from three hardcoded sample definitions to a pool:

- Template tags: giver role, town/faction, target selector, weight, cooldown, active limit, reward range.
- Per-giver offer slate: fill slots up to the limit from eligible templates; keep a stored offer for a while so the
  list does not re-roll on every conversation.
- Target selection belongs to the offer/instance: faction, nearby roaming squad, individual non-unique NPC, animal
  species or spawned camp. Exclude unique/story characters and already-targeted NPCs.
- Anomaly's `setup_bounty_task` is a useful checklist: allowed factions, relation bucket, map restrictions,
  non-story target, no base-targeting squad, and a cached target pool.

For Meitou, expose a `Character` target through the stable API (the internal model already has it) and select a
marked non-unique target at offer time. Keep the existing faction-count contracts as the simple fallback.

### 4. Add availability requirements and consequences

Anomaly gates templates with `precondition`, `sim_communities` and completed-task prerequisites. Meitou does not
need a full condlist language; start with declarative requirements:

- faction relation threshold, player faction/rank, town, day/time, prior completed contract, active count,
  giver role and cooldown.

Use the same template to apply consequences on completion/failure: faction relation change, reward money range,
items and reputation. These should be saved in the instance, not recomputed from live world state.

### 5. Make text dynamic

Anomaly's `title_functor`/`descr_functor` resolve target name, faction and location at runtime. Meitou's current
`Title`/`Detail` are static. A resolved offer should carry:

- target name, faction/town, distance and region;
- required kills/captures, time limit and reward;
- a short risk/detail line.

Use placeholders or a host callback, but resolve once into API strings so C#/Lua and the UI see the same text.

### 6. Presentation and feedback

- Map marker for the current target; move it to the giver when the contract is ready.
- Notifications for accepted, updated (target/faction/distance), ready, failed and expired.
- Journal entries with icon/priority, like the Faction tab's contract rows.
- A "turn in at <giver>" state instead of silently disappearing when the objective is done.

### 7. Keep giver identity and anti-duplication

- Giver contracts need a stable giver key (already `GiverId`) and a saved character id where possible.
- The same target should not be offered by several givers or re-rolled while a contract is active.
- A dead giver should fail or redirect the contract; Anomaly's `task_giver_alive` check is the pattern.
- Per-giver active limits and per-template cooldowns are separate controls.

## Guardrails

- Keep templates/instances behind the API; C# and Lua should not learn simulation task classes.
- Generate with a saved seed or roll once at offer/accept; save target, reward roll, deadline and giver.
- Do not copy Anomaly content, task ids, dialogue text or scripts.
- Add a stable API field only when at least two givers or samples need it; the three-slot framework/content split should
  remain a small demonstration, not the whole design.
- Preserve the existing global board behavior: global contracts still pay on completion; only giver contracts need
  the turn-in loop.

## Next slice

- Add one generated target contract (a specific non-unique NPC) to the content pack without removing the three-slot
  fallback.
- Add tag/weight/cooldown selection for per-giver offer slates.
- Add requirement and consequence policies (relation, rank, prior work, reward ranges and items).
- Add a dialogue detail page and map markers for the active target/giver.
