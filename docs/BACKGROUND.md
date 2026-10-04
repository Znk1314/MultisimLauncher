# Background: what this project works around

This document is the condensed technical record behind Multisim Launcher. It
exists so that the next person does not have to redo the investigation.

The measurements below were taken on one machine and are reported as such. They
are consistent, but they are a sample of one.

---

## 1. The symptom

On Windows 11 (24H2 / 25H2), NI Multisim 14.x intermittently starts with

```
Problem accessing the database.
The Master Database cannot be accessed. Features using the Master Database
will not be available.
```

The main window still opens, but the component library is empty, so no parts
can be placed.

On the machine this was measured on, the failure is **not** rare:

| measurement | result |
|---|---|
| cold starts sampled | 25 |
| succeeded | 8 (≈32 %) |
| failed | 17 |
| crashes during those failures | **0** |

A later batch of 10 produced 2 successes. The rate drifts between roughly
**20 % and 50 %** across batches.

## 2. Failures arrive in runs, not independently

This is the single most useful observation for anyone building a retry tool.
A typical raw sequence:

```
ok  fail fail fail ok ok fail fail
```

and in another batch:

```
fail fail fail fail fail fail ok fail ok fail
```

Six consecutive failures were observed. Retrying *immediately* therefore lands
inside the same failing run very often. This is why the launcher waits a little
between attempts instead of hammering the button.

## 3. A failing attempt is not a crash — it parks on a dialog

On failure:

* `.ldb` lock files: **0** (Jet never opened any database)
* the error dialog is on screen and stays there
* the process is **alive**: ~4 s CPU, ~150 MB, 61 threads, `Responding = True`
* crash dumps and WER reports: **unchanged**

The dialog's real structure, which is not documented anywhere:

```
class = #32770   title = 'Multisim'   visible = True
  id=1   class=Button   text='OK'
  id=52  class=Edit     text='Problem accessing the database.
                               The Master Database cannot be accessed...'
```

Two consequences:

1. The message lives in an **`Edit` control that does not answer
   `GetWindowText`** — you must send `WM_GETTEXT`. Comparing only the window
   class and title tells you "a box exists" but never what it says.
2. **The main window exists even on a failed launch**, so "Multisim is open" is
   not evidence of success.

## 4. There *is* a real crash, and it is a consequence, not the cause

Windows Event Log contains 8 `APPCRASH` entries for `multisim.exe`, all with an
identical signature:

```
faulting module : clr.dll  (4.8.9345.0, built by NET481REL1LAST_25H2_C)
exception code  : 0xc0000005
fault offset    : 0x004598aa
crash bucket    : 2241029211660656768
```

Aligning each failure's PID with the crash events shows what is happening:

| attempt | outcome | crash event | dump |
|---|---|---|---|
| 4 | failed @13:33:46 | 13:33:47 | pid 27464 |
| 7 | failed @13:35:46 | 13:35:47 | pid 9700 |
| 8 | failed @13:35:59 | 13:36:01 | pid 30384 |
| 9 | failed @13:36:13 | 13:36:14 | pid 33208 |

Every failed attempt crashed about a second after the attempt was **closed**;
every successful attempt closed cleanly with no crash. So:

```
database open fails  ->  process is left in a state where shutdown crashes
database open succeeds -> clean shutdown
```

The `clr.dll` crash is a **symptom** of the failed initialisation, not an
independent defect. This also explains an old, misleading observation of
"it crashed five seconds after starting successfully" — that was a failed
launch being closed.

## 5. What was measured and ruled out

Everything below was tested on the reference machine.

| candidate | how it was tested | verdict |
|---|---|---|
| damaged master database | SHA-256 vs the copy inside the installer | **identical** — not damaged |
| corporate / user database damage | removed each in turn, re-measured | no change |
| permissions on the database folders | ACLs + an actual file write probe | writable, no `ReadOnly` |
| leftover `.ldb` lock files | stale locks present vs removed, interleaved | **not the deciding factor** (0/4 vs 2/4) |
| Jet / DAO engine health | built a password-protected Jet 3 database, opened it 15 times | 15/15 success, 0 failures |
| Jet registry corruption | compared against the machine's own pre-change backup | identical |
| a missing/incorrect Jet ISAM path | all 9 engine DLLs + Authenticode signatures | all valid, Microsoft-signed |
| third-party DLL hijacking | `AppInit_DLLs`, IFEO, engine path targets | all clean |
| bitness mismatch | `DAO.DBEngine.36` probed from a 32-bit process | works |
| Windows 8 compatibility shim | applied and measured | **actively harmful** — `dao360.dll` and `MSJET40.DLL` never load at all |
| Windows code page | launched with `chcp 65001` vs 936, interleaved A/B | 25 % vs 38 % — **within noise** |
| system file corruption | `DISM /RestoreHealth` + `sfc /scannow` | repaired 3678 items; **no effect on this fault** |
| Defender scanning the database | added path + process exclusions | measured 50 %, later 20 % — **noise** |

## 6. What is *not* claimed

* The root cause is **not** identified.
* The exact Jet error code was never obtained. The dialogs carry no error code,
  and the Database Manager cannot be loaded while the fault is active
  ([NI KB](https://www.ni.com/knowledgebase/66FBC47314D58D3886257FE50048167F)).
* The crash call stack was never recovered; no debugger was available on the
  reference machine.

## 7. Where the public discussion points

| source | what it says |
|---|---|
| [Microsoft Q&A #5579770](https://learn.microsoft.com/zh-cn/answers/questions/5579770/kb5065426-multisim-14-3) | The **exact same message** after KB5065426; 5 users confirm. The suggested fix (uninstall the update) was reported as impossible — the update cannot be removed once installed. A later comment repeats the same request. |
| [NI KB: Problem Accessing the Database](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z0000019YLUSA2) | Official causes: corrupt user configuration, **corrupt Jet engine registry**, third-party software. |
| [NI KB: Why does Database Manager not load](https://www.ni.com/knowledgebase/66FBC47314D58D3886257FE50048167F) | Database corruption; fix by repairing the NI installation. |
| Community posts (CNBlogs, CSDN) | Three recurring fixes: uninstall a Windows update, registry `CodePage = 65001`, or **"open several copies of Multisim and keep the one that works"**. |

That last one is the honest state of the art: the community workaround is
manual retrying. **This project automates exactly that**, which is why it does
not depend on any theory about the cause.

### A note on source quality

A large share of the Chinese-language pages found on this topic are
AI-generated filler. Signs: near-identical titles, very recent dates, claims
like "fixes 98.7 % of cases", and outright factual errors — one article
asserts Multisim uses **SQLite** (it uses **Jet/Access**), another blames WSL's
`chown` for hijacking NTFS ACLs. Treat blogs as hints, not evidence.

## 8. Open leads

1. **`msrd3x40.dll`** — the 32-bit Jet 3.x ISAM driver. Multisim's databases are
   Jet 3 format and go through this engine. On the reference machine it is
   dated exactly with a Windows cumulative update, and one community write-up
   claims the update broke it. **This is the most promising lead and is untested.**
2. **NI Package Manager Repair** — rebuilds the databases and resets registry
   state; recommended by NI's own KB. Not yet tried.
3. **Another Windows account / another machine** — cheap and decisive for
   separating "this profile" from "this machine" from "this version".

## 9. Reproducing any measurement here

The launcher's log at `%LOCALAPPDATA%\MultisimLauncher\launcher.log` records
every attempt. To measure a failure rate yourself, launch repeatedly and count.

When comparing two conditions, **interleave them** (A, B, A, B, ...). The
failure rate drifts over minutes, so running all of A and then all of B will
attribute that drift to your change. A clear example from this investigation:
the same "unchanged" condition measured 32 %, then 50 %, then 20 % across
three batches.
