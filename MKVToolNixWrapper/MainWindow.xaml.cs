using MKVToolNixWrapper.Dtos;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace MKVToolNixWrapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string SourceFolderPath { get; set; } = "";
        private List<string> SourceMkvPaths { get; set; } = new List<string>();

        private List<RootObject> MkvInfoPassList { get; set; } = new List<RootObject>();
        private List<RootObject> MkvInfoFailList { get; set; } = new List<RootObject>();

        public List<FileList> FileList { get; set; }
        public List<TrackList> TrackList { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;
        }


        private async void BrowseFolderHandler(string path)
        {
            BrowseFolderButton.IsEnabled = false;

            // Prevent blocking of the UI thread
            await Task.Run(() =>
            {
                SourceMkvPaths = GetMkvFilesInFolder(path);
                if (SourceMkvPaths.Count == 0)
                {
                    System.Windows.MessageBox.Show("Unable to locate .mkv files in the selected folder, try again.", "Directory error", MessageBoxButton.OK, MessageBoxImage.Error);
                    WriteOutputLine($"Error: Unable to locate .mkv files in the selected folder: \"{path}\"");
                    Dispatcher.Invoke(() => SelectedFolderPathLabel.Content = "");
                }

                // Path is valid, update UI label
                SourceFolderPath = path;
                // Execute on the UI thread
                Dispatcher.Invoke(() => SelectedFolderPathLabel.Content = path);


                WriteOutputLine($"Source folder successfully validated - MKV count: {SourceMkvPaths.Count()}");
                // Enable anaylse button
                Dispatcher.Invoke(() => AnalyseButton.IsEnabled = true);

                //System.Windows.MessageBox.Show("The selected folder will now be analysed, this could take a minute or so", "Information", MessageBoxButton.OK, MessageBoxImage.Information);

                // Once user selects the folder we populate a multi select list of the MKV files it found in the directory.
                Dispatcher.Invoke(() => FileList = SourceMkvPaths.Select(x => new FileList() { FilePath = x, Include = true }).ToList());


                // Then give the user a button to 'Analyse' the selected files
                // Based on the outcome of this we either proceed
                // OR deselect the problem files in the multiselect.
                Dispatcher.Invoke(() => FileListBox.ItemsSource = this.FileList);

                // Next step is two multiseleect lists -> Audio track & Subtitle track
                // Once the user is happy with the selection of tracks we can generate the command to mkvmerge 
                // Then loop over files spitting them into a output directory

                // Start processing
                //AnalyseMkvFiles();

                //WriteOutputLine($"Analysis complete.");

                Dispatcher.Invoke(() => BrowseFolderButton.IsEnabled = true);
            });
        }

        private void AnalyseMkvFiles()
        {
            TrackGrid.ItemsSource = null;
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
                    WriteOutputLine($"SKIP - \"{Path.GetFileName(fileItem.FilePath)}\" marked as comparison point");
                    CompareFileItem = fileItem;
                    CompareMkvSubTracks = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    CompareMkvAudioTracks = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    CompareMkvVideoTracks = MKVInfo.tracks.Where(x => x.type == "video").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();

                    // Using track info from first file populate the grid
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
                    //TrackGrid.ItemsSource = TrackList;

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

            if (fail)
            {
                WriteOutputLine($"Analysis outcome: FAIL");
                WriteOutputLine($"Explanation: Unable to unlock batching as selected files have differing sub/audio/video track setup, proceeding would result in missmatched tracks");
                WriteOutputLine($"Resolution: Deselect the outliers and process them on their own. Once all selected files PASS the batch button is enabled");
            }
            else
            {
                WriteOutputLine($"Analysis outcome: PASS");
                TrackGrid.ItemsSource = TrackList;
                TrackGrid.IsEnabled = true;
                BatchButton.IsEnabled = true;
            }

        }


        //private void analyse()
        //{
        //    var video = MKVInfo.tracks.Where(x => x.type == "video").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
        //    if (!CheckTracks("video", video, FirstMKVVideoTracks, file.FilePath))
        //    {
        //        file.Include = false;
        //        MkvInfoFailList.Add(MKVInfo);
        //        continue;
        //    }

        //    var audio = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
        //    if (!CheckTracks("audio", audio, FirstMKVAudioTracks, file.FilePath))
        //    {
        //        file.Include = false;
        //        MkvInfoFailList.Add(MKVInfo);
        //        continue;
        //    }

        //    var subs = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
        //    if (!CheckTracks("subtitles", subs, FirstMKVSubTracks, file.FilePath))
        //    {
        //        file.Include = false;
        //        MkvInfoFailList.Add(MKVInfo);
        //        continue;
        //    }
        //}

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
            return Directory.GetFiles(path, "*.mkv").ToList();
        }

        private RootObject? QueryMkvFile(string filePath)
        {
            var jsonOut = QueryMkvFileToJson(filePath);
            return JsonSerializer.Deserialize<RootObject>(jsonOut);
        }

        private string QueryMkvFileToJson(string filePath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "C:\\Program Files\\MKVToolNix\\mkvmerge.exe",
                    Arguments = $"--identification-format json --identify \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            //WriteOutputLine($"mkvmerge processed: \"{filePath}\"");
            return output;
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

        private void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            //TODO: Block this from being called until we have a passed analyse

            // Take the tracklist and from this generate the command line arguments 


            var OutputPath = SourceFolderPath + "\\MergedOutput";
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
            mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--forced-track {x.Id}:{(x.Default ? "yes" : "no")}")) + " ";

            
            foreach(var filePath in FileList.Where(x => x.Include))
            {
                var mkvMergeArgument = $"-o \"{OutputPath}\\{Path.GetFileName(filePath.FilePath)}\" {mergeCommandString} \"{filePath.FilePath}\"";
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "C:\\Program Files\\MKVToolNix\\mkvmerge.exe",
                        Arguments = mkvMergeArgument,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
            }


        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;

            var openFileDlg = new FolderBrowserDialog();
            var result = openFileDlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                WriteOutputLine($"Source folder selected: \"{openFileDlg.SelectedPath}\"");
                BrowseFolderHandler(openFileDlg.SelectedPath);
            }
            else
            {
                SourceFolderPath = "";
                SelectedFolderPathLabel.Content = "";
            }
        }
    }
}
