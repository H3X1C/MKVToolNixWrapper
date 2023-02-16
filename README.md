# MKVToolNixWrapper
This program is a wrapper for mkvmerge.exe which ships as part of MKVToolNix (collection of tools for .mkv files).
This programs purpose is to facilitate quick bulk editing of .MKV files, allowing you to edit audio, video and subtitle tracks. Editing includes removal of tracks, defaulting tracks, forcing tracks, renaming tracks and setting track language. Also accounts for files having differing track setup as part of the analyse functionality and flags offending files so they can be dealt with in isolation.

# Download
[Download the latest MKVToolNixWrapper](https://github.com/H3X1C/MKVToolNixWrapper/releases)

Changelogs and all releases are provided in the releases section. Do not try to download using the 'Source code (zip)' option if you just want to use the program!

# Screenshots
![Screenshot](MKVToolNixWrapper/Assets/Screenshots/Screenshot1.png)
![Screenshot](MKVToolNixWrapper/Assets/Screenshots/Screenshot2.png)
![Screenshot](MKVToolNixWrapper/Assets/Screenshots/Screenshot3.png)

# Example scenario #1
You have a series of anime that has multiple audio tracks with english dub as the default audio track, the show also has multiple subtitle tracks including english, french and Polish but non of these subtitles are flagged as forced or default so must be enabled manually each time you open an episode.

Using MKVToolNix Wrapper you can quickly remove the tracks you don't want such as the English dub audio track, the French and Polish subtitles. You can also set the default and forced flags if desired for the english subtitle track.
The end result would be a reduced file size and the convenience of opening the file in your chosen player and it automatically defaulting to your own personal preference of subtitles and audio.

# Example scenario #2
You have a collection of mkv files for a TV show, but sadly the track setup differs in 3 out of the 30 episodes, with the subtitle track naming being different and it being in a different order.

Using MKVToolNix Wrapper you must click the 'Analyse' button before proceeding with the batching, doing so will perform an analysis on the files, giving each file a PASS or FAIL status.
In this example the 3 files would have flagged as FAILs and would have been highlighted in red.
A file fails when there are differences in track count, track naming, track language and or track ordering when compared to the first file it analysed.
To progress you would simply uncheck the 3 files that failed, click 'Analyse' again which would result in all PASS so would unlock the 'Start Batch' button allowing you to proceed.
Once you have processed the 27 episode batch you can then click the 'Invert' button to select the remaining 3 episode, analyse again and they would all PASS allowing you batch those 3 in isolation.