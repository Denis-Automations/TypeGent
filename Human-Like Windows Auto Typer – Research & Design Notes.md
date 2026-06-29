# Human-Like Windows Auto Typer – Research & Design Notes

> **Note:** This is the original research log, kept as-is for provenance. Committed decisions live in
> `TypeGent – Findings, Architecture & Tech Stack.md`, the build steps in `plan.md`, and the
> independent review (which revised both on 2026-06-29) in `TypeGent – Approach Review.md`.
> Where this log and those documents disagree, the later documents win — notably: **.NET 10** (not 8),
> **US QWERTY only + Unicode fallback** in v1 (not five layouts), and a **seedable RNG** in the timing models.

## 1. Problem Statement

You want a self-contained Windows tool that, given some text and a focused cursor in any application, types that text character-by-character with realistic human behavior instead of pasting it instantly.[^1][^2] The tool should operate at the OS/Windows keyboard level so it looks and feels like genuine keyboard input to the target app, including proper modifier usage (e.g., Shift+1 for `!`) rather than direct text injection.[^3][^4]

Key aspects of the problem:
- Avoid copy/paste detection and “bot-like” behavior by simulating keystrokes and human timing rather than programmatic text insertion.[^5][^6]
- Work across arbitrary Windows applications (browsers, editors, games, etc.) by sending events to whatever window currently has focus.[^3][^7]
- Provide a configurable but minimal feature set tailored to your use case (no bloated GUI or unnecessary automation flows initially).[^8]

## 2. Findings on Similar Projects (with URLs)

There is a substantial ecosystem of GitHub projects and tools that simulate typing and/or human-like keyboard behavior. The most relevant clusters are:

### 2.1 General Auto-Typers & Human-Like Typing Bots

- **Typing-Simulator** – realistic typing bot using Python, mimics how a human would type. Uses PyAutoGUI/keyboard to send keystrokes to the active window.[^1]
  - URL: https://github.com/ApexXP/Typing-Simulator[^1]

- **Generic clipboard auto-typer (`paste`)** – simple Windows program that types text from clipboard by simulating key presses, effectively replacing paste with keystrokes.[^9]
  - URL: https://github.com/jarekj9/paste[^9]

- **TypingBot** – Python auto-typing script that types into any application using PyAutoGUI.[^10]
  - URL: https://github.com/AdritoPramanik/TypingBot[^10]

- **auto-typer topic** – GitHub topic listing many auto-typing utilities, including Windows tools and scripting-based typers.[^6][^8]
  - URL: https://github.com/topics/auto-typer[^8]

### 2.2 Human-Like Auto-Typers (Typos, Delays, Corrections)

- **human-typing-simulator** – Python script that simulates human-like typing with realistic delays, random typos, and automatic corrections; designed to create natural typing simulations or human-feeling automation.[^11]
  - URL: https://github.com/FoeXploit/human-typing-simulator[^11]

- **GhostType (GitHub / Store family)** – auto-typer tools that mimic “human-like” typing patterns with adjustable speed and natural timing; some variants preserve formatting or focus on being undetectable in chat/Docs contexts.[^12][^13][^14]
  - Windows app (store listing reference): https://apps.microsoft.com/detail/9p7bbqk6zn58[^12]
  - Chrome extension: https://chromewebstore.google.com/detail/ghosttype/dkpbjgpjnbnphdkpeljeknmaednclaoe[^13]
  - GhostType AI: https://chromewebstore.google.com/detail/ghosttype-ai/oailoanlpoofglbaechjhohmbbhpeifi[^14]

- **Human-mimic Auto-Typer** – Python-based automation tool to simulate human typing patterns with configurable imperfections, focused on realistic delays and error corrections.[^15]
  - URL: https://github.com/Pranet-Godavarty/Human-mimic-Auto-Typer[^15]

- **AutoTyper / AutoTyper-Pro / similar GUI tools** – Python + GUI (often CustomTkinter) projects that implement realistic typing simulation with variable speed, random pauses, and occasional mistakes, exposing these options in a desktop UI.[^16][^17]
  - Example URLs:
    - https://github.com/maxmmmmmmmmmma/AutoTyper-Pro[^16]
    - https://github.com/AriooGN/AutoTyper[^17]

### 2.3 Human Typing Engines / Models

- **human_typer (Python package)** – pip-installable package `human_typer` that simulates human keyboard typing. It accepts parameters like `keyboard_layout="qwerty"` and `average_cpm` (characters per minute) and exposes methods like `keyboard_type(text)` and `type_in_element(text, element)` for Selenium.[^18]
  - URL: https://github.com/UnMars/human_typer[^18]

- **HumanTyping (Markov/stochastic)** – “The most realistic keyboard typing simulator based on Markov Chains and stochastic processes,” modeling authentic human typing behavior via probabilistic timing/behavior.[^19]
  - URL: https://github.com/Lax3n/HumanTyping[^19]

These projects provide ideas for modeling keystroke timing, average CPM/WPM, layout awareness, and error patterns that you can port to your own engine.

### 2.4 Windows-Level Keyboard Simulation Libraries

- **Windows Input Simulator** – a C# wrapper around Win32 **SendInput** providing a simple .NET interface to simulate keyboard and mouse input, including modified key strokes and chords.[^3]
  - URL: https://github.com/michaelnoonan/inputsimulator[^3]

- **InputSimulatorPlus** – actively maintained fork “Windows Input Simulator Plus”, built on the same SendInput foundation, recommended as an improved replacement.[^20]
  - URL: https://github.com/kmcnaught/InputSimulatorPlus[^20]

- **Win32 (.NET wrapper)** – `raoyutian/Win32` wraps common Win32 APIs, including modules for keyboard hooks and simulating keyboard input for arbitrary text across languages.[^7]
  - URL: https://github.com/raoyutian/Win32[^7]

- **sendinput C++ helpers** – `myfreeer/sendinput` and `HydraLM81/sendInput` are C/C++ wrappers around SendInput, providing helper functions to send keyboard events and hold key sequences (including multi-key chords like Win+D).[^21][^22]
  - URLs:
    - https://github.com/myfreeer/sendinput[^21]
    - https://github.com/HydraLM81/sendInput[^22]

- **keyboard-auto-type (cross-platform)** – `antelle/keyboard-auto-type` is a cross-platform library for simulating keyboard events, used in apps like KeeWeb, with platform-specific implementations for Windows and others.[^23]
  - URL: https://github.com/antelle/keyboard-auto-type[^23]

These libraries fulfill the requirement to “use the keyboard at the Windows level” via SendInput or equivalent APIs, making your tool appear as genuine keyboard input.

### 2.5 Layout / Key-Mapping References

- **Windows key mapping scripts (AutoHotkey / Emacs)** – repos like `RayIci/windows-key-mapping`, `cnaj/win-keys`, and HHKB mapping gists show how Windows users remap keys and define mapping tables from characters to key combinations using AutoHotkey.[^24][^25][^26]
  - Example URLs:
    - https://github.com/RayIci/windows-key-mapping[^24]
    - https://gist.github.com/haydnhkim/b427d09d160378136e5c2563d6e47bb7[^25]
    - https://github.com/cnaj/win-keys[^26]

These provide useful reference patterns for maintaining character→(virtual key, modifiers) tables per layout.


## 3. Your Actual Requirements

From your description, the distilled requirements for the first version are:

1. **Human-like typing behavior**
   - Keystrokes must exhibit natural timing, with variable delays, small pauses, and optional errors plus corrections, rather than uniform sleep or instantaneous paste.[^11][^18]
   - Optionally model average CPM/WPM and jitter so different profiles (fast, slow typist) can be represented.[^18][^19]

2. **Windows-level keyboard simulation**
   - The tool must send real keyboard events via Windows APIs (e.g., SendInput) so applications treat them as genuine input, avoiding bot-like behavior and bypassing simple paste detection.[^3][^20]
   - Events should go to the **currently focused window**, independent of app type, so the workflow is: enter text → focus cursor → trigger typing.[^3][^7]

3. **Windows-only for now**
   - Initial scope limited to Windows OS, avoiding cross-platform constraints and allowing deep integration with Win32 and keyboard layouts.[^7][^3]

4. **Correct key-combo behavior for characters**
   - Typing must replicate how a real user would hit keys on the physical keyboard: characters that require modifiers should use the appropriate key combos (e.g., `!` via Shift+1 on standard US layout).[^4][^27]
   - Ideally, the tool learns or respects the current keyboard layout (e.g., QWERTY, other locale layouts) so mapping remains correct across configurations.[^18][^7]

5. **Minimal feature set initially**
   - A simple desktop UI: text input, basic configuration (speed, humanization intensity), and start/stop controls, plus a hotkey.
   - No unnecessary advanced features (macro recording, scripting, multi-profile management) in the first iteration.[^8]


## 4. Proposed Approach: Build on Existing Projects and Borrow Features

### 4.1 Core Architecture Overview

The recommended approach is to:

- Use a **Windows SendInput wrapper** (e.g., InputSimulatorPlus) as the keyboard backend to ensure OS-level keystrokes.[^3][^20]
- Implement a small **HumanTypingEngine** inspired by `human_typer` and human-typing-simulator projects to generate realistic timing and optional typos.[^11][^18][^19]
- Maintain a **layout-aware key mapping table** for characters → `(VirtualKeyCode, modifiers)` and feed those sequences into the backend, ensuring correct modifier usage for punctuation and special symbols.[^4][^27][^25]
- Expose a minimal Windows GUI (WinForms or WPF) to configure speed and start typing into whichever window is currently focused.

This lets you avoid reinventing OS-level input and reuse proven human-typing concepts, while tightly scoping the first version.

### 4.2 Base Project to Start From

A strong starting point is either:

- **InputSimulatorPlus (C#)** for the keystroke layer:[^20]
  - URL: https://github.com/kmcnaught/InputSimulatorPlus[^20] forked from https://github.com/michaelnoonan/inputsimulator
  - Benefits:
    - Clean .NET API over SendInput, widely used and maintained.[^3][^20]
    - Supports simple key presses, key up/down, and **modified key strokes** (e.g., Shift + VK_1).[^3][^27]
    - Already proven in Windows desktop environments; you focus solely on humanization and UI.

Optionally, for lower-level control:

- **Win32 (.NET)** – if you want direct Win32 operations and custom wrappers earlier:[^7]
  - URL: https://github.com/raoyutian/Win32  [^7]

For rapid prototyping in Python, you could temporarily use PyAutoGUI-based projects like Typing-Simulator or human-typing-simulator, then port the behavior engine to C# once satisfied.[^1][^11][^28]

### 4.3 Borrowed Features & Logic from Other Projects

#### HumanTypingEngine Design (from `human_typer`, HumanTyping, GhostType-style)

Borrowed ideas:

- From **human_typer**:[^18]
  - `average_cpm` or WPM as the primary speed parameter.
  - `keyboard_layout` parameter to adjust timing/behavior per layout.[^18]
  - Concept of per-character delay derived from CPM.

- From **HumanTyping (Markov/stochastic)**:[^19]
  - Use probabilistic models (e.g., Gaussian/log-normal distributions, Markov chains) for inter-key intervals rather than constant delays.
  - More advanced patterns later (e.g., digraph timing or per-key variability).

- From **human-typing-simulator / GhostType-like tools**:[^11][^14]
  - Random typos: occasionally send a wrong key, then backspace sequences to correct.
  - Micro-pauses at word boundaries, punctuation, or after long words.
  - Adjustable “humanization intensity” controlling jitter and error rate.

Implementation sketch:

- Given text and an `average_cpm`, compute base delay per character.
- For each character:
  - Sample `delayMs` from a distribution centered on the base delay.
  - Optionally inject a typo based on a small probability, then schedule backspaces.
  - At spaces/punctuation, add a slightly longer pause.
- Emit a sequence of `(keySpec, delayMs)` where `keySpec` is either a single `VirtualKeyCode` or a tuple `(modifiers, VirtualKeyCode)`.

#### Keyboard Mapping & Modifier Handling (from key-mapping repos and SendInput wrappers)

Borrowed ideas:

- From **InputSimulator / InputSimulatorPlus**:[^3][^27]
  - Use `ModifiedKeyStroke` to send chords like `Shift + VK_1` for punctuation such as `!`.
  - Use text-entry helpers (`TextEntry`) for baseline char mapping when appropriate.[^3]

- From key-mapping scripts (AutoHotkey, Emacs mappings):[^24][^25][^26]
  - Use mapping tables (per layout) from characters to key sequences.
  - Respect user’s actual keyboard layout by retrieving it via Win32 APIs and selecting the corresponding mapping table.

Implementation sketch:

- Maintain a dictionary `char -> (VirtualKeyCode baseKey, ModifierFlags)`:
  - E.g., `'!' -> (VK_1, SHIFT)`, `'@' -> (VK_2, SHIFT)` for US layout.[^4]
  - For plain letters, just `(VK_A, none)` etc.
- For each character emitted by the HumanTypingEngine:
  - Look up the mapping table to get the key combination.
  - Use InputSimulatorPlus to send either `KeyPress(baseKey)` or `ModifiedKeyStroke(modifiers, baseKey)`.[^3][^27]

### 4.4 Minimal Windows UI & Workflow

Starting from the InputSimulatorPlus library:[^20]

- Build a **WinForms/WPF** app with:
  - Text area where you paste or write the text to be typed.
  - Controls for `average_cpm` (slider/number) and “humanization intensity” (jitter, typo probability).
  - Start/Stop buttons.
  - Optionally, a global hotkey (e.g., F8) to begin typing into the currently focused window.

Workflow:

1. User enters or pastes text into your app.
2. User focuses the target input in any application on Windows.
3. User triggers typing (button click or hotkey).
4. Your app:
   - Generates the `(keySpec, delayMs)` sequence via HumanTypingEngine.
   - Iterates over the sequence, sleeping for `delayMs` then invoking InputSimulatorPlus to send the corresponding key or combo.

Because the backend uses SendInput, the target application sees real keyboard events from the OS input stream.[^3][^20]

### 4.5 URLs Summary for Reuse & Inspiration

Key repos and references:

- **Human-typing behavior & engines**:
  - human_typer: https://github.com/UnMars/human_typer[^18]
  - HumanTyping (Markov-based): https://github.com/Lax3n/HumanTyping[^19]
  - human-typing-simulator: https://github.com/FoeXploit/human-typing-simulator[^11]
  - GhostType family: https://apps.microsoft.com/detail/9p7bbqk6zn58, https://chromewebstore.google.com/detail/ghosttype/dkpbjgpjnbnphdkpeljeknmaednclaoe, https://chromewebstore.google.com/detail/ghosttype-ai/oailoanlpoofglbaechjhohmbbhpeifi[^12][^13][^14]

- **Windows keyboard simulation**:
  - Windows Input Simulator: https://github.com/michaelnoonan/inputsimulator[^3]
  - InputSimulatorPlus: https://github.com/kmcnaught/InputSimulatorPlus[^20]
  - Win32 (.NET): https://github.com/raoyutian/Win32[^7]
  - sendinput C++ helpers: https://github.com/myfreeer/sendinput, https://github.com/HydraLM81/sendInput[^21][^22]
  - keyboard-auto-type (cross-platform): https://github.com/antelle/keyboard-auto-type[^23]

- **General auto-typers**:
  - Typing-Simulator: https://github.com/ApexXP/Typing-Simulator[^1]
  - paste (clipboard typer): https://github.com/jarekj9/paste[^9]
  - TypingBot: https://github.com/AdritoPramanik/TypingBot[^10]
  - auto-typer topic: https://github.com/topics/auto-typer[^8]

- **Key mapping references**:
  - Windows key mapping scripts: https://github.com/RayIci/windows-key-mapping, https://gist.github.com/haydnhkim/b427d09d160378136e5c2563d6e47bb7, https://github.com/cnaj/win-keys[^24][^25][^26]

This set of projects and libraries should give you enough proven building blocks to implement a robust, Windows-only, human-like auto typer with correct modifier behavior, without overbuilding features you don’t need initially.

---

## References

1. [GitHub - ApexXP/Typing-Simulator: This is a realistic typing bot that mimics how a human would type: Read the README](https://github.com/ApexXP/Typing-Simulator) - This is a realistic typing bot that mimics how a human would type: Read the README - ApexXP/Typing-S...

2. [Auto Typer to Automatically Type on Keyboard - MurGee.com](https://www.murgee.com/auto-typer/) - The Auto Typer Software Utility can be used to type Text on Keyboard with a configurable Hot Key or ...

3. [Windows Input Simulator (C# SendInput Wrapper - GitHub](https://github.com/michaelnoonan/inputsimulator) - The Windows Input Simulator provides a simple .NET (C#) interface to simulate Keyboard or Mouse inpu...

4. [SendKeyboard (System) send combined keys like Alt+F9 - vvvv Forum](http://forum.vvvv.org/t/sendkeyboard-system-send-combined-keys-like-alt-f9/15725) - Hi , how could i send combined keys to the system ? , i tried via char and code but seem not to reac...

5. [HumanTyper - Type Like a Human, Not a Robot](https://humantyper.tech) - Paste text like a human! HumanTyper simulates natural keystrokes with speed control, random delays, ...

6. [auto-typer · GitHub Topics](https://github.com/topics/auto-typer?o=desc&s=updated) - Professional automated typing utility with syntax-preserving keyboard simulation, screen-sharing com...

7. [GitHub - raoyutian/Win32: Win32API Net package, including 1: Net package of common Win32 APIs, 2: mouse, keyboard, hotkey hook module, 3: simulating keyboard input text (supporting various characters and text in different languages), simulating mouse click, movement and scrolling, etc. 4: delay function delay method](https://github.com/raoyutian/Win32) - Win32API Net package, including 1: Net package of common Win32 APIs, 2: mouse, keyboard, hotkey hook...

8. [Build software better, together](https://github.com/topics/auto-typer) - GitHub is where people build software. More than 150 million people use GitHub to discover, fork, an...

9. [GitHub - jarekj9/paste: Program types text from clipboard by simulating key presses.](https://github.com/jarekj9/paste) - Program types text from clipboard by simulating key presses. - jarekj9/paste

10. [GitHub - AdritoPramanik/TypingBot: Typing bot made using python](https://github.com/AdritoPramanik/TypingBot) - Typing bot made using python. Contribute to AdritoPramanik/TypingBot development by creating an acco...

11. [GitHub - FoeXploit/human-typing-simulator: A Python script that simulates human-like typing with realistic delays, random typos, and automatic corrections. Perfect for creating natural typing simulations or automating text input with a human touch.](https://github.com/FoeXploit/human-typing-simulator) - A Python script that simulates human-like typing with realistic delays, random typos, and automatic ...

12. [GhostType AutoTyper - Free download and install on Windows](https://apps.microsoft.com/detail/9p7bbqk6zn58) - Preserves Bold, Italic, and Rich Text formatting from your clipboard while mimicking natural, human-...

13. [GhostType - Chrome Web Store](https://chromewebstore.google.com/detail/ghosttype/dkpbjgpjnbnphdkpeljeknmaednclaoe) - Humanlike auto-typing for chat platforms. Triggers the 'is typing' indicator so replies feel natural...

14. [GhostType AI - Chrome Web Store](https://chromewebstore.google.com/detail/ghosttype-ai/oailoanlpoofglbaechjhohmbbhpeifi) - The ultimate undetectable AI typing engine. Perfect for bypass and high-speed automation.

15. [GitHub - Pranet-Godavarty/Human-mimic-Auto-Typer: A Python-based automation tool designed to simulate human typing patterns with configurable imperfections.](https://github.com/pranet111/Human-mimic-Auto-Typer) - A Python-based automation tool designed to simulate human typing patterns with configurable imperfec...

16. [AutoTyper Pro – Human Typing Simulation Tool - GitHub](https://github.com/maxmmmmmmmmmma/AutoTyper-Pro) - AutoTyper Pro is a realistic, customizable typing simulator built with Python and CustomTkinter. It ...

17. [AriooGN/AutoTyper - Realistic Typing Simulation Tool - GitHub](https://github.com/AriooGN/AutoTyper) - Realistic Typing Simulation: AutoTyper leverages random number generation to introduce variability, ...

18. [Python package to simulate human keyboard typing · GitHub](https://github.com/UnMars/human_typer) - Python package to simulate human keyboard typing. Human_typer(keyboard_layout = "qwerty", average_cp...

19. [Lax3n/HumanTyping: The most realistic keyboard typing simulator ...](https://github.com/Lax3n/HumanTyping) - The most realistic keyboard typing simulator based on Markov Chains and stochastic processes. HumanT...

20. [kmcnaught/InputSimulatorPlus: Windows Input Simulator Plus - GitHub](https://github.com/kmcnaught/InputSimulatorPlus) - This library is a fork of Michael Noonan's Windows Input Simulator (a C# wrapper around the SendInpu...

21. [GitHub - myfreeer/sendinput: keyboard and mouse input simulator for windows](https://github.com/myfreeer/sendinput) - keyboard and mouse input simulator for windows . Contribute to myfreeer/sendinput development by cre...

22. [GitHub - HydraLM81/sendInput](https://github.com/HydraLM81/sendInput) - Contribute to HydraLM81/sendInput development by creating an account on GitHub.

23. [GitHub - antelle/keyboard-auto-type: Cross-platform library for simulating keyboard events](https://github.com/antelle/keyboard-auto-type) - Cross-platform library for simulating keyboard events - antelle/keyboard-auto-type

24. [GitHub - RayIci/windows-key-mapping: AutoHotKey script for window that remap some useful keys.](https://github.com/RayIci/windows-key-mapping) - AutoHotKey script for window that remap some useful keys. - RayIci/windows-key-mapping

25. [macOS-like HHKB(happy hacking) key mapping for windows](https://gist.github.com/haydnhkim/b427d09d160378136e5c2563d6e47bb7) - macOS-like HHKB(happy hacking) key mapping for windows - hhkb.ahk

26. [GitHub - cnaj/win-keys: Windows key mapping for Emacs](https://github.com/cnaj/win-keys) - Windows key mapping for Emacs. Contribute to cnaj/win-keys development by creating an account on Git...

27. [winauth/Third Party/InputSimulator/InputSimulator.cs at master](https://github.com/winauth/winauth/blob/master/Third%20Party/InputSimulator/InputSimulator.cs) - This function retrieves the state of the key when the input message was generated. /// To retrieve s...

28. [GitHub - asweigart/pyautogui: A cross-platform GUI automation ...](https://github.com/asweigart/pyautogui) - PyAutoGUI is a cross-platform GUI automation Python module for human beings. Used to programmaticall...

