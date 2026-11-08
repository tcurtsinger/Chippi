# Port manager: brief

Wave 3. For Travis to confirm. Decisions: [README](README.md) (scope 478, shortcuts 471 and 482, admin helper 483, deleting 485, where things live 486). Research: [behavior-spec-b.md](behavior-spec-b.md) section 4, [architecture.md](architecture.md) sections 6, 9 and 11, [surfaces.md](surfaces.md) sections 6 and 7. The helper: `admin-helper.md` (Kayla). Mockup: later; Lumen draws it before this is built. DESIGN.md wins where it speaks.

## Job

Answer "who is holding port 3000?" and free it, after a question that names what will end.

## Doors

| Door | What it does |
|---|---|
| **Home tile and Actions** | None (README task 486) |
| **Search** | "port 3000" is a value row: "Port 3000 · node.exe" with its PID and state. Its verb depends on the holder (below). Enter on a row with no verb opens Settings › Ports at that row |
| **Search: "ports"** | Opens Settings › System › Ports |
| **Shortcuts** | Find port (opens Search with "port "), Free port (the same, then the confirm) |
| **Radial menu** | Any verb |
| **Bar** | None |
| **Settings** | System › Ports: the live list |

## Behavior

### The list (Settings › Ports)

Protocol, local address and port, remote address and port, state, and the process: real icon, name, PID, path on hover. It opens filtered to **listening** ports, with a switch for all connections. A search box takes a port number or a name ("3000", "node"). Rows that are new since the last read are marked for a few seconds, and ended ones fade (TCPView's idea). Refresh is every 2 seconds while the page is open, with a Pause switch, and nothing runs in the background (architecture section 9). Copy writes a row's port, address or PID.

### Reading

`GetExtendedTcpTable` and `GetExtendedUdpTable` with owner PIDs, IPv4 and IPv6, as a standard user (about 650 rows in under 2 ms, measured in the architecture work). Names from the process list; path and icon with `QueryFullProcessImageName`, which needs only limited access. Services are matched to the PID through `EnumServicesStatusEx`, since `GetOwnerModuleFromTcpEntry` returns empty names to a standard user. Several services can share one `svchost` PID, so a PID alone never says which service owns a port. The owning service is known only when the port's endpoint is attributed to it: `GetExtendedTcpTable` and `GetExtendedUdpTable` in their `OWNER_MODULE` forms (`TCP_TABLE_OWNER_MODULE_LISTENER`, `UDP_TABLE_OWNER_MODULE`) give the service name per endpoint, or the one service in that PID if it hosts only one. If the name can't be read as a standard user, or the PID hosts more than one service, the row is **ambiguous**.

### Verbs, by holder

| Holder | Verb | Confirm |
|---|---|---|
| Your own process | Free | "End node.exe to free port 3000?" with Cancel and End |
| A service (its row says "Service") | Stop, with the UAC shield | "Stop service Print Spooler to free port 9100?" with Cancel and Stop. Only a service whose ownership of this endpoint is attributed is offered Stop, and only that service stops, never its `svchost` host, which may run others. An ambiguous row has no Stop (next list) |
| An admin, SYSTEM or another user's process | Free, with the UAC shield | "End postgres.exe to free port 5432?" with Cancel and End; it goes through the admin helper as `EndProcess(pid, startTime)`, one prompt |
| System (PID 4) or a closing connection (PID 0, TIME_WAIT) | None | The row says why ("Windows"); Enter opens Settings › Ports at the row |

- **Ambiguous shared host:** when the PID's `svchost` hosts several services and the endpoint can't be attributed to one, the row says "Shared Windows service" with no verb, Stop is withheld, and Enter opens Settings › Ports at the row. Nothing ends the host or guesses a service from the PID. `StopService(name)` takes a name only from an attributed row, and the helper refuses a name whose service isn't running in the endpoint's PID.
- **Read again before anything runs.** After Travis confirms, the row's PID and start time are checked once more. If the process changed or has gone, nothing ends and the row updates ("Already closed" or "Changed, check again").
- **Ask the app to close first** for a process with a window (a normal close request, then 3 s). If it's still there, a second step offers "Force end". A listener with no window ends at once on confirm.
- **Never ended:** PID 0 and 4, `csrss`, `wininit`, `lsass`, `services`, `smss`, and Chippi itself. The helper refuses them too.
- **Why the confirm names the process:** its unsaved state is the one thing Travis can't get back (README task 485).

### Left out (README task 478)

Closing a single connection (`SetTcpEntry`), name lookup and whois, and saving the list to a file.

## Edge cases

- **Port held by several processes** (a reuse port, or IPv4 and IPv6): the Search row lists the first and says "+1", Enter opens Settings › Ports filtered to that port.
- **Nothing on the port:** "Nothing is using port 3000".
- **Not a number:** "port abc" is an ordinary search.
- **A process that respawns** (a supervisor): Free ends it once; the next read shows the new holder. Chippi doesn't loop.
- **UAC declined:** "Needs admin"; nothing ends.
- **A row whose path can't be read** (protected process): shows the name with a generic icon; never an error.
- **Port above 65535:** "Ports go up to 65535".

## Shortcuts

| Verb | Slot | Notes |
|---|---|---|
| Find port | Global | Opens Search with "port " ready |
| Free port | Global | The same, then the confirm for the row chosen; with none chosen, a picker of listening ports. Never ends anything by itself |
| Copy port details, Refresh ports, Pause | In context | The page's own buttons |

Unassigned. Settings › Ports shows the global slots read-only with Edit.

## What we took

- **TCPView:** new and ended rows are marked; a calm refresh instead of every second.
- **CurrPorts:** process, path and user on each row; a filter; copy; "ask before any action" stays on, with no switch to turn it off.
- **netstat and Get-NetTCPConnection:** the filter vocabulary (state and port).
- **Left out:** closing one connection, whois, HTML or XML export.

## Copy

| Where | Text |
|---|---|
| Page | Ports; Listening, All connections; Pause, Copy |
| Columns | Protocol, Local, Remote, State, Process, PID |
| Search row | Port 3000 · node.exe · PID 4120 · Listening |
| Row labels | Service, Windows, Free, Stop |
| Confirms | End node.exe to free port 3000? / Stop service Print Spooler to free port 9100? / Force end node.exe? (Cancel and the verb) |
| States | Nothing is using port 3000; Already closed; Changed, check again |
| Errors | End failed; Stop failed; Needs admin; Ports go up to 65535 |
| Shortcut rows | Find port, Free port |

## Shared code

- **New:** `Chippi.Features.PortManager` and `.Tests`, in `Chippi.slnx` (names to Marge first). Module id `port-manager`, already in the catalog (group System); the PR adds `Capability.Admin` and its badge, as App updates has. Shared Core code, called out in the PR.
- **Search value rows:** follows the existing "volume 40" pattern; no new mechanism, unless the pattern needs a verb on the row (the Free button), which is called out in the PR.
- **IP Helper P/Invoke:** pattern from `SystemMonitor/Sensors/Platform/NetworkSource.cs`. Ending a process reuses `WindowsQuickSystem`'s.
- **The helper:** `EndProcess(pid, startTime)` and `StopService(name)` are `admin-helper.md`'s verbs.
- **Settings page:** Lumen's. Verbs through `IModuleUi.AddCommands`; slots through `ModuleContext.Shortcuts`.

## Tests

- **CI, unit:** the table reader against fake IP Helper buffers (IPv4 and IPv6, TCP and UDP, listening and TIME_WAIT, a buffer that grows between calls); holder classification (own, attributed service, ambiguous shared `svchost` with two services in one PID and no endpoint attribution: no Stop, no host end; elevated, PID 0 and 4, Chippi); verb and confirm text per holder; the re-read refusing a changed PID or start time; the protected list; filter and search; port parsing (3000, 0, 65536, "abc").
- **Real reading, no ending:** the real table is read on Travis's PC.
- **Ending:** a test opens its own listener on `127.0.0.1` (not all interfaces, which raises a Firewall prompt) in a child process it started, finds it, and ends it. Never another process. The elevated and service paths run only in the Sandbox, against a stand-in listener and service. No UAC prompt in tests or dev runs.
- **Offscreen shots:** the page, the Search row for each holder, the confirms, empty and error states; dark and light.

## Done means, and timings to set

- **"port 3000" typed to row shown:** under 100 ms warm. Target, not measured.
- **Settings › Ports open:** under 100 ms, already filled; one refresh read under 10 ms of UI work. CPU of the page's 2-second refresh measured over 60 s, three times, and reported.
- Screenshots next to the mockup. The Sandbox run passes.

## Not verified

- That the `OWNER_MODULE` tables give a standard user the service name per endpoint; if not, every shared-host row stays ambiguous (the first spike checks).
- That a standard user can enumerate services and every process's PID name as the architecture infers; the first spike checks.
- The "ask to close, then force" step's timing, and apps that ignore a close request.
- Whether a Search value row can carry a verb button today.
- Timings are guesses until measured.
