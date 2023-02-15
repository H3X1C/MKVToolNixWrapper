using MKVToolNixWrapper.Dtos;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;

namespace MKVToolNixWrapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<string> SourceMkvPaths { get; set; }
        public List<FileList> FileList { get; set; } = new List<FileList>();
        public List<TrackList> TrackList { get; set; } = new List<TrackList>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;
            PlayIntroSound();
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;
            TrackGrid.ItemsSource = null;
            FileListBox.ItemsSource = null;

            var openFileDlg = new FolderBrowserDialog();
            var result = openFileDlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                BrowseFolderHandler(openFileDlg.SelectedPath);
            }
            else
            {
                SelectedFolderPathLabel.Content = "";
            }
        }

        private async void BrowseFolderHandler(string path)
        {
            WriteOutputLine($"Source folder selected: \"{path}\"");
            BrowseFolderButton.IsEnabled = false;

            // Prevent blocking of the UI thread
            await Task.Run(() =>
            {
                // Force mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);

                SourceMkvPaths = GetMkvFilesInFolder(path);
                if (SourceMkvPaths.Count == 0)
                {
                    System.Windows.MessageBox.Show("Unable to locate .mkv files in the selected folder, please select a valid folder.\r\n\r\nNote: 'MasteredFiles' folder is reserved and not included in the search!", "Directory error", MessageBoxButton.OK, MessageBoxImage.Error);
                    WriteOutputLine($"Error: Unable to locate .mkv files in the selected folder: \"{path}\"");
                    Dispatcher.Invoke(() => SelectedFolderPathLabel.Content = "");
                    // Restore cursor
                    Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
                    Dispatcher.Invoke(() => BrowseFolderButton.IsEnabled = true);
                    return;
                }

                // Path is valid, update UI label
                // Execute on the UI thread
                Dispatcher.Invoke(() => SelectedFolderPathLabel.Content = path);

                WriteOutputLine($"Source folder successfully validated - MKV count: {SourceMkvPaths.Count()}");
                // Enable anaylse button
                Dispatcher.Invoke(() => AnalyseButton.IsEnabled = true);

                // Once user selects the folder we populate a multi select list of the MKV files it found in the directory.
                Dispatcher.Invoke(() => FileList = SourceMkvPaths.Select(x => new FileList() { FilePath = x, Include = true }).ToList());

                Dispatcher.Invoke(() => FileListBox.ItemsSource = this.FileList);
                Dispatcher.Invoke(() => BrowseFolderButton.IsEnabled = true);

                // Restore cursor
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
            });
        }

        private async void AnalyseMkvFiles()
        {
            await Task.Run(() =>
            {
                WriteOutputLine("Analysis has started...");

                // Force mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);

                Dispatcher.Invoke(() => TrackGrid.ItemsSource = null);
                var includedFiles = FileList.Where(x => x.Include).ToList();

                FileList? CompareFileItem = null;
                var CompareMkvSubTracks = new List<Track>();
                var CompareMkvAudioTracks = new List<Track>();
                var CompareMkvVideoTracks = new List<Track>();
                var fail = false;

                foreach(var fileItem in FileList.Where(x => x.Include).ToList())
                {
                    var MKVInfo = QueryMkvFile(fileItem.FilePath);
                    if (MKVInfo == null)
                    {
                        WriteOutputLine($"FAIL - \"{Path.GetFileName(fileItem.FilePath)}\" is not a valid mkv file");
                        continue;
                    }

                    if (CompareFileItem == null)
                    {
                        // Populate comparison point
                        WriteOutputLine($"PASS - \"{Path.GetFileName(fileItem.FilePath)}\" marked as comparison point");
                        CompareFileItem = fileItem;
                        CompareMkvSubTracks = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                        CompareMkvAudioTracks = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                        CompareMkvVideoTracks = MKVInfo.tracks.Where(x => x.type == "video").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();

                        // Using track info from first file, will later be applied to the track grid given it passes
                        TrackList = MKVInfo.tracks.OrderBy(y => y.id).ThenBy(z => z.properties.track_name)
                            .Select(x => new TrackList
                            {
                                Id = x.id,
                                Name = x.properties.track_name,
                                Language = x.properties.language,
                                Type = x.type,
                                Codec = x.properties.codec_id,
                                Include = true,
                                Default = x.properties.default_track,
                                Forced = x.properties.forced_track
                            }).ToList();
                        continue;
                    }

                    var curMkvVideoTracks = MKVInfo.tracks.Where(x => x.type == "video").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    if (!CheckTracks("video", fileItem.FilePath, curMkvVideoTracks, CompareFileItem.FilePath, CompareMkvVideoTracks))
                    {
                        fail = true;
                        continue;
                    }

                    var curMkvAudioTracks = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    if (!CheckTracks("audio", fileItem.FilePath, curMkvAudioTracks, CompareFileItem.FilePath, CompareMkvAudioTracks))
                    {
                        fail = true;
                        continue;
                    }

                    var curMkvSubTracks = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    if (!CheckTracks("subtitles", fileItem.FilePath, curMkvSubTracks, CompareFileItem.FilePath, CompareMkvSubTracks))
                    {
                        fail = true;
                        continue;
                    }

                    WriteOutputLine($"PASS - \"{Path.GetFileName(fileItem.FilePath)}\"");
                }

                // unset mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);

                if (fail)
                {
                    WriteOutputLine($"Analysis Completed - Outcome: FAIL");
                    WriteOutputLine($"Explanation: Unable to unlock batching as the selected files have differing sub/audio/video track setup, proceeding would result in missmatched tracks");
                    WriteOutputLine($"Resolution: Deselect the MKV's that have FAILED and process them on their own. Only once all selected files PASS is the batch button unlocked");
                    SystemSounds.Hand.Play();
                }
                else
                {
                    WriteOutputLine($"Analysis outcome: PASS");
                    Dispatcher.Invoke(() => TrackGrid.ItemsSource = TrackList);
                    Dispatcher.Invoke(() => TrackGrid.IsEnabled = true);
                    Dispatcher.Invoke(() => BatchButton.IsEnabled = true);
                }
            });
        }

        private bool CheckTracks(string trackType, string currentFilePath, List<Track> currentTracks, string compareFilePath, List<Track> compareTracks)
        {
            var currentCount = currentTracks.Count;
            var compareCount = compareTracks.Count;

            if (currentCount != compareCount)
            {
                WriteOutputLine($"FAIL - \"{Path.GetFileName(currentFilePath)}\" due to differing {trackType} track count compared to \"{Path.GetFileName(compareFilePath)}\": {currentCount} vs {compareCount}");
                return false;
            }

            for (int i = 0; i < currentCount; i++)
            {
                if (currentTracks[i].properties.language != compareTracks[i].properties.language)
                {
                    WriteOutputLine($"FAIL - \"{Path.GetFileName(currentFilePath)}\" due to {trackType} track {i} having a different lang flag compared to \"{Path.GetFileName(compareFilePath)}\": {currentTracks[i].properties.language} vs {compareTracks[i].properties.language}");
                    return false;
                }

                if (currentTracks[i].properties.track_name != compareTracks[i].properties.track_name)
                {
                    WriteOutputLine($"FAIL - \"{Path.GetFileName(currentFilePath)}\" due to {trackType} track {i} having a different track name compared to \"{Path.GetFileName(compareFilePath)}\": {currentTracks[i].properties.track_name} vs {compareTracks[i].properties.track_name}");
                    return false;
                }
            }

            return true;
        }


        private List<string> GetMkvFilesInFolder(string path)
        {
            return Directory.GetFiles(path, "*.mkv", SearchOption.AllDirectories).Where(filePath => !filePath.Contains("\\MasteredFiles\\")).ToList();
        }

        private RootObject? QueryMkvFile(string filePath)
        {
            try
            {
                var jsonOut = QueryMkvFileToJson(filePath);
                return JsonSerializer.Deserialize<RootObject>(jsonOut);
            }
            catch (Exception ex)
            {
                WriteOutputLine($"An exception occured deserializing the JSON output from MKVMerge: {ex}");
                return null;
            }
        }

        private string QueryMkvFileToJson(string filePath)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "C:\\Program Files\\MKVToolNix\\mkvmerge.exe",
                        Arguments = $"--identification-format json --identify \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                
                var standardOuput = process.StandardOutput.ReadToEnd();
                var standardErrorOutput = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(standardErrorOutput))
                {
                    WriteOutputLine($"An error occured with mkvmerge.exe identify: {standardErrorOutput}");
                }
                process.WaitForExit();
                return standardOuput;
            }
            catch (Exception ex)
            {
                WriteOutputLine($"An exception occured requesting JSON from MKVMerge: {ex}");
                return "";
            }
        }

        private void WriteOutputLine(string text)
        {
            Dispatcher.Invoke(() =>
            {
                OutputTextBox.Text += $"{DateTime.Now} - {text}\r\n";
                OutputTextBox.ScrollToEnd();
            });
        }

        private void AnalyseButton_Click(object sender, RoutedEventArgs e)
        {
            AnalyseMkvFiles();
        }

        private async void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                WriteOutputLine("Batching process has started...");

                // Force mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);

                var OutputPath = "\\MasteredFiles\\";
                var mergeCommandString = "";

                // audio tracks.    Example:    --audio-tracks 2
                var audioTracks = TrackList.Where(x => x.Type == "audio" && x.Include);
                if (audioTracks.Any())
                {
                    mergeCommandString += $"--audio-tracks {string.Join(",", audioTracks.Select(x => x.Id))} ";
                }

                // Subtitle tracks. Example:    --subtitle-tracks 3,4
                var subtitleTracks = TrackList.Where(x => x.Type == "subtitles" && x.Include);
                if (subtitleTracks.Any())
                {
                    mergeCommandString += $"--subtitle-tracks {string.Join(",", subtitleTracks.Select(x => x.Id))} ";
                }

                // Name tracks.     Example:    --track-name ID:NewName
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--track-name {x.Id}:\"{x.Name}\"")) + " ";

                // Default tracks   Example:    --default-track 1:no
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--default-track {x.Id}:{(x.Default ? "yes" : "no")}")) + " ";

                // Forced tracks    Example:    --forced-track 1:no
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--forced-track {x.Id}:{(x.Forced ? "yes" : "no")}")) + " ";


                foreach (var filePath in FileList.Where(x => x.Include))
                {

                    try
                    {
                        WriteOutputLine($"Writing  \"{OutputPath}\\{Path.GetFileName(filePath.FilePath)}\"");

                        var mkvMergeArgument = $"-o \"{Path.GetDirectoryName(filePath.FilePath)}\\{OutputPath}\\{Path.GetFileName(filePath.FilePath)}\" {mergeCommandString} \"{filePath.FilePath}\"";

                        using var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "C:\\Program Files\\MKVToolNix\\mkvmerge.exe",
                                Arguments = mkvMergeArgument,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            }
                        };

                        process.OutputDataReceived += (sender, e) => {
                            if (e.Data != null)
                            {
                                WriteOutputLine(e.Data);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.WaitForExit();

                        var standardError = process.StandardError.ReadToEnd();
                        if (!string.IsNullOrEmpty(standardError))
                        {
                            WriteOutputLine(standardError);
                        }
                        WriteOutputLine($"Writing Complete  \"{OutputPath}\\{Path.GetFileName(filePath.FilePath)}\"");
                    }
                    catch (Exception ex)
                    {
                        WriteOutputLine($"An exception occured attempting to invoke mkvmerge for {filePath}: {ex}");
                    }

                }

                // Restore mouse cursor
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);

                WriteOutputLine("Batching process has completed!");
                PlayNotificationSound();
            });
        }

        private void PlayNotificationSound()
        {
            var notificationSound = new SoundPlayer(GetType().Assembly.GetManifestResourceStream("MKVToolNixWrapper.Assets.SH3Menu.wav"));
            notificationSound.Play();
        }

        private void PlayIntroSound()
        {
            var notificationSound = new SoundPlayer(GetType().Assembly.GetManifestResourceStream("MKVToolNixWrapper.Assets.SH2Menu.wav"));
            notificationSound.Play();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNotificationSound();

            using (var fileStream = File.Create($"{Path.GetTempPath()}\\Info.txt"))
            {
                GetType().Assembly.GetManifestResourceStream("MKVToolNixWrapper.Assets.Info.txt")!.CopyTo(fileStream);
            }

            Process.Start("notepad", $"{Path.GetTempPath()}\\Info.txt");
        }

        private void SelectAllTrackButton_Click(object sender, RoutedEventArgs e)
        {
            TrackList.ForEach(item => item.Include = true);
            TrackGrid.ItemsSource = null;
            TrackGrid.ItemsSource = TrackList;
        }

        private void SelectNoneTrackButton_Click(object sender, RoutedEventArgs e)
        {
            TrackList.ForEach(item => item.Include = false);
            TrackGrid.ItemsSource = null;
            TrackGrid.ItemsSource = TrackList;
        }

        private void SelectAllFileButton_Click(object sender, RoutedEventArgs e)
        {
            FileList.ForEach(item => item.Include = true);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileList;
        }

        private void SelectNoneFileButton_Click(object sender, RoutedEventArgs e)
        {
            FileList.ForEach(item => item.Include = false);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileList;
        }

        private void InvertTrackButton_Click(object sender, RoutedEventArgs e)
        {
            FileList.ForEach(item => item.Include = !item.Include);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileList;
        }

        private void FileListBoxCheckBox_Click(object sender, RoutedEventArgs e)
        {
            TrackGrid.ItemsSource = null;
            TrackGrid.IsEnabled = false;
            BatchButton.IsEnabled = false;
        }
    }
}
