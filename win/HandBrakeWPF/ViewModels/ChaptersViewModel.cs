﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ChaptersViewModel.cs" company="HandBrake Project (http://handbrake.fr)">
//   This file is part of the HandBrake source code - It may be used under the terms of the GNU General Public License.
// </copyright>
// <summary>
//   The Chapters View Model
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace HandBrakeWPF.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;

    using Caliburn.Micro;
    using HandBrake.Model.Prompts;
    using HandBrakeWPF.EventArgs;
    using HandBrakeWPF.Properties;
    using HandBrakeWPF.Services.Interfaces;
    using HandBrakeWPF.Services.Presets.Model;
    using HandBrakeWPF.Services.Scan.Model;
    using HandBrakeWPF.Utilities.Input;
    using HandBrakeWPF.Utilities.Output;
    using HandBrakeWPF.ViewModels.Interfaces;
    using PlatformBindings;
    using PlatformBindings.Models.FileSystem;
    using ChapterMarker = HandBrakeWPF.Services.Encode.Model.Models.ChapterMarker;
    using EncodeTask = HandBrakeWPF.Services.Encode.Model.EncodeTask;
    using GeneralApplicationException = HandBrakeWPF.Exceptions.GeneralApplicationException;

    /// <summary>
    /// The Chapters View Model
    /// </summary>
    public class ChaptersViewModel : ViewModelBase, IChaptersViewModel
    {
        #region Constants and Fields

        private readonly IErrorService errorService;

        /// <summary>
        /// The source chapters backing field
        /// </summary>
        private List<Chapter> sourceChaptersList;

        #endregion Constants and Fields

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ChaptersViewModel"/> class.
        /// </summary>
        /// <param name="windowManager">
        /// The window manager.
        /// </param>
        /// <param name="userSettingService">
        /// The user Setting Service.
        /// </param>
        /// <param name="errorService">
        /// The Error Service
        /// </param>
        public ChaptersViewModel(IWindowManager windowManager, IUserSettingService userSettingService, IErrorService errorService)
        {
            this.Task = new EncodeTask();
            this.errorService = errorService;
        }

        public event EventHandler<TabStatusEventArgs> TabStatusChanged;

        #endregion Constructors and Destructors

        #region Public Properties

        /// <summary>
        /// Gets or sets Task.
        /// </summary>
        public EncodeTask Task { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether chapter markers are enabled.
        /// </summary>
        public bool IncludeChapterMarkers
        {
            get
            {
                return this.Task.IncludeChapterMarkers;
            }
            set
            {
                this.Task.IncludeChapterMarkers = value;
                this.NotifyOfPropertyChange(() => this.IncludeChapterMarkers);
                this.OnTabStatusChanged(null);
            }
        }

        public ObservableCollection<ChapterMarker> Chapters
        {
            get
            {
                return this.Task.ChapterNames;
            }

            set
            {
                this.Task.ChapterNames = value;
                this.NotifyOfPropertyChange(() => this.Chapters);
            }
        }

        #endregion Public Properties

        #region Properties

        /// <summary>
        /// Gets or sets SourceChapterList.
        /// </summary>
        private ObservableCollection<Chapter> SourceChapterList { get; set; }

        #endregion Properties

        #region Public Methods

        /// <summary>
        /// Export the Chapter Markers to a CSV file
        /// </summary>
        /// <exception cref="GeneralApplicationException">
        /// Thrown if exporting fails.
        /// </exception>
        public void Export()
        {
            var properties = new FileSavePickerProperties();
            properties.FileTypes.Add(".csv");

            var file = AppServices.Current?.IO?.Pickers?.SaveFile(properties)?.Result;
            if (file == null)
            {
                // Exit if the user cancelled the dialog, or some other exception occurred.
                return;
            }

            try
            {
                using (var filestream = file.OpenAsStream(true).Result)
                {
                    using (var csv = new StreamWriter(filestream))
                    {
                        foreach (ChapterMarker row in this.Chapters)
                        {
                            csv.Write("{0},{1}{2}", row.ChapterNumber, CsvHelper.Escape(row.ChapterName), Environment.NewLine);
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                throw new GeneralApplicationException(
                    Resources.ChaptersViewModel_UnableToExportChaptersWarning,
                    Resources.ChaptersViewModel_UnableToExportChaptersMsg,
                    exc);
            }
        }

        /// <summary>
        /// Imports a Chapter marker file
        /// </summary>
        /// <exception cref="GeneralApplicationException">
        /// Thrown if importing fails.
        /// </exception>
        public void Import()
        {
            var properties = new FilePickerProperties();
            properties.FileTypes.Add(".csv");
            properties.FileTypes.Add(".tsv");
            properties.FileTypes.Add(".xml");
            properties.FileTypes.Add(".txt");

            var file = AppServices.Current?.IO?.Pickers?.PickFile(properties).Result;
            // Exit if the user cancelled the dialog, or some other exception occurred.
            if (file == null)
            {
                return;
            }

            var fileExtension = Path.GetExtension(file.Path);
            var importedChapters = new Dictionary<int, Tuple<string, TimeSpan>>();

            using (var filestream = file.OpenAsStream(false).Result)
            {
                // Execute the importer based on the file extension
                switch (fileExtension)
                {
                    case ".csv": // comma separated file
                    case ".tsv": // tab separated file
                        ChapterImporterCsv.Import(filestream, fileExtension, ref importedChapters);
                        break;

                    case ".xml":
                        ChapterImporterXml.Import(filestream, ref importedChapters);
                        break;

                    case ".txt":
                        ChapterImporterTxt.Import(filestream, ref importedChapters);
                        break;

                    default:
                        throw new GeneralApplicationException(
                            Resources.ChaptersViewModel_UnsupportedFileFormatWarning,
                            string.Format(Resources.ChaptersViewModel_UnsupportedFileFormatMsg, fileExtension));
                }
            }

            // Exit early if no chapter information was extracted
            if (importedChapters == null || importedChapters.Count <= 0)
                return;

            // Validate the chaptermap against the current chapter list extracted from the source
            string validationErrorMessage;
            if (!this.ValidateImportedChapters(importedChapters, out validationErrorMessage))
            {
                if (!string.IsNullOrEmpty(validationErrorMessage))
                {
                    throw new GeneralApplicationException(
                              Resources.ChaptersViewModel_ValidationFailedWarning,
                              validationErrorMessage);
                }

                // The user has cancelled the import, so exit
                return;
            }

            // Now iterate over each chatper we have, and set it's name
            foreach (ChapterMarker item in this.Chapters)
            {
                // If we don't have a chapter name for this chapter then
                // fallback to the value that is already set for the chapter
                string chapterName = item.ChapterName;

                // Attempt to retrieve the imported chapter name
                Tuple<string, TimeSpan> chapterInfo;
                if (importedChapters.TryGetValue(item.ChapterNumber, out chapterInfo))
                    chapterName = chapterInfo.Item1;

                // Assign the chapter name unless the name is not set or only whitespace charaters
                item.ChapterName = !string.IsNullOrWhiteSpace(chapterName) ? chapterName : string.Empty;
            }
        }

        /// <summary>
        /// Setup this window for a new source
        /// </summary>
        /// <param name="source">
        /// The source.
        /// </param>
        /// <param name="title">
        /// The title.
        /// </param>
        /// <param name="preset">
        /// The preset.
        /// </param>
        /// <param name="task">
        /// The task.
        /// </param>
        public void SetSource(Source source, Title title, Preset preset, EncodeTask task)
        {
            this.Task = task;

            if (preset != null)
            {
                this.IncludeChapterMarkers = preset.Task.IncludeChapterMarkers;
            }

            this.sourceChaptersList = title.Chapters;
            this.SetSourceChapters(title.Chapters);
        }

        /// <summary>
        /// Setup this tab for the specified preset.
        /// </summary>
        /// <param name="preset">
        /// The preset.
        /// </param>
        /// <param name="task">
        /// The task.
        /// </param>
        public void SetPreset(Preset preset, EncodeTask task)
        {
            this.Task = task;
            this.IncludeChapterMarkers = preset.Task.IncludeChapterMarkers;
            this.NotifyOfPropertyChange(() => this.Chapters);
        }

        /// <summary>
        /// Update all the UI controls based on the encode task passed in.
        /// </summary>
        /// <param name="task">
        /// The task.
        /// </param>
        public void UpdateTask(EncodeTask task)
        {
            this.Task = task;

            this.NotifyOfPropertyChange(() => this.IncludeChapterMarkers);
            this.NotifyOfPropertyChange(() => this.Chapters);
        }

        public bool MatchesPreset(Preset preset)
        {
            if (preset.Task.IncludeChapterMarkers != this.IncludeChapterMarkers)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reset Chapter Names
        /// </summary>
        public void Reset()
        {
            if (this.sourceChaptersList != null)
            {
                this.SetSourceChapters(this.sourceChaptersList);
            }
        }

        /// <summary>
        /// Set the Source Chapters List
        /// </summary>
        /// <param name="sourceChapters">
        /// The source chapters.
        /// </param>
        public void SetSourceChapters(IEnumerable<Chapter> sourceChapters)
        {
            // Cache the chapters in this screen
            this.SourceChapterList = new ObservableCollection<Chapter>(sourceChapters);
            this.Chapters.Clear();

            // Then Add new Chapter Markers.
            int counter = 1;

            foreach (Chapter chapter in this.SourceChapterList)
            {
                string chapterName = string.IsNullOrEmpty(chapter.ChapterName) ? string.Format("Chapter {0}", counter) : chapter.ChapterName;
                var marker = new ChapterMarker(chapter.ChapterNumber, chapterName, chapter.Duration);
                this.Chapters.Add(marker);

                counter += 1;
            }
        }

        #endregion Public Methods

        #region Private Methods

        protected virtual void OnTabStatusChanged(TabStatusEventArgs e)
        {
            this.TabStatusChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Validates any imported chapter information against the currently detected chapter information in the
        /// source media. If validation fails then an error message is returned via the out parameter <see cref="validationErrorMessage"/>
        /// </summary>
        /// <param name="importedChapters">The list of imported chapter information</param>
        /// <param name="validationErrorMessage">In case of a validation error this variable will hold
        ///                                      a detailed message that can be presented to the user</param>
        /// <returns>True if there are no errors with imported chapters, false otherwise</returns>
        private bool ValidateImportedChapters(Dictionary<int, Tuple<string, TimeSpan>> importedChapters, out string validationErrorMessage)
        {
            validationErrorMessage = null;

            // If the number of chapters don't match, prompt for confirmation
            if (importedChapters.Count != this.Chapters.Count)
            {
                if (this.errorService.ShowMessageBox(
                        string.Format(Resources.ChaptersViewModel_ValidateImportedChapters_ChapterCountMismatchMsg, this.Chapters.Count, importedChapters.Count),
                        Resources.ChaptersViewModel_ValidateImportedChapters_ChapterCountMismatchWarning,
                        DialogButtonType.YesNo,
                        DialogType.Question) !=
                    HandBrake.Model.Prompts.DialogResult.Yes)
                {
                    return false;
                }
            }

            // If the average discrepancy in timings between chapters is either:
            //   a) more than 15 sec for more than 2 chapters
            //      (I chose 15sec based on empirical evidence from testing a few DVDs and comparing to chapter-marker files I downloaded)
            //      => This check will not be performed for the first and last chapter as they're very likely to differ significantly due to language and region
            //         differences (e.g. longer title sequences and different distributor credits)
            var diffs = importedChapters.Zip(this.Chapters, (import, source) => source.Duration - import.Value.Item2);
            if (diffs.Count(diff => Math.Abs(diff.TotalSeconds) > 15) > 2)
            {
                if (this.errorService.ShowMessageBox(
                        Resources.ChaptersViewModel_ValidateImportedChapters_ChapterDurationMismatchMsg,
                        Resources.ChaptersViewModel_ValidateImportedChapters_ChapterDurationMismatchWarning,
                        DialogButtonType.YesNo,
                        DialogType.Question) !=
                    HandBrake.Model.Prompts.DialogResult.Yes)
                {
                    return false;
                }
            }

            // All is well, we should import chapters
            return true;
        }

        #endregion Private Methods
    }
}