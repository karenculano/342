﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ChapterImporterXml.cs" company="HandBrake Project (http://handbrake.fr)">
//   This file is part of the HandBrake source code - It may be used under the terms of the GNU General Public License.
// </copyright>
// <summary>
//   Imports chapter markers in the ChaptersDb.org XML format
//   More info: http://www.chapterdb.org/docs
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace HandBrakeWPF.Utilities.Input
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml;
    using System.Xml.Linq;
    using System.Xml.XPath;

    using HandBrakeWPF.Helpers;

    /// <summary>
    /// Imports chapter markers in the ChaptersDb.org XML format
    /// More info: http://www.chapterdb.org/docs
    /// </summary>
    public class ChapterImporterXml
    {
        /// <summary>
        /// Imports all chapter information from the given <see cref="filename"/> into the <see cref="chapterMap"/> dictionary.
        /// </summary>
        /// <param name="filestream">The file stream of the chapter marker file to import</param>
        /// <param name="chapterMap">The dictionary that should be populated with parsed chapter markers</param>
        public static void Import(Stream filestream, ref Dictionary<int, Tuple<string, TimeSpan>> chapterMap)
        {
            using (var reader = new StreamReader(filestream))
            {
                XDocument xDoc = XDocument.Load(reader);
                var xRoot = xDoc.Root;
                if (xRoot == null)
                    return;

                // Indexing is 1-based
                int chapterMapIdx = 1;

                // Get all chapters in the document
                var chapters = xRoot.XPathSelectElements("/Chapters/EditionEntry/ChapterAtom");
                TimeSpan prevChapterStart = TimeSpan.Zero;

                foreach (XElement chapter in chapters)
                {
                    // Extract and clean up any special XML escape characters
                    var chapterName = chapter.XPathSelectElement("ChapterDisplay/ChapterString")?.Value;
                    if (!string.IsNullOrWhiteSpace(chapterName))
                    {
                        chapterName = XmlConvert.DecodeName(chapterName);
                    }

                    var chapterStartRaw = chapter.XPathSelectElement("ChapterTimeStart")?.Value;
                    if (!string.IsNullOrWhiteSpace(chapterStartRaw))
                    {
                        // Format: 02:35:05 and 02:35:05.2957333
                        var chapterStart = TimeSpanHelper.ParseChapterTimeStart(chapterStartRaw);

                        // If we're past the first chapter in the file then calculate the duration for the previous chapter
                        if (chapterMapIdx > 1)
                        {
                            var old = chapterMap[chapterMapIdx - 1];
                            chapterMap[chapterMapIdx - 1] = new Tuple<string, TimeSpan>(
                                                                old.Item1,
                                                                chapterStart - prevChapterStart);
                        }

                        prevChapterStart = chapterStart;
                    }

                    // Save the chapter info, we calculate the duration in the next iteration (look back)
                    chapterMap[chapterMapIdx++] = new Tuple<string, TimeSpan>(chapterName, TimeSpan.Zero);
                }
            }
        }
    }
}