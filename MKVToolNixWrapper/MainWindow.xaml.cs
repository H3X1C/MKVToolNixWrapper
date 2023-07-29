using MKVToolNixWrapper.Dtos;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace MKVToolNixWrapper
{
    // TODO:
    // Add support for re-id'ing the tracks, some uploaders randomly change ordering mid season like english becomes track 3 suddenly
    public partial class MainWindow : Window
    { 
        private List<FileMeta> FileMetaList { get; set; } = new List<FileMeta>();
        private List<TrackListMeta> TrackList { get; set; } = new List<TrackListMeta>();
        private static string MkvMergePath = "C:\\Program Files\\MKVToolNix\\mkvmerge.exe";
        private List<int> ProcessIdTracker { get; set; } = new List<int>();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Title = $"MKVToolNixWrapper v{Assembly.GetExecutingAssembly().GetName().Version}";
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WriteOutputLine($"Welcome to MKVToolNixWrapper v{Assembly.GetExecutingAssembly().GetName().Version}");
            DataContext = this;
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;
            PlayIntroSound();
            MkvMergeExistsCheck();
            StartPulsing(BrowseFolderButton, 2000);
            StartPulsing(SelectedFolderPathLabel, 2000);
            TaskbarItemInfo.ProgressValue = 100;
        }

        private void MkvMergeExistsCheck()
        {
            // Load "MkvMergePath" from the user-level configuration file if it exists
            var roaming = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            var fileMap = new ExeConfigurationFileMap();
            fileMap.ExeConfigFilename = roaming.FilePath;
            var config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            var appSettingsMkvMergePath = config?.AppSettings?.Settings["MkvMergePath"]?.Value;
            MkvMergePath = string.IsNullOrEmpty(appSettingsMkvMergePath) ? MkvMergePath : appSettingsMkvMergePath;

            if (File.Exists(MkvMergePath))
            {
                WriteOutputLine($"Succesfully located MKVMerge at: \"{MkvMergePath}\"");
            }
            else
            {
                MessageBox.Show($"Unable to locate mkvmerge.exe\r\nPlease click OK and locate your mkvmerge.exe", "Failed to locate MKVMerge", MessageBoxButton.OK, MessageBoxImage.Error);
                WriteOutputLine($"Failed to locate MKVMerge at: \"{MkvMergePath}\" prompting user for location");

                if (!SetMkvMergePath())
                {
                    ToggleUI(false);
                }
            }
        }

        private bool SetMkvMergePath()
        {
            var openFileDlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Locate your mkvmerge.exe",
                Filter = "Executable Files|*.exe",
                FileName = "mkvmerge.exe",
                CheckFileExists = true,
                CheckPathExists = true
            };

            var result = openFileDlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                if (Path.GetFileName(openFileDlg.FileName).Equals("mkvmerge.exe"))
                {
                    // Save to config
                    var roaming = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
                    var fileMap = new ExeConfigurationFileMap();
                    fileMap.ExeConfigFilename = roaming.FilePath;
                    var config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
                    if (config.AppSettings.Settings["MkvMergePath"] == null)
                    {
                        config.AppSettings.Settings.Add("MkvMergePath", openFileDlg.FileName);
                    }
                    else
                    {
                        config.AppSettings.Settings["MkvMergePath"].Value = openFileDlg.FileName;
                    }
                    config.Save(ConfigurationSaveMode.Modified);

                    // Update path
                    MkvMergePath = openFileDlg.FileName;
                    WriteOutputLine($"Succesfully located MKVMerge at: \"{MkvMergePath}\"");
                    return true;
                }
                else
                {
                    MessageBox.Show($"You have selected an invalid executable\r\nEnsure the file is correctly named 'mkvmerge.exe'\r\nPlease restart the application and try again\r\n\r\nClick on 'Help' if you need more information on MkvMerge", "Failed to locate MKVMerge", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                SetMkvMergePath();
            }

            return false;
        }

        private static async void StartPulsing(UIElement element, int durationMs)
        {
            // Setup storyboard + animation
            var storyboard = new Storyboard();
            var opacityAnimation1 = new DoubleAnimation()
            {
                From = 1.0,
                To = 0.2,
                Duration = new Duration(TimeSpan.FromSeconds(0.5)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            // Assign to passed in element
            storyboard.Children.Add(opacityAnimation1);
            Storyboard.SetTarget(opacityAnimation1, element);
            Storyboard.SetTargetProperty(opacityAnimation1, new PropertyPath(UIElement.OpacityProperty));

            // Start animation and stop it after the passed in duration
            storyboard.Begin();
            await Task.Delay(durationMs);
            storyboard.Stop();
            element.Opacity = 1;
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            TrackGrid.IsEnabled = false;
            AnalyseButton.IsEnabled = false;
            BatchButton.IsEnabled = false;
            TrackGrid.ItemsSource = null;
            FileListBox.ItemsSource = null;

            var openFileDlg = new System.Windows.Forms.FolderBrowserDialog();
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
            try
            {
                WriteOutputLine($"Source folder selected: \"{path}\"");
                BrowseFolderButton.IsEnabled = false;

                // Show mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = Cursors.Wait);

                var mkvFiles = await Task.Run(() => GetMkvFilesInFolder(path));
                if (mkvFiles.Count == 0)
                {
                    MessageBox.Show("Unable to locate .mkv files in the selected folder, please select a valid folder.\r\n\r\nNote: 'MasteredFiles' folder is reserved and not included in the search!", "Directory error", MessageBoxButton.OK, MessageBoxImage.Error);
                    WriteOutputLine($"Error: Unable to locate .mkv files in the selected folder: \"{path}\"");
                    SelectedFolderPathLabel.Content = "";
                }
                else
                {
                    SelectedFolderPathLabel.Content = path;
                    WriteOutputLine($"Source folder successfully validated - MKV count: {mkvFiles.Count()}");
                    WriteOutputLine();
                    AnalyseButton.IsEnabled = true;
                    StartPulsing(AnalyseButton, 2000);
                    // Populate file list
                    FileMetaList = mkvFiles.Select(x => new FileMeta() { FilePath = x, Include = true, Status = FileStatusEnum.Unprocessed }).ToList();
                    FileListBox.ItemsSource = FileMetaList;
                }
            }
            finally
            {
                // Hide mouse spinner and enable browse button
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
                BrowseFolderButton.IsEnabled = true;
            }
        }

        private async void AnalyseMkvFiles()
        {
            await Task.Run(() =>
            {
                WriteOutputLine("**** ANALYSIS START ****");
                Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate);

                // Clear any previously processes track items
                TrackList = new List<TrackListMeta>();
                Dispatcher.Invoke(() => ToggleUI(false));

                // Clear an failed analysis files
                foreach (var file in FileMetaList)
                {
                    if (file.Status == FileStatusEnum.FailedAnalysis)
                    {
                        file.Status = FileStatusEnum.Unprocessed;
                    }
                }
                ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);

                // Force mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = Cursors.Wait);

                // Clear Track grid on the UI
                Dispatcher.Invoke(() => TrackGrid.ItemsSource = null);

                // Get set of files which are checked in the UI
                var includedFiles = FileMetaList.Where(x => x.Include).ToList();

                // Analyze each file against comparison file
                var allPassed = true;
                FileMeta? CompareFileItem = null;
                var CompareMkvSubTracks = new List<Track>();
                var CompareMkvAudioTracks = new List<Track>();
                var CompareMkvVideoTracks = new List<Track>();
                foreach(var fileItem in FileMetaList.Where(x => x.Include).ToList())
                {
                    var MKVInfo = QueryMkvFile(fileItem.FilePath);
                    if (MKVInfo == null)
                    {
                        WriteOutputLine($"FAIL - \"{Path.GetFileName(fileItem.FilePath)}\" is not a valid mkv file");
                        fileItem.Status = FileStatusEnum.FailedAnalysis;
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
                        continue;
                    }

                    // Attachment info dump
                    var attachmentCount = MKVInfo?.attachments.Count();
                    var coverRegex = new Regex(@"cover.*\.(png|jpg)$", RegexOptions.IgnoreCase);
                    var coverArtAttachments = MKVInfo?.attachments.Where(x => coverRegex.IsMatch(x.file_name));
                    if(attachmentCount > 0)
                    {
                        WriteOutputLine($"Attachment(s) Found: {attachmentCount} {(coverArtAttachments?.Count() > 0 ? $"- Cover Art Found: {coverArtAttachments?.Count()}" : "")}");
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
                            .Select(x => new TrackListMeta
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
                        allPassed = false;
                        fileItem.Status = FileStatusEnum.FailedAnalysis;
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
                        continue;
                    }

                    var curMkvAudioTracks = MKVInfo.tracks.Where(x => x.type == "audio").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    if (!CheckTracks("audio", fileItem.FilePath, curMkvAudioTracks, CompareFileItem.FilePath, CompareMkvAudioTracks))
                    {
                        allPassed = false;
                        fileItem.Status = FileStatusEnum.FailedAnalysis;
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
                        continue;
                    }

                    var curMkvSubTracks = MKVInfo.tracks.Where(x => x.type == "subtitles").OrderBy(y => y.id).ThenBy(z => z.properties.track_name).ToList();
                    if (!CheckTracks("subtitles", fileItem.FilePath, curMkvSubTracks, CompareFileItem.FilePath, CompareMkvSubTracks))
                    {
                        allPassed = false;
                        fileItem.Status = FileStatusEnum.FailedAnalysis;
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
                        continue;
                    }

                    WriteOutputLine($"PASS - \"{Path.GetFileName(fileItem.FilePath)}\"");
                    fileItem.Status = FileStatusEnum.PassedAnalysis;
                    ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
                }

                // unset mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);
                // Unlock ui
                Dispatcher.Invoke(() => ToggleUI(true));

                if (allPassed)
                {
                    ForceSetControlItemsSourceBinding(TrackGrid, TrackList);
                    Dispatcher.Invoke(() => TrackGrid.IsEnabled = true);
                    Dispatcher.Invoke(() => BatchButton.IsEnabled = true);
                    Dispatcher.Invoke(() => StartPulsing(BatchButton, 2000));
                    WriteOutputLine("Analysis Completed - Outcome: PASS");
                    Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal);
                }
                else
                {
                    Dispatcher.Invoke(() => BatchButton.IsEnabled = false);
                    Dispatcher.Invoke(() => TrackGrid.IsEnabled = false);
                    WriteOutputLine("Analysis Completed - Outcome: FAIL");
                    WriteOutputLine("Explanation: Unable to unlock batching as the selected files have differing sub/audio/video track setup, proceeding would result in missmatched tracks");
                    WriteOutputLine("Resolution: Deselect the MKV's that have FAILED and process them on their own. Only once all selected files PASS is the batch button unlocked");
                    SystemSounds.Hand.Play();
                    Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error);
                }

                WriteOutputLine($"**** ANALYSIS END ****");
                WriteOutputLine();
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

        private static List<string> GetMkvFilesInFolder(string path)
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
                ProcessIdTracker.Add(process.Id);

                var standardOuput = process.StandardOutput.ReadToEnd();
                var standardErrorOutput = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(standardErrorOutput))
                {
                    WriteOutputLine($"An error occured with mkvmerge.exe identify: {standardErrorOutput}");
                }
                process.WaitForExit();
                ProcessIdTracker.Remove(process.Id);
                return standardOuput;
            }
            catch (Exception ex)
            {
                WriteOutputLine($"An exception occured requesting JSON from MKVMerge: {ex}");
                return "";
            }
        }

        private void WriteOutputLine(string text = "", bool replaceLastLine = false)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (replaceLastLine)
                    {
                        int secondToLastIndex = OutputTextBox.Text.LastIndexOf("\r\n", OutputTextBox.Text.Length - 3);
                        if (secondToLastIndex >= 0)
                        {
                            OutputTextBox.Text = OutputTextBox.Text.Substring(0, secondToLastIndex + 2);
                        }
                    }

                    if (string.IsNullOrEmpty(text))
                    {
                        OutputTextBox.Text += "\r\n";
                    }
                    else
                    {
                        OutputTextBox.Text += $"{DateTime.Now} - {text}\r\n";
                    }
                    OutputTextBox.ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to write to output window: {ex.Message}");
            }
        }

        private void AnalyseButton_Click(object sender, RoutedEventArgs e)
        {
            AnalyseMkvFiles();
        }

        private async void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleUI(false);
            await Task.Run(() =>
            {
                WriteOutputLine("**** BATCH START ****");
                // Taskbar - In Progress
                Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate);

                // Force mouse spinner
                Dispatcher.Invoke(() => Mouse.OverrideCursor = Cursors.Wait);

                var OutputPath = "\\MasteredFiles\\";
                var mergeCommandString = "";

                var videoTracks = TrackList.Where(x => x.Type == "video" && x.Include);
                if (videoTracks.Any())
                {
                    mergeCommandString += $"--video-tracks {string.Join(",", videoTracks.Select(x => x.Id))} ";
                }
                else
                {
                    mergeCommandString += "--no-video ";
                }

                var audioTracks = TrackList.Where(x => x.Type == "audio" && x.Include);
                if (audioTracks.Any())
                {
                    mergeCommandString += $"--audio-tracks {string.Join(",", audioTracks.Select(x => x.Id))} ";
                }
                else
                {
                    mergeCommandString += "--no-audio ";
                }

                var subtitleTracks = TrackList.Where(x => x.Type == "subtitles" && x.Include);
                if (subtitleTracks.Any())
                {
                    mergeCommandString += $"--subtitle-tracks {string.Join(",", subtitleTracks.Select(x => x.Id))} ";
                }
                else
                {
                    mergeCommandString += "--no-subtitles ";
                }

                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--track-name {x.Id}:\"{x.Name}\"")) + " ";
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--default-track {x.Id}:{(x.Default ? "yes" : "no")}")) + " ";
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--forced-track {x.Id}:{(x.Forced ? "yes" : "no")}")) + " ";
                mergeCommandString += string.Join(" ", TrackList.Where(x => x.Include).Select(x => $"--language {x.Id}:{x.Language}")) + " ";

                if (Dispatcher.Invoke(() => (AttachmentsCheckbox.IsChecked == true)))
                {
                    mergeCommandString += "--no-attachments ";
                }

                foreach (var filePath in FileMetaList.Where(x => x.Include))
                {
                    try
                    {
                        var outputFilePath = $"{Path.GetDirectoryName(filePath.FilePath)}\\{OutputPath}\\{Path.GetFileName(filePath.FilePath)}";
                        var mkvMergeArgument = $"-o \"{outputFilePath}\" {mergeCommandString} \"{filePath.FilePath}\"";

                        // Inform user
                        WriteOutputLine($"Writing  \"{outputFilePath}\"");
                        filePath.Status = FileStatusEnum.WritingFile;
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);

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

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (e.Data != null)
                            {
                                // If it's a progress line (but not 0%) we set replace last line to true
                                WriteOutputLine(e.Data, Regex.IsMatch(e.Data, @"^Progress: [1-9]\d*%$"));
                            }
                        };

                        process.Start();
                        ProcessIdTracker.Add(process.Id);
                        process.BeginOutputReadLine();
                        process.WaitForExit();
                        ProcessIdTracker.Remove(process.Id);

                        var standardError = process.StandardError.ReadToEnd();
                        if (string.IsNullOrEmpty(standardError) && File.Exists(outputFilePath) && process.ExitCode == 0)
                        {
                            filePath.Status = FileStatusEnum.WrittenFile;
                            WriteOutputLine($"Writing Complete  \"{outputFilePath}\"");
                        }
                        else
                        {
                            filePath.Status = FileStatusEnum.Error;
                            WriteOutputLine($"Writing Error! - Please review the output for details {standardError}");
                            SystemSounds.Hand.Play();
                            // Taskbar - Error
                            Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error);
                        }
                        WriteOutputLine();
                        ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);

                    }
                    catch (Exception ex)
                    {
                        WriteOutputLine($"An exception occured attempting to invoke mkvmerge for {filePath}: {ex}");
                    }

                }

                // Restore mouse cursor
                Dispatcher.Invoke(() => Mouse.OverrideCursor = null);

                // Taskbar - Success
                Dispatcher.Invoke(() => TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal);

                PlayNotificationSound();

                WriteOutputLine("**** BATCH END ****");
            });
            ToggleUI(true);
        }

        // ToDo: Instead of a dumb toggle have a enum that dictates stage, dependent on the stage activate x,y,z ui element
        private void ToggleUI(bool enable)
        {
            TrackGrid.IsEnabled = enable;
            BrowseFolderButton.IsEnabled = enable;
            AnalyseButton.IsEnabled = enable;
            BatchButton.IsEnabled = enable;
            InvertFileButton.IsEnabled = enable;
            SelectAllFileButton.IsEnabled = enable;
            SelectAllTrackButton.IsEnabled = enable;
            SelectNoneFileButton.IsEnabled = enable;
            SelectNoneTrackButton.IsEnabled = enable;
            DeselectFailsButton.IsEnabled = enable;
            SelectUnprocessedButton.IsEnabled = enable;
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Check if the edited column is the language code column
            if (e.Column == LanguageCodeColumn)
            {
                // Get the edited text box
                var textBox = e.EditingElement as TextBox;

                // Validate the language code
                if (!IsValidLanguageCode(textBox.Text))
                {
                    // Cancel the edit and show an error message
                    e.Cancel = true;
                    MessageBox.Show($"The language code \"{textBox.Text}\" is invalid.\r\nPlease enter a valid ISO 639-2 language code.", "Invalid language code", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static bool IsValidLanguageCode(string code)
        {
            if (!Regex.IsMatch(code, @"^[a-z]{3}$"))
            {
                return false;
            }

            try
            {
                CultureInfo.GetCultureInfoByIetfLanguageTag(code);
                return true;
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
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

            var process = Process.Start("notepad", $"{Path.GetTempPath()}\\Info.txt");
            process.Dispose();
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
            FileMetaList.ForEach(item => item.Include = true);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileMetaList;
        }

        private void SelectNoneFileButton_Click(object sender, RoutedEventArgs e)
        {
            FileMetaList.ForEach(item => item.Include = false);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileMetaList;
        }

        private void InvertFileButton_Click(object sender, RoutedEventArgs e)
        {
            FileMetaList.ForEach(item => item.Include = !item.Include);
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = FileMetaList;
        }

        private void FileListBoxCheckBox_Click(object sender, RoutedEventArgs e)
        {
            TrackGrid.ItemsSource = null;
            TrackGrid.IsEnabled = false;
            BatchButton.IsEnabled = false;
        }

        private void DeselectFailsButton_Click(object sender, RoutedEventArgs e)
        {
            FileMetaList.ForEach(x =>
            {
                if (x.Status == FileStatusEnum.FailedAnalysis)
                {
                    x.Include = false;
                }
            });
            ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
        }

        private void SelectUnprocessedButton_Click(object sender, RoutedEventArgs e)
        {
            FileMetaList.ForEach(x =>
            {
                if (x.Status == FileStatusEnum.Unprocessed)
                {
                    x.Include = true;
                }
            });
            ForceSetControlItemsSourceBinding(FileListBox, FileMetaList);
        }

        // Hack to avoid using INotifyPropertyChanged as it's messy to implement and restricts us to only using ObservableCollection :/
        // Nulling the item source and re-assigning it our collection effectively re-syncs with the UI. Aware this is not great but the 'offical' solution is some what to be desired.
        private void ForceSetControlItemsSourceBinding<T>(Control control, List<T> items)
        {
            Dispatcher.Invoke(() =>
            {
                if (control is ListBox listBox)
                {
                    listBox.ItemsSource = null;
                    listBox.ItemsSource = items;
                }
                else if (control is DataGrid dataGrid)
                {
                    dataGrid.ItemsSource = null;
                    dataGrid.ItemsSource = items;
                }
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Check for any running mkvmerge processes we started and kill them
            foreach (int processId in ProcessIdTracker)
            {
                try
                {
                    Process process = Process.GetProcessById(processId);
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to kill process with ID {processId}: {ex.Message}");
                }
            }
            
            base.OnClosing(e);
        }
    }
}