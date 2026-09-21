# Piano Roll

A player piano: open a MIDI file and its notes fall down the window as coloured bars, playing
themselves on the keyboard as they land. The keys can also be clicked by hand, with a recorded
piano, saxophone, bass or electric guitar, or with sounds the program synthesises itself.

Built with .NET 8 and [Avalonia](https://avaloniaui.net/) for the interface, and
[SDL2](https://www.libsdl.org/) for audio output, so it runs on Linux, Windows and macOS.

https://github.com/user-attachments/assets/d870b253-1e9a-4240-be3c-930fc12c7ef1

## Features

- **MIDI playback**: **Open...** in the toolbar (or Ctrl+O) loads a `.mid` file and starts it
  playing. The dialog starts in the `music/` folder that comes with the program. Tempo changes
  in the file are followed, and percussion (channel 10) is left out, since drum notes aren't
  pitches. A piece is shifted back four seconds as it loads, so its first note enters at the top
  of the screen and falls the whole way rather than landing the instant the file opens.
- **Falling notes**: each note is a bar in its key's column, moving down so its bottom edge
  touches the keyboard exactly as the note sounds. A bar is as long as the note, so a whole
  note's bar is four times a quarter note's, and every bar is the same width, centred on its key,
  whether it lands on a white key or a black one. Four seconds of music are visible at a time.
- **Colour that moves**: bars are coloured from a looping palette — cyan, blue, violet, pink,
  warm orange, sea green — placed by pitch and turned slowly as the piece goes on, so the bass
  and treble always differ and the same phrase never comes back in the same colours. Each bar has
  a soft halo and a bright leading edge, and brightens while it sounds.
- **Sparks**: landing throws a burst of sparks up off the key, more of them the harder the note
  is struck, which drift, slow under gravity and fade.
- **A full-sized keyboard**, always: 88 keys from A0 to C8, whatever the song uses. They are
  shaded and rounded like real ivories and sharps — a felt strip behind them, shadows under the
  black keys, a front lip that presses down as a key is held — and the keyboard's height follows
  the window's width, so the keys stay in a real piano's proportions instead of growing skinny.
  Click a key to play it, hold to sustain, drag along them to slide from note to note; all of it
  works while a song is playing.
- **Transport**: play, pause and stop in the toolbar, with the song's name, the clock and a
  volume slider beside them.
- **Seek bar**: a slim vertical bar down the right-hand side — the top is the start of the song
  and the bottom the end. Click anywhere on it to jump there, drag the handle to scrub, or roll
  the wheel over it to rewind and fast-forward five seconds at a time. Marks every 30 seconds
  give a sense of scale.
- **Full screen** from the toolbar or F11; Escape comes back.
- **A backdrop for recording**: F1 puts `images/wallpaper.jpg` up on a window of its own, filling
  the screen *behind* the player, so the player can be captured against a clean background
  instead of the desktop. F1 again takes it down. Replace the JPEG and rebuild to use another
  picture.
- **Velocity and sustain**: a note's recorded attack velocity sets how loud *and* how bright it
  sounds — softly struck notes are dulled as well as quietened, rather than just turned down —
  and the sustain pedal (controller 64) is followed, so released keys keep ringing while it's
  held.
- The instrument, volume, window size and SoundFont are remembered between sessions.

## Instruments

The toolbar's instrument button opens a list describing each one. Three kinds of sound sit
behind it, and whichever is available wins in this order.

### Recorded instruments

WAVs named by note in a folder under `samples/`, one per instrument:

| Instrument | Folder | Notes | Spacing |
| --- | --- | --- | --- |
| Real piano | `samples/piano` | 30 | every 3 semitones |
| Real saxophone | `samples/saxaphone` | 32 | chromatic |
| Real bass | `samples/bass` | 17 | every 3 semitones |
| Real black guitar | `samples/black` | 47 | chromatic |
| Real green guitar | `samples/green` | 47 | chromatic |

Files are named `A1.wav`, `Asharp1.wav`, `C4.wav` and so on, where C4 is middle C. A note with no
recording of its own is resampled from the nearest one — never more than a semitone away with the
spacings above, which is inaudible — so a set covering part of the keyboard still plays across
all of it. 8, 16, 24 and 32-bit PCM and floating-point WAVs are all read.

Each set is loaded in the background the first time its instrument is chosen. Instruments that
hold a note, like the saxophone, use the sustain loop recorded in the WAV's `smpl` chunk, so a
held key lasts as long as it is held rather than running out of recording.

### A SoundFont

For the instruments with no recordings, the app looks for a General MIDI bank at startup — on
Linux, `/usr/share/sounds/sf2/FluidR3_GM.sf2` from the `fluid-soundfont-gm` package — and plays
the piano, organ, guitar and banjo from it through [MeltySynth](https://github.com/sinshu/meltysynth).
The path is kept as `SoundFontPath` in settings.json; set it to `none` to play with the built-in
synthesis instead.

### Built-in synthesis

Always there, so the app makes a sound on a machine with no samples and no SoundFont:

- *Piano*: sixteen partials, stretched sharp by string stiffness, each with its own decay — the
  top of the spectrum is gone in a second while the fundamental rings on, and bass notes ring
  roughly ten times longer than treble ones. The partials beat slowly against each other, as a
  note's two or three strings do, and a short filtered noise burst stands in for the hammer.
- *Organ*: drawbar partials holding at full strength while the key is down.
- *Electric guitar and banjo*: plucked strings (Karplus-Strong), shaped by where the pick strikes
  — a comb filter that cancels harmonics, which is most of why the guitar sounds round and the
  banjo nasal — then a tone control, a soft asymmetric clip for amplifier warmth, and a DC
  blocker.

Sixteen notes can sound at once, and velocity changes brightness as well as loudness.

## How the sound works

SDL2 is used for **audio output only** — Avalonia draws everything. SDL is started with just its
audio subsystem (no window, renderer or event loop), and calls back on its own thread every 512
frames (about 12ms at 44.1kHz) for more samples; `SynthEngine` mixes whichever notes are down and
hands them over in stereo. That callback never touches the UI, so clicks stay responsive while
notes ring.

Native SDL2 binaries come with the `ppy.SDL2-CS` package for Windows (x64/x86/arm64), macOS
(x64/arm64) and Linux x64, so there is nothing to install per platform.

The same frame timer drives the player, the falling bars and the seek bar from one position
value, so what is heard and what is seen cannot drift apart.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.100 or later)

NuGet packages — Avalonia, `ppy.SDL2-CS`, `Melanchall.DryWetMidi` and `MeltySynth` — are
downloaded automatically on the first build.

A SoundFont is optional. On Ubuntu / Linux Mint:

```sh
sudo apt install fluid-soundfont-gm
```

installs the FluidR3 General MIDI bank where the app looks for it. On Windows and macOS there is
no standard location, so download any `.sf2` bank and put its path in settings.json.

### Linux

Install the .NET 8 SDK with your distribution's package manager, for example:

```sh
sudo apt install dotnet-sdk-8.0
```

Avalonia needs an X11 desktop (or XWayland) and fontconfig, which desktop installs already have.
The app turns off Avalonia's desktop-portal file picker (`UseDBusFilePicker = false` in
`Program.cs`), because that picker never passes a starting folder to the portal and Open would
always come up in the home folder instead of `music/`.

## Running

```sh
dotnet run
```

## Installing (Linux)

`publish.sh` builds a self-contained copy into `~/.local/share/pianoroll` and adds a menu entry:

```sh
./publish.sh
```

It builds into a staging folder first and syncs across, so old files from earlier builds are
cleared out, then copies `samples/` and `music/` in beside the program — both are left out of the
build because they are far larger than it is.

## Where things are stored

`~/.config/pianoroll/settings.json` (`%APPDATA%\pianoroll` on Windows) holds the window size, the
chosen instrument, the volume, the SoundFont path and the folder the last MIDI file came from.
Nothing in it is secret, and deleting it simply starts everything at its default.

## Layout

```
music/                  MIDI files that come with the program (not copied into the build)
samples/<instrument>/   Recorded instruments, one WAV per note (likewise)
images/icon.png         App icon, used by the window, the taskbar and Help > About
src/Program.cs          Entry point and platform options
src/App.axaml           Application, theme palette and rich-tooltip template
src/Themes/             Mint-Y-Dark styling for Fluent controls
src/Views/              Main window, split by job, and the About and Instrument dialogs
src/Controls/           PianoKeyboard (the keys), PianoRoll (falling bars and sparks), SeekBar
src/Audio/              SDL2 output device, the mixer, the sampler, SoundFont playback,
                        WAV reading, and the built-in synthesis voices
src/Models/             Instruments, and a MIDI file flattened to notes
src/Services/           MIDI reading, the player's clock, finding samples, music and
                        SoundFonts, and settings
docs/styling.txt        UI styling rules
```

## License

MIT. See [LICENSE](LICENSE).
