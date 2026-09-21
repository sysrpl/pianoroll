# Piano Roll

A player piano: open a MIDI file and its notes fall down the window as coloured bars, playing
themselves on the keyboard as they land. Play it on a recorded piano, saxophone, bass or electric
guitar — or split the keyboard and play it on two instruments at once.

Built with .NET 8 and [Avalonia](https://avaloniaui.net/), so it runs on Linux, Windows and macOS.

https://github.com/user-attachments/assets/d870b253-1e9a-4240-be3c-930fc12c7ef1

*🔊 The video has sound — GitHub starts it muted, so click the speaker icon.*

## Features

- **Watch and hear any MIDI file.** Open a song and its notes fall onto a full 88-key keyboard,
  lighting each key as it plays. The first note starts at the top of the screen, so you see the
  piece coming. Tempo changes are followed; drum tracks are left out.
- **Notes you can read at a glance.** Each bar is as long as its note, so a whole note is four
  times the length of a quarter note, and the bars glow in colours that drift slowly through the
  piece. Every note throws off sparks as it lands — more of them the harder it's played.
- **Keyboard split: two instruments in one song.** Turn on the split and a red line appears in
  the roll. Keys to its left play one instrument and keys to its right another — a bass under a
  piano, say. Drag the line to move the split, and give each side its own volume. The split's
  controls can be hidden while it keeps playing, for a clean view.
- **Real instruments.** Recorded piano, saxophone, bass and two electric guitars, plus piano,
  organ, electric guitar and banjo sounds that work on any computer.
- **Played with feeling.** How hard each note was played in the file sets how loud and bright it
  sounds, and the sustain pedal is followed.
- **A real-looking keyboard** that stays in a piano's proportions as the window changes size.
  Click the keys to play along, even while a song is playing.
- **Easy to find your place.** A seek bar down the right-hand side: click to jump, drag to scrub,
  or scroll to skip back and forward.
- **Made for recording.** Full screen with F11, and F1 puts a clean picture up behind the player,
  so you can capture it without the desktop showing.
- Your instrument, split, volume and window size are remembered.

## Using it

| | |
| --- | --- |
| **Open...** or Ctrl+O | Choose a MIDI file. Songs that come with the program are in `music/`. |
| Play, pause, stop | In the toolbar; the clock and volume are beside them. |
| Instrument button | Choose what the keyboard sounds like. |
| Split button | Turn the keyboard split on or off. A second toolbar row appears for each side's instrument and volume. |
| Sliders button | Hide or show the split's controls and red line; the split keeps playing. |
| F11 / Escape | Full screen, and back. |
| F1 | A clean backdrop behind the player, for recording. |
| F2 | Sound diagnostics, if playback ever stutters. |

## Instruments

| Instrument | Sound |
| --- | --- |
| Real piano, Real saxophone, Real bass, Real black guitar, Real green guitar | Recordings of real instruments, in `samples/` |
| Piano, Organ, Electric guitar, Banjo | From a General MIDI SoundFont if one is installed, otherwise made by the program |

A SoundFont is optional. On Ubuntu or Linux Mint, `sudo apt install fluid-soundfont-gm` installs one
where Piano Roll finds it automatically.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

Everything else is downloaded automatically on the first build. On Linux, install the SDK with
your package manager, for example `sudo apt install dotnet-sdk-8.0`.

## Running

```sh
dotnet run
```

## Installing (Linux)

```sh
./publish.sh
```

installs Piano Roll into `~/.local/share/pianoroll`, with its songs and instruments, and adds it
to your applications menu.

## Where things are stored

Settings are kept in `~/.config/pianoroll/settings.json` (`%APPDATA%\pianoroll` on Windows).
Deleting the file simply puts everything back to its defaults.

## License

MIT. See [LICENSE](LICENSE).
