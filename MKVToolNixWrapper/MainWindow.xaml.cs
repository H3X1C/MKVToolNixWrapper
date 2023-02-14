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
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
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
            // TODO: Have this automatically de-select files that failFileList.Where(x => x.Include)

            var FirstMKVSubTracks = new List<Track>();
            var FirstMKVAudioTracks = new List<Track>();

            for (int i = 0; i < FileList.Where(x => x.Include).ToList().Count; i++)
            {
                var reject = false;
                var MKVInfo = QueryMkvFile(FileList.Where(x => x.Include).ToList()[i].FilePath);
                if (MKVInfo == null)
                {
                    WriteOutputLine($"FAIL - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[i].FilePath)}\" is not a valid mkv file");
                    FileList.Where(x => x.Include).ToList()[i].Include = false;
                    // We need to error message this to the user
                    continue;
                }

                // Use first file as a comparison point
                if (i == 0)
                {
                    FirstMKVSubTracks = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    FirstMKVAudioTracks = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    WriteOutputLine($"SKIP - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[i].FilePath)}\" marked as comparison point");

                    TrackList = MKVInfo.tracks.OrderBy(y => y.id).ThenBy(z => z.properties.track_name).Select(x => new TrackList { 
                        Id = x.id,
                        Name = x.properties.track_name,
                        Language = x.properties.language,
                        Type = x.type,
                        Codec = x.properties.codec_id,
                        Include = true,
                        Default = x.properties.default_track,
                        Forced = x.properties.forced_track
                    }).ToList();

                    TrackGrid.ItemsSource = TrackList;

                    continue;
                }

                // Compare subtitle track counts against comparison file
                var subs = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                if (subs.Count != FirstMKVSubTracks.Count)
                {
                    //REJECT
                    reject = true;
                    WriteOutputLine($"FAIL - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[i].FilePath)}\" due to differing sub track count compared to \"{Path.GetFileName(FileList.Where(x => x.Include).ToList().First().FilePath)}\": {subs.Count} vs {FirstMKVSubTracks.Count}");
                    FileList.Where(x => x.Include).ToList()[i].Include = false;
                    MkvInfoFailList.Add(MKVInfo);
                    continue;
                }

                // Iterate over the given files subtitle tracks to compare granular detail
                for (int j = 0; j < subs.Count; j++)
                {
                    if (subs[j].properties.language != FirstMKVSubTracks[j].properties.language)
                    {
                        //REJECT
                        reject = true;
                        WriteOutputLine($"FAIL - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[j].FilePath)}\" due to sub track {j} having a different lang flag compared to \"{Path.GetFileName(FileList.Where(x => x.Include).ToList().First().FilePath)}\": {subs[j].properties.language} vs {FirstMKVSubTracks[j].properties.language}");
                        FileList.Where(x => x.Include).ToList()[i].Include = false;
                        MkvInfoFailList.Add(MKVInfo);
                        break;
                    }

                    if (subs[j].properties.track_name != FirstMKVSubTracks[j].properties.track_name)
                    {
                        //REJECT
                        reject = true;
                        WriteOutputLine($"FAIL - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[i].FilePath)}\" due to sub track {j} having a different track name compared to \"{Path.GetFileName(SourceMkvPaths.First())}\": {subs[j].properties.track_name} vs {FirstMKVSubTracks[j].properties.track_name}");
                        FileList.Where(x => x.Include).ToList()[i].Include = false;
                        MkvInfoFailList.Add(MKVInfo);
                        break;
                    }
                }

                // Only add to pass list if no rejection flag triggered
                if (!reject)
                {
                    WriteOutputLine($"PASS - \"{Path.GetFileName(FileList.Where(x => x.Include).ToList()[i].FilePath)}\"");
                    MkvInfoPassList.Add(MKVInfo);
                }
            }


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

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Test");
        }

        private void AnalyseButton_Click(object sender, RoutedEventArgs e)
        {
            AnalyseMkvFiles();

            WriteOutputLine($"Analysis complete.");
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
    }
}
