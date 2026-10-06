# Multisim Launcher

**Keep starting Multisim until the component library actually loads.**

A small Windows desktop launcher for NI Multisim 14.x that works around a
well known startup problem on Windows 11, where Multisim intermittently comes
up with

```
Problem accessing the database.
The Master Database cannot be accessed. Features using the Master Database
will not be available.
```

and an empty component library, so no parts can be placed.

Press **Start** and the launcher keeps launching Multisim, discarding the
attempts that came up broken, until one instance is healthy. The button then
turns into **Stop**; press it to close Multisim again.

There is **no console window**, and failed attempts are **not thrown in your
face** — only one status line changes.

> 中文说明见下方 [中文](#中文说明)。

---

## Why this is needed

This is a real, reproducible, and widely reported defect — not a corrupted
installation.

* On the reference machine, **only ~25–50 % of cold starts succeed**.
* The failures come in **runs**: several failures in a row, then several
  successes (observed: 6 consecutive failures).
* A failing attempt is **not** a crash: the process stays alive and simply
  parks on a modal dialog.
* The component database files are **not** damaged — the master database
  matches the installer's copy byte-for-byte (SHA-256 verified).
* Microsoft Q&A carries a report with **this exact message** after
  [KB5065426](https://learn.microsoft.com/zh-cn/answers/questions/5579770/kb5065426-multisim-14-3),
  with 5 users confirming the same problem.

Because the failure is random per launch and the databases are intact, the
only reliable strategy is: **try again until it works**. That is exactly what
this launcher automates.

See [`docs/BACKGROUND.md`](docs/BACKGROUND.md) for the full investigation,
including everything that was measured and ruled out.

---

## Download

Grab `MultisimLauncher-Setup.exe` from
[Releases](../../releases) and run it. It installs per-user, so **no
administrator rights are required**, and it creates a desktop shortcut.

If you prefer a portable copy, just download `MultisimLauncher.exe` — it is a
single self-contained file with no dependencies.

![Multisim Launcher](assets/screenshot.png)

> **Unsigned binary warning.** Windows SmartScreen will block the installer on
> first run because it is not code-signed. Click `More info` → `Run anyway`, or
> verify the SHA-256 against `release/SHA256SUMS.txt`, or build from source.
> This is normal for any unsigned program downloaded from the internet — see
> [docs/RELEASE_NOTES.md](docs/RELEASE_NOTES.md).

## Requirements

| | |
|---|---|
| OS | Windows 10 or 11 (x64) |
| Multisim | NI Multisim / Circuit Design Suite 14.x, already installed |
| Runtime | .NET Framework 4.x — already present on every supported Windows |

The launcher **cannot install Multisim**; it only starts the copy you already
have.

---

## Is it only the Chinese version of Multisim?

Short answer: **no, but the reported cases cluster heavily in Chinese-language
environments, and that is a reporting artefact rather than a language
restriction.**

What the evidence actually shows:

* The failing code path is **locale-independent by construction**: Multisim
  opens its `.prd`/`.prj`/`.usr` databases through **DAO 3.6 → the 32-bit Jet
  4.0 / Jet 3.x ISAM engines**, none of which care about the UI language of
  Multisim.
* What *is* locale-dependent is the **system ANSI code page**. The reference
  machine runs a Chinese Windows (ACP = **936**). The most credible community
  root-cause write-up points at the **serviced 32-bit Jet ISAM driver
  (`msrd3x40.dll`)** being changed by the August–September 2025 cumulative
  updates — and on the reference machine that DLL is dated exactly with a
  cumulative update.
* Community fixes that are described as working are **registry / code page**
  changes, and one of them sets `CodePage = 65001`. That only makes sense if
  the code page is part of the mechanism.

So the honest position is:

| Claim | Status |
|---|---|
| Only the Chinese *localised build* of Multisim is affected | **Not supported** — the failing layer is not localised |
| Chinese-language Windows is over-represented in reports | **Supported** — nearly all public reports are `zh-CN` |
| The mechanism is probably code-page / Jet-ISAM related | **Plausible, partly tested** |
| Setting the console code page to 65001 fixes it | **Tested here and refuted** — 25 % vs 38 % success, within noise |

This launcher deliberately does **not** depend on any of those theories. It
works regardless of which locale gets hit, because it does not care *why* an
attempt failed.

---

## How it decides that a launch worked

A launch counts as healthy only when **all** of these hold:

1. the Jet lock files appear in the database folder
   (`MSCOMP_S.ldb`, `CPCOMP_S.ldb`) — the component databases really opened;
2. the Multisim main window exists and is visible;
3. no "Problem accessing the database" dialog is on screen;
4. the process is still alive after a short settle period.

Checking only one of these is not enough: the main window appears even on a
failed launch, and the lock files alone do not prove the UI came up.

## How a failed attempt is discarded

The broken instance is closed with a graceful `WM_CLOSE`, and only force-killed
if it refuses — it usually refuses, because it is sitting on a modal dialog.
The lock files are then removed and the next attempt is started. None of this
is shown to you beyond the attempt counter.

## How Multisim is located

1. **Registry** — the NI installer records the real database paths, so this
   works no matter which drive Multisim is installed on.
2. **Filesystem scan** — covers the default install and the common case of
   Multisim installed onto `D:` or `E:`.

Adding another location is a one-line change in
`src/MultisimLauncher/MultisimLocator.cs`.

---

## Tray, reuse and preheat

Three behaviours that matter if you use this every day. All three came out of
the same discovery: **`multisim.exe` is not single instance.** Starting it while
it is already running produces a second complete copy — measured on the
reference machine, two starts in a row left two processes, each with its own
main window and about 150 MB of memory.

### It will not start a second Multisim

Before every attempt the launcher asks whether a usable instance is already
running, by window class rather than window title (the title carries the open
file name and the UI language, so it is not stable). If there is one, it is
brought to the front and nothing is started.

This also fixed a latent bug: the previous version unconditionally closed every
`multisim.exe` on the machine at the start of each attempt. That was survivable
while the launcher was the only way Multisim ever got started, but it would
have thrown away a good running session on a second click.

### Closing the window keeps it running

Closing the window moves it to the notification area instead of exiting, so a
retry loop that is already in progress can finish. The tray menu restores the
window or exits for real, and the first time you minimise, a balloon points out
where the window went.

Because the window is hidden rather than minimised, no taskbar button is left
behind for a window you cannot see.

`Quit` in the sidebar exits without redirecting to the tray.

### Preheat at login (off by default)

Tick **Preheat at login** and the launcher registers itself under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with `--preheat`. At your
next sign-in it starts Multisim in the background, retrying silently until the
component library loads, then **exits completely** — no window, no tray icon, no
process left resident.

The point is that by the time you click the desktop shortcut, Multisim is
already up and waiting. Cold start of the ~238 MB database is the slow part, and
this is the only way to make it not yours to wait for.

It lives in `HKCU`, not `HKLM`: no elevation, and nothing left behind that you
cannot remove. It is also off unless you turn it on, because the trade is that
Multisim starts with the machine whether or not you use it that day.

If the launcher is later moved, the stored path goes stale — the checkbox
reports **off** in that case rather than showing a tick that does nothing.

---

## Building from source

No SDK, no NuGet, no Visual Studio. The build uses the **C# compiler that
ships inside Windows**.

```powershell
# launcher only  ->  dist\MultisimLauncher.exe
powershell -ExecutionPolicy Bypass -File build.ps1

# launcher + installer  ->  dist\MultisimLauncher-Setup.exe
powershell -ExecutionPolicy Bypass -File build.ps1 -MakeInstaller
```

### Self-tests

The shipped executable can exercise the tray and preheat paths without a mouse,
which is how they are verified — synthetic clicks turned out to be a poor test,
since they need DPI-correct coordinates and a window genuinely on screen:

```powershell
.\dist\MultisimLauncher.exe --selftest-preheat   # writes/reads/removes the Run key
.\dist\MultisimLauncher.exe --selftest-tray      # hide to tray, restore again
```

The exit code is the number of failures, and the report is written to
`%TEMP%\multisimlauncher-selftest-*.txt` and appended to the launcher log. These
switches are in the release build on purpose: they are a few dozen lines, they
touch nothing unless invoked, and being able to reproduce a tray problem on the
machine that has it is worth more than the bytes.

Producing the installer additionally needs [Inno Setup](https://jrsoftware.org/isdl.php);
`installer/get-innosetup.ps1` can fetch and install it for you. End users never
need Inno Setup.

### Project layout

```
build.ps1                     build with the in-box csc.exe
src/MultisimLauncher/
    Program.cs                entry point, Win32 interop
    MultisimLocator.cs        install detection, database folders, health checks
    MainForm.cs               the GUI (dark, hand painted)
    app.manifest              asInvoker + per-monitor DPI + common controls v6
installer/
    MultisimLauncher.iss      Inno Setup script
    get-innosetup.ps1         fetch Inno Setup and build the installer
    payload/install.bat       fallback installer (used by the IExpress path)
docs/BACKGROUND.md            the full investigation
```

### Why `asInvoker` and not administrator?

Multisim keeps a **per-user** component database in
`%APPDATA%\National Instruments\Circuit Design Suite\<ver>\database`. Running
Multisim elevated would give it a different profile and therefore a different
user database. The launcher must run as the logged-on user.

---

## Notes on the code

* **C# 5 only.** The in-box compiler is C# 5, so no string interpolation, no
  null-conditional operators, no expression-bodied members.
* **`SendMessageTimeoutW`, never `SendMessageW`.** A synchronous `SendMessage`
  blocks *forever* while the target thread is not pumping messages — which is
  exactly what Multisim does while loading its ~238 MB database. This was a
  real hang in an earlier tool.
* **Non-ASCII strings are built from code points.** Windows PowerShell 5.1
  decodes BOM-less UTF-8 files as ANSI, which silently corrupts non-ASCII
  literals; the dialog-text match uses `new string(new char[] { (char)0x8BBF, ... })`
  so the source stays pure ASCII and cannot break.
* **All framework references are in-box** (`System`, `System.Core`,
  `System.Drawing`, `System.Windows.Forms`) so the build needs nothing extra.

---

## Contributing

Issues and pull requests are welcome, particularly:

* install locations that the locator fails to find (please include the path);
* reports from **non-Chinese** Windows, to settle the locale question;
* confirmation on Multisim versions other than 14.3.

When reporting a problem, please attach
`%LOCALAPPDATA%\MultisimLauncher\launcher.log`.

## Disclaimer

Not affiliated with, endorsed by, or sponsored by National Instruments or
Emerson. "Multisim", "Ultiboard" and "NI Circuit Design Suite" are trademarks
of their respective owners.

This program does **not** modify, patch, or crack Multisim. It does not touch
your databases, and it changes no Windows settings. It only starts and closes
programs already on your computer.

## License

[MIT](LICENSE.txt)

---

# 中文说明

**反复启动 Multisim,直到元器件库真正加载成功。**

Windows 11 上 Multisim 14.x 启动时会**随机**出现

```
访问数据库时发生错误。
主数据库 无法访问。使用 主数据库 的功能将不可用。
```

然后元器件库是空的,什么都放不了。

这个启动器点一下就帮你反复尝试,把没能正常加载的那几次**安静地关掉**,
直到有一次真正可用为止。**没有终端窗口**,失败过程不会弹一堆框给你看,
只有一行状态文字在变。成功之后按钮变成「停止」,再点一下就把 Multisim 关掉。

## 它解决的是什么

这是一个**真实、可复现、且被广泛报告**的缺陷,不是你的安装坏了:

* 参考机器上**冷启动成功率只有约 25%–50%**;
* 失败是**成串出现**的:连着失败好几次,然后连着成功几次(实测出现过连续 6 次失败);
* 失败**不是崩溃**:进程还活着,只是永久卡在一个模态对话框上;
* 元器件库文件**没有损坏**——主库与安装包内的原始文件 SHA-256 完全一致;
* Microsoft Q&A 上有 [KB5065426 之后出现**逐字相同**报错](https://learn.microsoft.com/zh-cn/answers/questions/5579770/kb5065426-multisim-14-3)的报告,5 人确认同样问题。

既然失败是随机的、而数据库是好的,唯一可靠的办法就是:**不行就再来一次**。
这个启动器就是把这个过程自动化。

## 是不是只有中文版才有这个问题?

**不是。** 但公开报告里中文环境占绝大多数,这更像是**报告来源的偏差**,不是语言限制。

* 出错的那一层**与界面语言无关**:Multisim 通过 **DAO 3.6 → 32 位 Jet 4.0 / Jet 3.x 引擎**打开数据库;
* 真正与区域相关的是**系统 ANSI 代码页**(参考机器是中文 Windows,ACP = **936**);
* 社区里最可信的一条根因分析指向 **2025 年 8–9 月累积更新改动了 32 位 Jet ISAM 驱动
  (`msrd3x40.dll`)**——参考机器上这个 DLL 的日期正好对应一次累积更新;
* 社区给出的"有效"解法都是**注册表/代码页**层面的改动,其中一个就是把 `CodePage` 改成 `65001`。
  这只有在代码页确实是机制的一部分时才说得通。

> 社区流传的「把控制台 `CodePage` 改成 65001」这一条,我在参考机器上做了 8×2 交叉实测:
> **25% vs 38%,差异在噪声范围内,证伪。**

所以本启动器**刻意不依赖任何上述理论**——不管你的机器是哪种区域设置被命中,它都能用,
因为它不关心你**为什么**失败。

## 它怎么判断"这次成功了"

必须**同时**满足:

1. 数据库目录里出现 Jet 锁文件(`MSCOMP_S.ldb`、`CPCOMP_S.ldb`)——说明库真的打开了;
2. Multisim 主窗口存在且可见;
3. 屏幕上没有"访问数据库时发生错误"对话框;
4. 进程在随后一小段观察期内仍然存活。

只看其中一条是不够的:失败的那次**主窗口也会出现**,而只看锁文件也不能证明界面起来了。

## 关于 Multisim 的安装位置

1. **注册表优先**——NI 安装程序记录了真实的数据库路径,所以**无论装在哪个盘**都能找到;
2. **文件系统扫描兜底**——覆盖默认的 C 盘安装,以及很常见的**装在 D 盘 / E 盘**的情况。

如果你的路径很特殊,欢迎提 issue 把路径发我,加一条匹配规则只是一行代码。

## 托盘常驻、复用已有实例、开机预热

这三个功能来自同一个发现:**`multisim.exe` 不是单实例程序。** 它已经在运行时再启动一次,
会**开出第二个完整的副本**——我在参考机器上实测:连续启动两次,留下两个进程,
各自有独立的主窗口,各自占用约 150 MB 内存。

### 不会重复启动第二个 Multisim

每次尝试之前,启动器会先判断**是否已有可用的实例在运行**。
判断依据是**窗口类名**,不是窗口标题——标题里带着打开的文件名和界面语言,不是一个稳定的标识。

如果已有实例,就**把它切到前台,不再启动新的**。

这同时修掉了一个潜在缺陷:**旧版本在每次尝试开始时,无条件关闭机器上所有 `multisim.exe`。**
在"启动器是唯一入口"的前提下这尚可接受,但只要你**再点一次启动**,它就会把正在用的
会话整个丢掉,然后重新开一个——正好是"热启动"的反面。

### 关闭窗口不退程序

关闭窗口会把它**移到通知区域**,而不是退出,这样已经开始的**重试循环可以跑完**。
托盘菜单可以恢复窗口,也可以真正退出;第一次最小化时会弹一个气泡,告诉你窗口去哪了。

窗口是**隐藏**而不是"最小化",所以**任务栏不会留下一个点不开的按钮**。

侧栏的 `退出` 是直接退出,不会绕道托盘。

### 开机预热(默认关闭)

勾选 **开机预热** 后,启动器会把自己写进
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`,带上 `--preheat` 参数。
下次登录时,它在后台启动 Multisim,**静默重试直到元器件库加载成功**,
然后**完全退出**——不留窗口、不留托盘图标、不留常驻进程。

意义在于:等你点桌面快捷方式时,**Multisim 已经开着在等你了**。
加载那个约 238 MB 的数据库才是慢的部分,而这个方案是唯一能让这段等待**不算在你头上**的办法。

写的是 `HKCU` 而不是 `HKLM`:**不需要管理员权限**,而且**不会留下你删不掉的东西**。
它**默认关闭**,因为代价是"不管你今天用不用,Multisim 都随开机启动"。

如果之后移动了启动器位置,注册表里的路径会失效——此时复选框会**显示为未勾选**,
而不是给你一个**什么都不做的假勾**。

## 从源码构建

不需要 SDK、不需要 NuGet、不需要 Visual Studio——用的是 **Windows 自带的 C# 编译器**。

```powershell
# 只构建启动器      -> dist\MultisimLauncher.exe
powershell -ExecutionPolicy Bypass -File build.ps1

# 启动器 + 安装包   -> dist\MultisimLauncher-Setup.exe
powershell -ExecutionPolicy Bypass -File build.ps1 -MakeInstaller
```

### 自测开关

发布出来的 exe **自带了不依赖鼠标的托盘 / 预热自测**:

```powershell
.\dist\MultisimLauncher.exe --selftest-preheat   # 写入/读取/移除 Run 项
.\dist\MultisimLauncher.exe --selftest-tray      # 收进托盘再恢复
```

退出码 = 失败项数量;报告写到 `%TEMP%\multisimlauncher-selftest-*.txt`,并追加进启动器日志。

**这些开关是刻意留在发布版里的**:只有几十行,不调用就什么都不做,
而"能在一台真的出问题的机器上复现托盘故障"比省这点体积重要得多。

(之所以不用模拟鼠标点击来测:那需要 DPI 正确的坐标**且**窗口真的在屏幕上,
我在这一步上把点击打到空白桌面不止一次。)

生成安装包额外需要 [Inno Setup](https://jrsoftware.org/isdl.php),
`installer/get-innosetup.ps1` 可以自动帮你下载安装。**终端用户完全不需要它。**

## 许可证

[MIT](LICENSE.txt)
